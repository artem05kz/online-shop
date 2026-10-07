using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Личный кабинет с настоящим входом через куку: регистрация, вход, профиль, адреса, заказы, выход.</summary>
public class AccountTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private static int _counter;

    /// <summary>Новый покупатель для каждого теста, чтобы тесты не мешали друг другу.</summary>
    private async Task<(HttpClient Client, string UserName)> RegisterNew(string? returnUrl = null)
    {
        var client = factory.Browser();
        var userName = $"покупатель{Interlocked.Increment(ref _counter)}";
        var token = await TokenFrom(client, "/accounts/register");
        var response = await Post(client, "/accounts/register" + (returnUrl is null ? "" : $"?returnUrl={Uri.EscapeDataString(returnUrl)}"), token,
            ("Input.UserName", userName), ("Input.Email", $"{Guid.NewGuid():N}@example.com"),
            ("Input.Password", "зелёный-шампунь"), ("Input.ConfirmPassword", "зелёный-шампунь"), ("Input.AcceptPrivacy", "true"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return (client, userName);
    }

    [Fact]
    public async Task Registration_signs_in_right_away()
    {
        var (client, _) = await RegisterNew();
        var profile = await client.GetAsync("/accounts/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        Assert.Contains("Личные данные", await Text(profile));
    }

    [Fact]
    public async Task Registration_shows_russian_errors()
    {
        var (_, takenName) = await RegisterNew();
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/register");

        var page = await Text(await Post(client, "/accounts/register", token,
            ("Input.UserName", takenName), ("Input.Email", "x1@example.com"), ("Input.Password", "12345678"), ("Input.ConfirmPassword", "12345679")));
        Assert.Contains("Пароли не совпадают.", page);

        page = await Text(await Post(client, "/accounts/register", token,
            ("Input.UserName", takenName), ("Input.Email", "x2@example.com"), ("Input.Password", "12345678"), ("Input.ConfirmPassword", "12345678")));
        Assert.Contains("Пользователь с таким логином уже существует.", page);
        Assert.Contains("Пароль не может состоять только из цифр.", page);

        page = await Text(await Post(client, "/accounts/register", token,
            ("Input.UserName", "ivan petrov"), ("Input.Email", "x3@example.com"), ("Input.Password", "коротко"), ("Input.ConfirmPassword", "коротко")));
        Assert.Contains("Логин может содержать только буквы, цифры", page);
    }

    [Fact]
    public async Task Login_logout_and_return_url()
    {
        var (registered, userName) = await RegisterNew();
        var token = await TokenFrom(registered, "/accounts/profile");
        await Post(registered, "/accounts/logout", token);

        var client = factory.Browser();
        var guest = await client.GetAsync("/accounts/profile");
        Assert.Equal(HttpStatusCode.Redirect, guest.StatusCode);
        Assert.Equal("/accounts/login", guest.Headers.Location!.AbsolutePath);

        token = await TokenFrom(client, "/accounts/login");
        var wrong = await Post(client, "/accounts/login", token, ("Input.UserName", userName), ("Input.Password", "не тот"));
        Assert.Contains("Неверный логин или пароль.", await Text(wrong));

        // Логин без учёта регистра, после входа возврат туда, откуда увело на вход.
        var ok = await Post(client, "/accounts/login?ReturnUrl=%2Forders%2Fmy", token,
            ("Input.UserName", userName.ToUpperInvariant()), ("Input.Password", "зелёный-шампунь"));
        Assert.Equal("/orders/my", ok.Headers.Location!.OriginalString);

        // На чужой сайт после входа не уводит.
        var fresh = factory.Browser();
        var evil = await Post(fresh, "/accounts/login?ReturnUrl=https%3A%2F%2Fevil.example", await TokenFrom(fresh, "/accounts/login"),
            ("Input.UserName", userName), ("Input.Password", "зелёный-шампунь"));
        Assert.Equal("/", evil.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task Logout_clears_cart()
    {
        var (client, _) = await RegisterNew();
        int variantId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var variant = new ProductVariant { Article = $"L{Guid.NewGuid():N}"[..20], Price = 100m, StockQty = 1 };
            db.Products.Add(new Product
            {
                Category = new Category { Brand = new Brand { Name = $"Бренд {Guid.NewGuid():N}" }, Name = "Разное" },
                Name = "Товар для выхода", Variants = [variant],
            });
            db.SaveChanges();
            variantId = variant.Id;
        }

        // На пустой корзине форм нет, токен берём со страницы профиля (там форма «Выйти»).
        var token = await TokenFrom(client, "/accounts/profile");
        await Post(client, $"/orders/cart/add/{variantId}", token);
        Assert.Contains("Товар для выхода", await Page(client, "/orders/cart"));

        await Post(client, "/accounts/logout", token);
        Assert.Contains("Корзина пуста", await Page(client, "/orders/cart"));
    }

    [Fact]
    public async Task Profile_saves_names()
    {
        var (client, _) = await RegisterNew();
        var token = await TokenFrom(client, "/accounts/profile");

        var saved = await Post(client, "/accounts/profile", token,
            ("Input.FirstName", " Айгерим "), ("Input.LastName", "Сапарова"));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var page = await Page(client, "/accounts/profile");
        Assert.Contains("Данные сохранены.", page);
        Assert.Contains("value=\"Айгерим\"", page);

        // Имя из профиля подставляется в новый адрес.
        Assert.Contains("value=\"Айгерим Сапарова\"", await Page(client, "/accounts/profile/addresses/add"));
    }

    [Fact]
    public async Task Addresses_add_edit_single_default_and_delete()
    {
        var (client, _) = await RegisterNew();
        var token = await TokenFrom(client, "/accounts/profile/addresses/add");

        (string, string)[] Fields(string label, bool isDefault, string lat = "43.652500") =>
        [
            ("Input.Label", label), ("Input.FullName", "Иван"), ("Input.Phone", "+7 700"), ("Input.City", "Актау"),
            ("Input.Street", "14 микрорайон"), ("Input.House", "5"), ("Input.Latitude", lat), ("Input.Longitude", "51.157500"),
            ("Input.IsDefault", isDefault ? "true" : "false"),
        ];

        var missing = await Post(client, "/accounts/profile/addresses/add", token, ("Input.FullName", "Иван"));
        Assert.Contains("Обязательное поле.", await Text(missing));

        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/accounts/profile/addresses/add", token, Fields("Дом", true))).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/accounts/profile/addresses/add", token, Fields("Работа", true))).StatusCode);

        int homeId, workId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var mine = db.Addresses.Where(a => a.Label == "Дом" || a.Label == "Работа").OrderByDescending(a => a.Id).Take(2).ToList();
            var work = mine.Single(a => a.Label == "Работа");
            var home = mine.Single(a => a.Label == "Дом");
            Assert.True(work.IsDefault);
            Assert.False(home.IsDefault);
            Assert.Equal(43.6525, home.Latitude);
            (homeId, workId) = (home.Id, work.Id);
        }

        var list = await Page(client, "/accounts/profile/addresses");
        Assert.True(list.IndexOf("Работа", StringComparison.Ordinal) < list.IndexOf("Дом", StringComparison.Ordinal),
            "Основной адрес должен быть первым.");

        var edited = await Post(client, $"/accounts/profile/addresses/{homeId}", token, Fields("Дача", false, lat: "не число"));
        Assert.Contains("Точка на карте не распознана", await Text(edited));
        await Post(client, $"/accounts/profile/addresses/{homeId}", token, Fields("Дача", false));
        Assert.Contains("Дача", await Page(client, "/accounts/profile/addresses"));
        Assert.Contains("value=\"43.652500\"", await Page(client, $"/accounts/profile/addresses/{homeId}"));

        await Post(client, $"/accounts/profile/addresses?handler=Delete&id={workId}", token);
        var afterDelete = await Page(client, "/accounts/profile/addresses");
        Assert.Contains("Адрес удалён.", afterDelete);
        Assert.DoesNotContain("Работа", afterDelete);

        // Чужой адрес не открыть, не изменить и не удалить.
        var (stranger, _) = await RegisterNew();
        var strangerToken = await TokenFrom(stranger, "/accounts/profile/addresses");
        var open = await stranger.GetAsync($"/accounts/profile/addresses/{homeId}");
        Assert.Equal(HttpStatusCode.Redirect, open.StatusCode);
        await Post(stranger, $"/accounts/profile/addresses?handler=Delete&id={homeId}", strangerToken);
        Assert.Contains("Адрес не найден.", await Page(stranger, "/accounts/profile/addresses"));
        Assert.Contains("Дача", await Page(client, "/accounts/profile/addresses"));
    }

    [Fact]
    public async Task Order_history_lists_own_orders_newest_first()
    {
        var (client, userName) = await RegisterNew();
        var (_, otherName) = await RegisterNew();
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var me = await db.Users.SingleAsync(u => u.UserName == userName);
            var other = await db.Users.SingleAsync(u => u.UserName == otherName);
            var variant = new ProductVariant
            {
                Article = $"H{Guid.NewGuid():N}"[..20], Price = 500m,
                Product = new Product
                {
                    Category = new Category { Brand = new Brand { Name = $"Бренд {Guid.NewGuid():N}" }, Name = "Разное" },
                    Name = "Товар для истории",
                },
            };
            OrderItem Item(string name, int qty) => new() { Variant = variant, ProductName = name, Price = 500m, Quantity = qty };
            Order Make(ApplicationUser user, OrderStatus status, params OrderItem[] items) => new()
            {
                User = user, Status = status, ContactName = "Иван", ContactPhone = "+7", City = "Актау",
                StreetAndDetails = "1", Items = [.. items],
            };

            db.Orders.Add(Make(me, OrderStatus.Completed, Item("Первый", 1)));
            await db.SaveChangesAsync();
            db.Orders.Add(Make(me, OrderStatus.Canceled,
                Item("А", 1), Item("Б", 1), Item("В", 1), Item("Г", 2), Item("Д", 1)));
            db.Orders.Add(Make(other, OrderStatus.New, Item("Чужой", 1)));
            await db.SaveChangesAsync();
        }

        var page = await Page(client, "/orders/my");
        Assert.DoesNotContain("Чужой", page);
        Assert.True(page.IndexOf("Отменён", StringComparison.Ordinal) < page.IndexOf("Выполнен", StringComparison.Ordinal),
            "Новые заказы должны быть сверху.");
        Assert.Contains("и ещё 2 поз.", page);
        Assert.Contains("Итого (6 шт.)", page);
        Assert.DoesNotContain(">Д<", page);
    }

    [Theory]
    [InlineData("zelenyi-shampun", "ivan", "", true)]
    [InlineData("12345678", "ivan", "", false)]
    [InlineData("qwerty123", "ivan", "", false)]
    [InlineData("ivanpetrov1", "ivanpetrov", "", false)]
    [InlineData("aigerim2026", "user", "aigerim@example.com", false)]
    public void Password_rules(string password, string userName, string email, bool ok)
    {
        var user = new ApplicationUser { UserName = userName, Email = email };
        Assert.Equal(ok, !PasswordRules.Check(user, password).Any());
    }
}
