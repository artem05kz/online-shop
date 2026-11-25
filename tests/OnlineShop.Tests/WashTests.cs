using System.Globalization;
using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Запись на мойку: без регистрации, занятое время закрывается, администратор получает запись в Telegram.</summary>
public class WashTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private const long WashAdminChat = 7_700_500;
    private static int _counter;

    // Завтра: сегодня часть времени может быть уже в прошлом.
    private static string Tomorrow => DateOnly.FromDateTime(WashSlots.Now()).AddDays(1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private T Db<T>(Func<ShopDbContext, T> query)
    {
        using var scope = factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    private static Task<HttpResponseMessage> Book(HttpClient client, string token, string wash, string time, string name, string date,
        string service = "complex", bool consent = true) =>
        Post(client, "/wash", token, ("Form.Wash", wash), ("Form.Service", service), ("Form.Date", date), ("Form.Time", time),
            ("Form.Name", name), ("Form.Phone", "8 701 555 44 33"), ("Form.Car", "Camry 123ABC"),
            ("Form.AcceptPrivacy", consent ? "true" : "false"));

    // Новый посетитель: своя кука и свой токен формы.
    private async Task<HttpResponseMessage> BookFresh(string wash, string time, string name, string date)
    {
        var client = factory.Browser();
        return await Book(client, await TokenFrom(client, "/wash"), wash, time, name, date);
    }

    [Fact]
    public async Task Page_shows_washes_services_days_and_time()
    {
        var html = await Page(factory.Browser(), "/wash");
        Assert.Contains("Запись на мойку", html);
        foreach (var wash in CarWashes.All) Assert.Contains(wash.Name, html);
        foreach (var service in CarWashes.Services) Assert.Contains(service.Name, html);
        Assert.Contains("Сегодня", html);
        Assert.Contains("name=\"Form.Time\"", html);
        // Кнопка в шапке и приглашение на главной.
        var home = await Page(factory.Browser(), "/");
        Assert.Contains("Запись на мойку", home);
        Assert.Contains("Запишитесь на автомойку онлайн", home);
    }

    [Fact]
    public async Task Booking_without_account_takes_the_time_and_goes_to_telegram()
    {
        Db(db =>
        {
            db.TelegramRecipients.Add(new TelegramRecipient { Name = "Мойка", Phone = "77015550001", ChatId = WashAdminChat, CreatedAt = DateTime.UtcNow, LinkedAt = DateTime.UtcNow });
            return db.SaveChanges();
        });
        var wash = CarWashes.All[0];
        var name = $"Клиент-{Interlocked.Increment(ref _counter)}";
        var client = factory.Browser();
        var token = await TokenFrom(client, "/wash");

        // Мест четыре: на одно время записываются четверо, пятому время уже не достаётся.
        var first = await Book(client, token, wash.Key, "10:00", name, Tomorrow);
        Assert.True(first.StatusCode == HttpStatusCode.Redirect, (int)first.StatusCode + " " + await first.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Redirect, first.StatusCode);
        var link = first.Headers.Location!.OriginalString;
        Assert.StartsWith("/wash/booking/", link);
        var page = await Page(client, link);
        Assert.Contains("Вы записаны на мойку", page);
        Assert.Contains("+7 701 555-44-33", page);
        Assert.Contains("Комплексная мойка", page);

        Assert.Equal(HttpStatusCode.Redirect, (await BookFresh(wash.Key, "10:30", name + "-2", Tomorrow)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await BookFresh(wash.Key, "10:00", name + "-3", Tomorrow)).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await BookFresh(wash.Key, "10:00", name + "-4", Tomorrow)).StatusCode);
        var fifth = await BookFresh(wash.Key, "10:00", name + "-5", Tomorrow);
        Assert.Equal(HttpStatusCode.OK, fifth.StatusCode);
        Assert.Contains("Это время уже занято", await Text(fifth));

        // На сетке 10:00 занято (четыре записи пересекаются с 10:00–11:00), 12:00 — свободно.
        var slots = await factory.Browser().GetStringAsync($"/wash/slots?wash={wash.Key}&service=complex&date={Tomorrow}");
        Assert.Contains("\"time\":\"10:00\",\"free\":false", slots);
        Assert.Contains("\"time\":\"12:00\",\"free\":true", slots);

        var message = await factory.Telegram.WaitForMessage(WashAdminChat, m => m.Text.Contains(name) && m.Text.Contains("Новая запись на мойку"));
        Assert.Contains("10:00–11:00", message.Text);
        Assert.Contains("+7 701 555-44-33", message.Text);
        Assert.Contains("Camry 123ABC", message.Text);
        // Под сообщением — кнопки «Подтвердить / Отменить» (CRM, 24.11).
        var bookingId = Db(db => db.WashBookings.Single(b => b.Name == name).Id);
        Assert.Contains($"wash:ok:{bookingId}", message.Markup);
        Assert.Contains($"wash:ask:{bookingId}", message.Markup);
        Assert.Contains($"/admin/wash/{bookingId}", message.Text);

        // Отмена по ссылке освобождает время и сообщает администратору.
        var cancelToken = await TokenFrom(client, link);
        var cancelled = await Post(client, link + "/cancel", cancelToken);
        Assert.Equal(HttpStatusCode.Redirect, cancelled.StatusCode);
        Assert.Equal(WashBookingStatus.Cancelled, Db(db => db.WashBookings.Single(b => b.Name == name).Status));
        Assert.Equal(HttpStatusCode.Redirect, (await BookFresh(wash.Key, "10:00", name + "-6", Tomorrow)).StatusCode);
        await factory.Telegram.WaitForMessage(WashAdminChat, m => m.Text.Contains(name) && m.Text.Contains("отменил"));
    }

    [Fact]
    public async Task Wrong_time_and_missing_consent_are_rejected()
    {
        var client = factory.Browser();
        var token = await TokenFrom(client, "/wash");
        var wash = CarWashes.All[0].Key;
        var name = $"Ошибка-{Interlocked.Increment(ref _counter)}";

        Assert.Contains("уже занято", await Text(await Book(client, token, wash, "06:00", name, Tomorrow)));       // до открытия
        Assert.Contains("уже занято", await Text(await Book(client, token, wash, "10:15", name, Tomorrow)));       // не на сетке
        var past = DateOnly.FromDateTime(WashSlots.Now()).AddDays(-1).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Assert.Contains("уже занято", await Text(await Book(client, token, wash, "12:00", name, past)));           // вчера
        Assert.Contains("Нужно согласие", await Text(await Book(client, token, wash, "12:00", name, Tomorrow, consent: false)));

        // Пустые имя и телефон: страница с подсказками, а не страница ошибки.
        var empty = await Post(client, "/wash", token, ("Form.Wash", wash), ("Form.Service", "complex"), ("Form.Date", Tomorrow),
            ("Form.Time", "12:00"), ("Form.Name", ""), ("Form.Phone", ""), ("Form.AcceptPrivacy", "true"));
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        Assert.Contains("Запись на мойку", await Text(empty));
        Assert.False(Db(db => db.WashBookings.Any(b => b.Name == name)));
    }

    [Fact]
    public async Task Manager_confirms_and_cancels_bookings_in_panel()
    {
        var name = $"Панель-{Interlocked.Increment(ref _counter)}";
        var client = factory.Browser();
        var token = await TokenFrom(client, "/wash");
        await Book(client, token, CarWashes.All[0].Key, "15:00", name, Tomorrow, service: "express");
        var id = Db(db => db.WashBookings.Single(b => b.Name == name).Id);

        var manager = await Manager();
        var list = await Page(manager, $"/admin/wash?date={Tomorrow}");
        Assert.Contains(name, list);
        Assert.Contains("Ждёт подтверждения", list);
        // Календарь недели (редизайн 24.11): запись видна блоком в своём дне.
        var week = await Page(manager, "/admin/wash?view=week");
        Assert.Contains(name, week);
        Assert.Contains("wash-block is-new", week);

        var adminToken = await TokenFrom(manager, $"/admin/wash?date={Tomorrow}");
        await Post(manager, $"/admin/wash?handler=Status&id={id}&status=Confirmed", adminToken);
        Assert.Equal(WashBookingStatus.Confirmed, Db(db => db.WashBookings.Single(b => b.Id == id).Status));

        // Покупатель в панель не попадает.
        var guest = await factory.Browser().GetAsync("/admin/wash");
        Assert.Equal(HttpStatusCode.Redirect, guest.StatusCode);
    }

    private async Task<HttpClient> Manager()
    {
        var login = $"мойка-менеджер{Interlocked.Increment(ref _counter)}";
        const string password = "панель-управления";
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = login };
            Assert.True((await users.CreateAsync(user, password)).Succeeded);
            await users.AddToRoleAsync(user, RoleNames.Manager);
        }
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/login");
        await Post(client, "/accounts/login", token, ("Input.UserName", login), ("Input.Password", password));
        return client;
    }

    [Fact]
    public async Task Booking_is_confirmed_from_telegram_button_by_a_recipient_only()
    {
        var name = $"Кнопка-{Interlocked.Increment(ref _counter)}";
        var client = factory.Browser();
        var token = await TokenFrom(client, "/wash");
        await Book(client, token, CarWashes.All[0].Key, "16:00", name, Tomorrow, service: "express");
        var id = Db(db => db.WashBookings.Single(b => b.Name == name).Id);
        Assert.True(Db(db => db.WashBookings.Single(b => b.Id == id).Box) > 0);

        // Чужой чат нажать не может.
        factory.Telegram.Press(9_999_001, $"wash:ok:{id}");
        await Task.Delay(300);
        Assert.Equal(WashBookingStatus.New, Db(db => db.WashBookings.Single(b => b.Id == id).Status));

        factory.Telegram.Press(WashAdminChat, $"wash:ok:{id}");
        await factory.Telegram.WaitForMessage(WashAdminChat, m => m.Text.Contains("Подтверждена") && m.Text.Contains(name));
        var booking = Db(db => db.WashBookings.Single(b => b.Id == id));
        Assert.Equal(WashBookingStatus.Confirmed, booking.Status);
        Assert.Equal("Мойка", booking.ConfirmedBy);
    }

    [Fact]
    public async Task Manager_adds_booking_by_phone_and_edits_price_and_payment()
    {
        var name = $"Звонок-{Interlocked.Increment(ref _counter)}";
        var manager = await Manager();
        var token = await TokenFrom(manager, $"/admin/wash/add?date={Tomorrow}&time=18:00&box=2");
        var added = await Post(manager, "/admin/wash/add", token,
            ("Input.Date", Tomorrow), ("Input.Time", "18:00"), ("Input.Service", "express"), ("Input.Box", "2"),
            ("Input.Name", name), ("Input.Phone", "+7 701 222-33-44"), ("Input.Source", "phone"), ("Input.Price", "4000"));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        var booking = Db(db => db.WashBookings.Single(b => b.Name == name));
        Assert.Equal(WashBookingStatus.Confirmed, booking.Status);
        Assert.Equal(2, booking.Box);
        Assert.Equal(4000m, booking.Price);
        Assert.Equal("phone", booking.Source);

        // Второй клиент на то же место и время не встанет.
        var busy = await Post(manager, "/admin/wash/add", token,
            ("Input.Date", Tomorrow), ("Input.Time", "18:00"), ("Input.Service", "express"), ("Input.Box", "2"),
            ("Input.Name", name + "-2"), ("Input.Phone", "+7 701 222-33-45"), ("Input.Source", "phone"));
        Assert.Equal(HttpStatusCode.OK, busy.StatusCode);
        Assert.Contains("Место 2 в это время занято", await Text(busy));

        // Карточка: календарь дня показывает запись, отметка оплаты и статус «Приехал».
        Assert.Contains(name, await Page(manager, $"/admin/wash?date={Tomorrow}"));
        var card = await Page(manager, $"/admin/wash/{booking.Id}");
        Assert.Contains("Визиты по этому номеру", card);
        token = await TokenFrom(manager, $"/admin/wash/{booking.Id}");
        await Post(manager, $"/admin/wash/{booking.Id}", token,
            ("Input.Date", Tomorrow), ("Input.Time", "18:00"), ("Input.Service", "express"), ("Input.Box", "2"),
            ("Input.Name", name), ("Input.Phone", "+7 701 222-33-44"), ("Input.Source", "phone"), ("Input.Price", "4500"),
            ("Input.IsPaid", "true"), ("Input.PaymentMethod", "kaspi"), ("Input.ManagerNote", "постоянный"));
        await Post(manager, $"/admin/wash/{booking.Id}?handler=Status&status=Arrived", token);
        booking = Db(db => db.WashBookings.Single(b => b.Id == booking.Id));
        Assert.Equal(4500m, booking.Price);
        Assert.True(booking.IsPaid);
        Assert.Equal("kaspi", booking.PaymentMethod);
        Assert.Equal(WashBookingStatus.Arrived, booking.Status);
    }

    [Fact]
    public async Task Clients_page_groups_bookings_by_phone_and_report_counts_money()
    {
        var name = $"Клиент-{Interlocked.Increment(ref _counter)}";
        var phone = $"7701{Random.Shared.Next(1_000_000, 9_999_999)}";
        var past = DateOnly.FromDateTime(WashSlots.Now()).AddDays(-3).ToDateTime(new TimeOnly(11, 0));
        Db(db =>
        {
            WashBooking B(DateTime at, WashBookingStatus status, decimal? price, bool paid, string method = "") => new()
            {
                WashKey = CarWashes.All[0].Key, WashName = CarWashes.All[0].Name, ServiceKey = "complex", ServiceName = "Комплекс",
                Minutes = 60, StartsAt = at, Name = name, Phone = phone, Car = "Kia Rio", Status = status, CreatedAt = DateTime.UtcNow,
                Price = price, IsPaid = paid, PaymentMethod = method, Source = WashSources.WhatsApp,
            };
            db.WashBookings.AddRange(
                B(past, WashBookingStatus.Done, 5000m, true, "kaspi"),
                B(past.AddDays(1), WashBookingStatus.Done, 5000m, true, "cash"),
                B(past.AddDays(2), WashBookingStatus.Done, 6000m, true, "kaspi"),
                B(past.AddDays(-1), WashBookingStatus.NoShow, null, false));
            return db.SaveChanges();
        });
        var manager = await Manager();

        var list = await Page(manager, "/admin/wash/clients?show=regular&q=" + Uri.EscapeDataString(name));
        Assert.Contains(name, list);
        Assert.Contains("постоянный", list);
        Assert.Contains($"/admin/wash/clients/{phone}", list);
        Assert.Contains(name, await Page(manager, "/admin/wash/clients?q=" + phone[^4..]));

        var card = await Page(manager, $"/admin/wash/clients/{phone}");
        Assert.Contains("Постоянный клиент", card);
        Assert.Contains("Не приехал 1 раз", card);
        Assert.Contains("Kia Rio", card);
        Assert.Contains($"/admin/wash/add?name={Uri.EscapeDataString(name)}&phone={phone}", card);

        var again = await Page(manager, $"/admin/wash/add?name={Uri.EscapeDataString(name)}&phone={phone}&car=Kia");
        Assert.Contains($"value=\"{name}\"", again);

        Assert.Equal(HttpStatusCode.NotFound, (await manager.GetAsync("/admin/wash/clients/70000000000")).StatusCode);

        var report = await Page(manager, "/admin/reports?period=30d");
        Assert.Contains("оплачено за мойку", report);
        Assert.Contains("Как платят", report);
        Assert.Contains("WhatsApp", report);
    }
}
