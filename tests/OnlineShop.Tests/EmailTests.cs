using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Почта: обязательна при регистрации, подтверждается кодом, по коду меняется пароль, приходят письма о заказах.</summary>
public class EmailTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private const string Password = "почтовый-голубь";
    private static int _counter;

    private T Db<T>(Func<ShopDbContext, T> query)
    {
        using var scope = factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    private static string NewName() => $"почта{Interlocked.Increment(ref _counter)}";

    private async Task<(HttpClient Client, string Email, HttpResponseMessage Response)> Register(string? email = null, string? login = null)
    {
        var client = factory.Browser();
        email ??= $"{Guid.NewGuid():N}@example.com";
        var token = await TokenFrom(client, "/accounts/register");
        var response = await Post(client, "/accounts/register", token, ("Input.UserName", login ?? NewName()), ("Input.Email", email),
            ("Input.Password", Password), ("Input.ConfirmPassword", Password), ("Input.AcceptPrivacy", "true"));
        return (client, email, response);
    }

    private async Task Confirm(HttpClient client, string email, int letter = 1)
    {
        var code = await factory.Email.CodeFor(email, letter);
        var token = await TokenFrom(client, "/accounts/email");
        var confirmed = await Post(client, "/accounts/email?handler=Confirm", token, ("Code", code));
        Assert.Equal(HttpStatusCode.Redirect, confirmed.StatusCode);
    }

    private int SeedVariant() => Db(db =>
    {
        var variant = new ProductVariant { Article = $"M{Guid.NewGuid():N}"[..12], PackLabel = "1 л", Price = 1200m, StockQty = 9 };
        db.Products.Add(new Product { Category = TestDb.NewCategory($"Бренд{Guid.NewGuid():N}"[..12], "Шампуни"), Name = "Шампунь письма", Variants = [variant] });
        db.SaveChanges();
        return variant.Id;
    });

    [Fact]
    public async Task Registration_requires_unique_email_and_sends_a_code()
    {
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/register");
        var noEmail = await Post(client, "/accounts/register", token, ("Input.UserName", NewName()), ("Input.Email", ""),
            ("Input.Password", Password), ("Input.ConfirmPassword", Password), ("Input.AcceptPrivacy", "true"));
        Assert.Contains("Укажите почту", await Text(noEmail));

        var (_, email, first) = await Register();
        Assert.Equal("/accounts/email", first.Headers.Location!.OriginalString.Split('?')[0]);
        Assert.Contains("Код подтверждения", (await factory.Email.WaitFor(email)).Subject);

        // Та же почта другими буквами занята.
        var (_, _, second) = await Register(email.ToUpperInvariant());
        Assert.Contains("уже привязана к другому аккаунту", await Text(second));
    }

    [Fact]
    public async Task Code_confirms_email_wrong_code_does_not()
    {
        var (client, email, _) = await Register();
        Assert.Contains("Отправили код", await Page(client, "/accounts/email"));

        var token = await TokenFrom(client, "/accounts/email");
        var wrong = await Post(client, "/accounts/email?handler=Confirm", token, ("Code", "000000"));
        Assert.Contains("Код не подошёл", await Text(wrong));
        Assert.False(Db(db => db.Users.Single(u => u.Email == email).EmailConfirmed));

        await Confirm(client, email);
        Assert.True(Db(db => db.Users.Single(u => u.Email == email).EmailConfirmed));
        Assert.Contains("подтверждена", await Page(client, "/accounts/profile"));
    }

    [Fact]
    public async Task Guest_order_moves_to_my_orders_after_registration_in_same_browser()
    {
        var variantId = SeedVariant();
        var client = factory.Browser();
        var token = await TokenFrom(client, "/");
        await Post(client, $"/orders/cart/add/{variantId}", token);
        var placed = await Post(client, "/orders/checkout", token, ("Form.ContactName", "Гость"), ("Form.ContactPhone", "+7 702 555"),
            ("Form.IsPickup", "false"), ("Form.City", "Актау"), ("Form.StreetAndDetails", "5 мкр"), ("Form.AcceptPrivacy", "true"));
        var link = placed.Headers.Location!.OriginalString;

        // На карточке «Заказ принят» гостю предлагают аккаунт, но не заставляют.
        Assert.Contains("Создать аккаунт", await Page(client, link));

        var email = $"{Guid.NewGuid():N}@example.com";
        token = await TokenFrom(client, "/accounts/register");
        await Post(client, "/accounts/register", token, ("Input.UserName", NewName()), ("Input.Email", email),
            ("Input.Password", Password), ("Input.ConfirmPassword", Password), ("Input.AcceptPrivacy", "true"));

        var accessToken = link.Split('/').Last();
        Assert.Equal(Db(db => db.Users.Single(u => u.Email == email).Id), Db(db => db.Orders.Single(o => o.AccessToken == accessToken).UserId));
        Assert.Contains($"/orders/my/", await Page(client, "/orders/my"));
    }

    [Fact]
    public async Task Checkout_does_not_ask_for_email()
    {
        // Заказчица: почта для заказа не нужна, менеджер звонит сам. Без подтверждённой почты оформление открывается.
        var variantId = SeedVariant();
        var (client, _, _) = await Register();
        var token = await TokenFrom(client, "/");
        await Post(client, $"/orders/cart/add/{variantId}", token);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/orders/checkout")).StatusCode);
    }

    [Fact]
    public async Task Changing_email_needs_new_code_and_taken_address_is_refused()
    {
        var (_, takenEmail, _) = await Register();
        var (client, email, _) = await Register();
        await Confirm(client, email);

        var token = await TokenFrom(client, "/accounts/email");
        var taken = await Post(client, "/accounts/email?handler=Send", token, ("NewEmail", takenEmail));
        Assert.Contains("уже привязана к другому аккаунту", await Text(taken));

        var newEmail = $"new-{Guid.NewGuid():N}@example.com";
        await Post(client, "/accounts/email?handler=Send", token, ("NewEmail", newEmail));
        var user = Db(db => db.Users.Single(u => u.Email == newEmail));
        Assert.False(user.EmailConfirmed);
        // Вход не сбросился, хотя смена почты меняет отметку безопасности аккаунта.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/accounts/email")).StatusCode);

        await Confirm(client, newEmail);
        Assert.True(Db(db => db.Users.Single(u => u.Email == newEmail).EmailConfirmed));
    }

    [Fact]
    public async Task Forgot_password_sends_code_only_to_confirmed_email_and_code_sets_new_password()
    {
        var login = NewName();
        var (client, email, _) = await Register(login: login);
        var guest = factory.Browser();
        var token = await TokenFrom(guest, "/accounts/forgot-password");

        // Пока почта не подтверждена, код смены пароля на неё не уходит. Ответ тот же, что и для чужой почты.
        var unconfirmed = await Post(guest, "/accounts/forgot-password", token, ("Email", email));
        Assert.Equal("/accounts/reset-password", unconfirmed.Headers.Location!.OriginalString.Split('?')[0]);
        await Post(guest, "/accounts/forgot-password", token, ("Email", "nobody@example.com"));
        Assert.Single(factory.Email.SentTo(email));
        Assert.Empty(factory.Email.SentTo("nobody@example.com"));

        await Confirm(client, email);
        await Post(guest, "/accounts/forgot-password", token, ("Email", email));
        var letter = await factory.Email.WaitFor(email, 2);
        Assert.Contains("Код для смены пароля", letter.Subject);
        var code = await factory.Email.CodeFor(email, 2);

        token = await TokenFrom(guest, "/accounts/reset-password");
        var wrong = await Post(guest, "/accounts/reset-password", token, ("Input.Email", email), ("Input.Code", "111111"),
            ("Input.Password", "новый-пароль-1"), ("Input.ConfirmPassword", "новый-пароль-1"));
        Assert.Contains("Код не подошёл", await Text(wrong));

        var ok = await Post(guest, "/accounts/reset-password", token, ("Input.Email", email), ("Input.Code", code),
            ("Input.Password", "новый-пароль-1"), ("Input.ConfirmPassword", "новый-пароль-1"));
        Assert.Equal("/accounts/login", ok.Headers.Location!.OriginalString.Split('?')[0]);

        // Старый пароль больше не подходит, новый подходит, а использованный код второй раз не сработает.
        var fresh = factory.Browser();
        var loginToken = await TokenFrom(fresh, "/accounts/login");
        Assert.Contains("Неверный логин или пароль", await Text(await Post(fresh, "/accounts/login", loginToken, ("Input.UserName", login), ("Input.Password", Password))));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(fresh, "/accounts/login", loginToken, ("Input.UserName", login), ("Input.Password", "новый-пароль-1"))).StatusCode);
        var reused = await Post(guest, "/accounts/reset-password", token, ("Input.Email", email), ("Input.Code", code),
            ("Input.Password", "третий-пароль-1"), ("Input.ConfirmPassword", "третий-пароль-1"));
        Assert.Contains("Код не подошёл", await Text(reused));
    }

    [Fact]
    public async Task Guessing_reset_code_locks_the_account()
    {
        var (client, email, _) = await Register();
        await Confirm(client, email);
        var guest = factory.Browser();
        var token = await TokenFrom(guest, "/accounts/reset-password");
        for (var i = 0; i < 5; i++)
        {
            await Post(guest, "/accounts/reset-password", token, ("Input.Email", email), ("Input.Code", $"12345{i}"),
                ("Input.Password", "новый-пароль-1"), ("Input.ConfirmPassword", "новый-пароль-1"));
        }
        var locked = await Post(guest, "/accounts/reset-password", token, ("Input.Email", email), ("Input.Code", "123459"),
            ("Input.Password", "новый-пароль-1"), ("Input.ConfirmPassword", "новый-пароль-1"));
        Assert.Contains("Слишком много неверных попыток", await Text(locked));
    }

    [Fact]
    public async Task Buyer_gets_letters_when_order_is_placed_and_status_changes()
    {
        var variantId = SeedVariant();
        var (client, email, _) = await Register();
        await Confirm(client, email);
        var token = await TokenFrom(client, "/");
        await Post(client, $"/orders/cart/add/{variantId}", token, ("quantity", "2"));
        var placed = await Post(client, "/orders/checkout", token, ("Form.ContactName", "Иван"), ("Form.ContactPhone", "+7 700 000"),
            ("Form.City", "Актау"), ("Form.StreetAndDetails", "14 мкр, 5"));
        var orderId = placed.Headers.Location!.OriginalString.Split('/').Last();

        var letter = await factory.Email.WaitFor(email, 2);
        Assert.Equal($"Заказ №{orderId} принят", letter.Subject);
        Assert.Contains("Шампунь письма, 1 л: 2 × 1 200 ₸", letter.Text);
        Assert.Contains($"/orders/my/{orderId}", letter.Text);

        // Менеджер меняет статус в списке заказов: покупателю приходит письмо.
        var manager = factory.Browser();
        var login = NewName();
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = login };
            await users.CreateAsync(user, Password);
            await users.AddToRoleAsync(user, RoleNames.Manager);
        }
        var mt = await TokenFrom(manager, "/accounts/login");
        await Post(manager, "/accounts/login", mt, ("Input.UserName", login), ("Input.Password", Password));
        mt = await TokenFrom(manager, "/admin/orders");
        await Post(manager, "/admin/orders?handler=SaveList", mt, ("Rows[0].Id", orderId), ("Rows[0].Status", "Delivering"));

        var status = await factory.Email.WaitFor(email, 3);
        Assert.Equal($"Заказ №{orderId}: доставляется", status.Subject);
        Assert.Contains("передан курьеру", status.Text);
        Assert.Equal(OrderStatus.Delivering, Db(db => db.Orders.Single(o => o.Id == int.Parse(orderId)).Status));
    }
}
