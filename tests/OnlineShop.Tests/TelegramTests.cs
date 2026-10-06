using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services.Telegram;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Уведомления в Telegram: список номеров в панели, подключение через бота, рассылка.</summary>
public class TelegramTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private const string Password = "панель-управления";
    private static int _counter;

    private static async Task<HttpClient> Manager(TempShopFactory site)
    {
        var login = $"менеджер-тг{Interlocked.Increment(ref _counter)}";
        using (var scope = site.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = login };
            Assert.True((await users.CreateAsync(user, Password)).Succeeded);
            await users.AddToRoleAsync(user, RoleNames.Manager);
        }
        var client = site.Browser();
        var token = await TokenFrom(client, "/accounts/login");
        await Post(client, "/accounts/login", token, ("Input.UserName", login), ("Input.Password", Password));
        return client;
    }

    private T Db<T>(Func<ShopDbContext, T> query)
    {
        using var scope = factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    private void AddRecipient(string phone, long? chatId) => Db(db =>
    {
        db.TelegramRecipients.Add(new TelegramRecipient
        {
            Phone = phone, ChatId = chatId, CreatedAt = DateTime.UtcNow,
            LinkedAt = chatId is null ? null : DateTime.UtcNow,
        });
        return db.SaveChanges();
    });

    [Theory]
    [InlineData("+7 (701) 123-45-67", "77011234567")]
    [InlineData("8 701 123 45 67", "77011234567")]
    [InlineData("7011234567", "77011234567")]
    [InlineData("77011234567", "77011234567")]
    [InlineData("12345", "")]
    [InlineData("", "")]
    public void Phone_numbers_compare_however_they_are_written(string raw, string expected)
    {
        Assert.Equal(expected, PhoneNumbers.Normalize(raw));
    }

    [Fact]
    public void Contact_update_from_telegram_is_read_with_snake_case_fields()
    {
        // Ответ getUpdates в том виде, в каком его присылает Telegram.
        const string json = """
            [{"update_id": 811, "message": {"message_id": 5, "date": 1791200000,
              "from": {"id": 42, "is_bot": false, "first_name": "Марат"},
              "chat": {"id": 42, "type": "private", "first_name": "Марат"},
              "contact": {"phone_number": "77015554433", "first_name": "Марат", "user_id": 42}}}]
            """;
        var update = Assert.Single(System.Text.Json.JsonSerializer.Deserialize<List<TelegramUpdate>>(json, TelegramApi.Json)!);

        Assert.Equal(811, update.UpdateId);
        Assert.Equal((42L, "private"), (update.Message!.Chat.Id, update.Message.Chat.Type));
        Assert.Equal(42, update.Message.From!.Id);
        Assert.Equal(("77015554433", 42L), (update.Message.Contact!.PhoneNumber, update.Message.Contact.UserId));
    }

    [Fact]
    public void Order_message_lists_items_total_and_escapes_customer_text()
    {
        var order = new Order
        {
            Id = 12, ContactName = "Иван <Петров>", ContactPhone = "+7 700 000-00-00", City = "Актау",
            StreetAndDetails = "14 мкр, дом 5", Comment = "A & B",
            CreatedAt = new DateTime(2026, 10, 6, 9, 30, 0, DateTimeKind.Utc),
            Items =
            [
                new OrderItem { ProductName = "Шампунь", PackLabel = "1 л", Price = 1890m, Quantity = 2 },
                new OrderItem { ProductName = "Воск", Price = 500m, Quantity = 1 },
            ],
        };

        var text = OrderMessage.Build(order, "https://shop.kz/admin/orders/12");

        Assert.StartsWith("<b>Новый заказ №12</b>\n06.10.2026, 14:30", text);
        Assert.Contains("Шампунь, 1 л: 2 × 1 890 ₸ = 3 780 ₸", text);
        Assert.Contains("Воск: 1 × 500 ₸", text);
        Assert.Contains("<b>Итого: 4 280 ₸</b>", text);
        Assert.Contains("Получатель: Иван &lt;Петров&gt;", text);
        Assert.Contains("Комментарий: A &amp; B", text);
        Assert.EndsWith("Открыть в панели: https://shop.kz/admin/orders/12", text);
    }

    [Fact]
    public async Task Manager_adds_number_and_person_connects_by_sharing_own_contact()
    {
        var client = await Manager(factory);
        var token = await TokenFrom(client, "/admin/telegram");

        var added = await Post(client, "/admin/telegram?handler=Add", token,
            ("Input.Phone", "8 (701) 555-44-33"), ("Input.Name", "Марат"));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Equal("77015554433", Db(db => db.TelegramRecipients.Single(r => r.Name == "Марат").Phone));

        var duplicate = await Post(client, "/admin/telegram?handler=Add", token, ("Input.Phone", "+7 701 5554433"));
        Assert.Contains("уже есть в списке", await Text(duplicate));
        var wrong = await Post(client, "/admin/telegram?handler=Add", token, ("Input.Phone", "123"));
        Assert.Contains("не похоже на номер", await Text(wrong));

        var page = await Page(client, "/admin/telegram");
        Assert.Contains("+7 701 555-44-33", page);
        Assert.Contains("Ждёт «Старт» в боте", page);
        Assert.Contains("https://t.me/grass_test_bot", page);

        // «Старт»: бот просит номер кнопкой.
        const long chat = 501;
        factory.Telegram.Write(chat, "/start");
        Assert.Contains("request_contact", (await factory.Telegram.WaitForMessage(chat, 1)).Markup);

        // Чужой контакт из записной книжки не принимается.
        factory.Telegram.Write(chat, contact: new TelegramContact("+77015554433", UserId: 999));
        Assert.Contains("чужой контакт", (await factory.Telegram.WaitForMessage(chat, 2)).Text);

        // Свой номер, которого нет в списке.
        factory.Telegram.Write(chat, contact: new TelegramContact("77770000000", UserId: chat));
        Assert.Contains("нет в списке", (await factory.Telegram.WaitForMessage(chat, 3)).Text);
        Assert.Null(Db(db => db.TelegramRecipients.Single(r => r.Name == "Марат").ChatId));

        factory.Telegram.Write(chat, contact: new TelegramContact("+77015554433", UserId: chat));
        var done = await factory.Telegram.WaitForMessage(chat, 4);
        Assert.StartsWith("Готово, Марат.", done.Text);
        Assert.Contains("remove_keyboard", done.Markup);
        Assert.Equal(chat, Db(db => db.TelegramRecipients.Single(r => r.Name == "Марат").ChatId));
        Assert.Contains("Подключён", await Page(client, "/admin/telegram"));

        // В группе бот молчит: номер кнопкой там не отправить.
        factory.Telegram.Write(-100, "/start", chatType: "group");

        factory.Telegram.Write(chat, "/stop");
        Assert.Contains("отключены", (await factory.Telegram.WaitForMessage(chat, 5)).Text);
        Assert.Null(Db(db => db.TelegramRecipients.Single(r => r.Name == "Марат").ChatId));
        Assert.Empty(factory.Telegram.SentTo(-100));
    }

    [Fact]
    public async Task Test_message_goes_to_connected_chats_and_drops_blocked_ones()
    {
        AddRecipient("77010000601", 601);
        AddRecipient("77010000602", 602);
        AddRecipient("77010000603", null);
        factory.Telegram.Fail(602, 403);

        var client = await Manager(factory);
        var token = await TokenFrom(client, "/admin/telegram");
        await Post(client, "/admin/telegram?handler=Test", token);

        Assert.Contains("Пробное сообщение", (await factory.Telegram.WaitForMessage(601)).Text);
        Assert.Contains("Не дошло: 1", await Page(client, "/admin/telegram"));
        // Заблокировавший бота остаётся в списке, но снова ждёт «Старт».
        Assert.Null(Db(db => db.TelegramRecipients.Single(r => r.Phone == "77010000602").ChatId));
    }

    [Fact]
    public async Task Without_token_panel_explains_and_orders_are_not_queued()
    {
        using var site = new TempShopFactory { BotToken = "" };
        var client = await Manager(site);

        var page = await Page(client, "/admin/telegram");
        Assert.Contains("Бот не подключён", page);
        Assert.DoesNotContain("Отправить пробное сообщение", page);

        var alerts = site.Services.GetRequiredService<OrderAlerts>();
        alerts.Enqueue(1, "http://localhost");
        Assert.False(alerts.Reader.TryRead(out _));
    }
}
