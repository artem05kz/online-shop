using System.Net;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>
/// Корзина и вход хранятся в базе: перезапуск сайта (новая фабрика на той же базе) их не теряет.
/// Раньше корзина жила в памяти сервера и пропадала при каждом обновлении сайта.
/// </summary>
public class CartStorageTests
{
    private const string Password = "зелёный-шампунь";

    private static int Seed(TempShopFactory site)
    {
        using var scope = site.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var variant = new ProductVariant { Article = "CART1", PackLabel = "1 л", Price = 1500m, StockQty = 5 };
        db.Products.Add(new Product
        {
            Category = new Category { Brand = new Brand { Name = "Grass" }, Name = "Шампуни" },
            Name = "Шампунь для корзины", Variants = [variant],
        });
        db.SaveChanges();
        return variant.Id;
    }

    private static async Task Register(HttpClient client, string userName)
    {
        var token = await TokenFrom(client, "/accounts/register");
        var response = await Post(client, "/accounts/register", token, ("Input.UserName", userName), ("Input.Email", $"{Guid.NewGuid():N}@example.com"),
            ("Input.Password", Password), ("Input.ConfirmPassword", Password), ("Input.AcceptPrivacy", "true"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    private static async Task AddToCart(HttpClient client, int variantId, int quantity)
    {
        var token = await TokenFrom(client, "/");
        await Post(client, $"/orders/cart/add/{variantId}", token, ("quantity", quantity.ToString()));
    }

    /// <summary>Куки браузера, чтобы «тот же браузер» пришёл на перезапущенный сайт.</summary>
    private static async Task<string> CookiesAfterLogin(HttpClient client, string userName)
    {
        var token = await TokenFrom(client, "/accounts/login");
        var response = await Post(client, "/accounts/login", token, ("Input.UserName", userName), ("Input.Password", Password));
        return string.Join("; ", response.Headers.GetValues("Set-Cookie").Select(c => c.Split(';')[0]));
    }

    [Fact]
    public async Task Cart_and_sign_in_survive_site_restart()
    {
        var name = Guid.NewGuid().ToString("N")[..12];
        string cookies;
        using (var first = new TempShopFactory { DataName = name, KeepData = true })
        {
            var variantId = Seed(first);
            var client = first.Browser();
            await Register(client, "перезапуск");
            await AddToCart(client, variantId, 3);
            await Post(client, "/accounts/logout", await TokenFrom(client, "/"));
            cookies = await CookiesAfterLogin(first.Browser(), "перезапуск");
        }

        using var second = new TempShopFactory { DataName = name };
        var browser = second.Browser();
        browser.DefaultRequestHeaders.Add("Cookie", cookies);

        // Вход сохранился: ключи подписи кук лежат в базе, а не в памяти прежнего процесса.
        var profile = await browser.GetAsync("/accounts/profile");
        Assert.Equal(HttpStatusCode.OK, profile.StatusCode);
        // На Windows ASP.NET и без базы хранит ключи в профиле пользователя, поэтому проверяем
        // прямо: ключ лежит в базе. В контейнере на Linux профиля нет, там спасает только база.
        using (var scope = second.Services.CreateScope())
        {
            Assert.NotEmpty(scope.ServiceProvider.GetRequiredService<ShopDbContext>().DataProtectionKeys);
        }
        // И корзина, положенная до выхода и перезапуска, на месте.
        var cart = await Page(browser, "/orders/cart");
        Assert.Contains("Шампунь для корзины", cart);
        Assert.Contains("4 500 ₸", cart);
    }

    [Fact]
    public async Task Guest_cart_joins_account_cart_on_login_and_logout_empties_browser()
    {
        using var site = new TempShopFactory();
        var variantId = Seed(site);
        var account = site.Browser();
        await Register(account, "слияние");
        await AddToCart(account, variantId, 1);
        await Post(account, "/accounts/logout", await TokenFrom(account, "/"));
        Assert.Contains("Корзина пуста", await Page(account, "/orders/cart"));

        var guest = site.Browser();
        await AddToCart(guest, variantId, 2);
        Assert.Contains("3 000 ₸", await Page(guest, "/orders/cart"));
        var token = await TokenFrom(guest, "/accounts/login");
        await Post(guest, "/accounts/login", token, ("Input.UserName", "слияние"), ("Input.Password", Password));

        // 1 штука из корзины покупателя и 2 штуки из корзины гостя.
        Assert.Contains("4 500 ₸", await Page(guest, "/orders/cart"));
        using var scope = site.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var item = Assert.Single(db.CartItems);
        Assert.Equal(3, item.Quantity);
        Assert.StartsWith("u:", item.OwnerKey);
    }
}
