using Microsoft.Extensions.Logging.Abstractions;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services.Telegram;

namespace OnlineShop.Tests;

/// <summary>Напоминания мойки (CRM, шаг 2): «ждёт подтверждения 30 минут» и вечерний список на завтра — по одному разу.</summary>
public class WashReminderTests
{
    private const long Chat = 7_700_900;

    private static WashBooking Booking(string name, DateTime startsLocal, WashBookingStatus status, DateTime createdUtc) => new()
    {
        WashKey = CarWashes.All[0].Key, WashName = CarWashes.All[0].Name,
        ServiceKey = CarWashes.Services[0].Key, ServiceName = CarWashes.Services[0].Name, Minutes = 60,
        StartsAt = startsLocal, Name = name, Phone = "77015550101", Status = status, CreatedAt = createdUtc,
    };

    [Fact]
    public async Task Reminds_about_waiting_and_tomorrow_bookings_once()
    {
        using var test = new TestDb();
        var evening = Format.FromLocal(new DateTime(2027, 3, 10, 18, 30, 0));
        var tomorrow = new DateTime(2027, 3, 11);
        using (var db = test.Create())
        {
            db.TelegramRecipients.Add(new TelegramRecipient { Name = "Мойка", Phone = "77015550001", ChatId = Chat, CreatedAt = evening, LinkedAt = evening });
            db.WashBookings.AddRange(
                Booking("Ждёт-давно", tomorrow.AddHours(10), WashBookingStatus.New, evening.AddMinutes(-40)),
                Booking("Ждёт-недавно", tomorrow.AddHours(12), WashBookingStatus.New, evening.AddMinutes(-10)),
                Booking("Послезавтра", tomorrow.AddDays(1).AddHours(10), WashBookingStatus.Confirmed, evening.AddDays(-1)),
                Booking("Отменил", tomorrow.AddHours(14), WashBookingStatus.Cancelled, evening.AddDays(-1)));
            await db.SaveChangesAsync();
        }

        var api = new FakeTelegramApi();
        WashReminders Reminders(OnlineShop.Web.Data.ShopDbContext db) =>
            new(db, new TelegramNotifier(db, api, NullLogger<TelegramNotifier>.Instance));

        using (var db = test.Create())
        {
            var result = await Reminders(db).RunAsync(evening, "https://site.test", CancellationToken.None);
            Assert.Equal(new WashReminders.Result(1, 2), result);
        }
        var sent = api.SentTo(Chat);
        Assert.Equal(2, sent.Count);
        Assert.Contains("ждёт подтверждения уже 30 минут", sent[0].Text);
        Assert.Contains("Ждёт-давно", sent[0].Text);
        Assert.Contains("wash:ok:", sent[0].Markup);
        Assert.Contains("Завтра на мойке: 2 записи", sent[1].Text);
        Assert.Contains("wa.me/77015550101?text=", sent[1].Text);
        Assert.DoesNotContain("Послезавтра", sent[1].Text);
        Assert.DoesNotContain("Отменил", sent[1].Text);

        // Второй проход ничего не повторяет.
        using (var db = test.Create())
        {
            Assert.Equal(new WashReminders.Result(0, 0), await Reminders(db).RunAsync(evening.AddMinutes(1), null, CancellationToken.None));
        }
        Assert.Equal(2, api.SentTo(Chat).Count);
    }

    [Fact]
    public async Task Tomorrow_list_waits_for_the_evening()
    {
        using var test = new TestDb();
        var afternoon = Format.FromLocal(new DateTime(2027, 3, 10, 15, 0, 0));
        using (var db = test.Create())
        {
            db.WashBookings.Add(Booking("Завтра", new DateTime(2027, 3, 11, 10, 0, 0), WashBookingStatus.Confirmed, afternoon.AddDays(-1)));
            await db.SaveChangesAsync();
        }
        using (var db = test.Create())
        {
            var api = new FakeTelegramApi();
            var result = await new WashReminders(db, new TelegramNotifier(db, api, NullLogger<TelegramNotifier>.Instance))
                .RunAsync(afternoon, null, CancellationToken.None);
            Assert.Equal(0, result.Tomorrow);
        }
    }

    [Fact]
    public void Client_reminder_text_has_day_time_and_service()
    {
        var b = Booking("Айгерим", new DateTime(2027, 3, 11, 14, 30, 0), WashBookingStatus.Confirmed, DateTime.UtcNow);
        var text = BookingMessage.ClientReminder(b);
        Assert.Contains("Айгерим", text);
        Assert.Contains("11 марта в 14:30", text);
        Assert.StartsWith("https://wa.me/77015550101?text=", BookingMessage.WhatsAppReminder(b));
    }

    [Fact]
    public void Client_is_awaited_for_15_minutes_then_late()
    {
        var start = new DateTime(2027, 3, 11, 20, 30, 0);
        var b = Booking("Опоздун", start, WashBookingStatus.Confirmed, DateTime.UtcNow);
        Assert.False(b.IsAwaited(start.AddMinutes(-1)));
        Assert.True(b.IsAwaited(start.AddMinutes(3)));
        Assert.Equal(12 * 60, b.WaitSecondsLeft(start.AddMinutes(3)));
        Assert.False(b.IsLate(start.AddMinutes(14)));
        Assert.True(b.IsLate(start.AddMinutes(15)));
        Assert.Equal(18, b.LateMinutes(start.AddMinutes(18)));
        b.Status = WashBookingStatus.Arrived;
        Assert.False(b.IsLate(start.AddMinutes(30)));
        Assert.False(b.IsAwaited(start.AddMinutes(3)));
    }

    [Fact]
    public void Car_ready_message_goes_to_whatsapp()
    {
        var b = Booking("Айгерим", new DateTime(2027, 3, 11, 14, 30, 0), WashBookingStatus.Done, DateTime.UtcNow);
        Assert.Contains("Айгерим", BookingMessage.CarReady(b));
        Assert.Contains("готова", BookingMessage.CarReady(b));
        Assert.StartsWith("https://wa.me/77015550101?text=", BookingMessage.WhatsAppCarReady(b));
    }
}
