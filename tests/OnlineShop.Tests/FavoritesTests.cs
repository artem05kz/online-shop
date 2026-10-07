using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Избранное: у гостя в куке, у покупателя в базе, при входе отмеченное гостем переносится.</summary>
public class FavoritesTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private const string Password = "зелёный-шампунь";
    private static int _counter;

    private (int Visible, int Hidden, string Slug) Seed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var n = Interlocked.Increment(ref _counter);
        var category = new Category { Brand = new Brand { Name = $"Избр{n}" }, Name = "Полироли" };
        var visible = new Product
        {
            Category = category, Name = $"Полироль {n}",
            Variants = [new ProductVariant { Article = $"FAV{n}", Price = 990m, StockQty = 2 }],
        };
        var hidden = new Product { Category = category, Name = $"Снятая {n}", IsActive = false };
        db.Products.AddRange(visible, hidden);
        db.SaveChanges();
        return (visible.Id, hidden.Id, visible.Slug);
    }

    private static async Task<(bool Active, int Count)> Toggle(HttpClient client, int productId, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, $"/favorites/toggle/{productId}")
        {
            Content = new FormUrlEncodedContent([new("__RequestVerificationToken", token)]),
        };
        request.Headers.Accept.ParseAdd("application/json");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (json.RootElement.GetProperty("active").GetBoolean(), json.RootElement.GetProperty("count").GetInt32());
    }

    private async Task<string> Register(HttpClient client)
    {
        var userName = $"избранное{Interlocked.Increment(ref _counter)}";
        var token = await TokenFrom(client, "/accounts/register");
        var response = await Post(client, "/accounts/register", token, ("Input.UserName", userName), ("Input.Email", ""),
            ("Input.Password", Password), ("Input.ConfirmPassword", Password), ("Input.AcceptPrivacy", "true"));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return userName;
    }

    private List<int> SavedFor(string userName)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        return db.Favorites.Where(f => f.User.UserName == userName).Select(f => f.ProductId).ToList();
    }

    [Fact]
    public async Task Guest_marks_product_with_heart_and_sees_it_on_favorites_page()
    {
        var (visible, hidden, slug) = Seed();
        var guest = factory.Browser();
        var token = await TokenFrom(guest, $"/product/{slug}");
        Assert.Contains("В избранное", await Page(guest, $"/product/{slug}"));
        Assert.Contains("Здесь пока пусто", await Page(guest, "/favorites"));

        Assert.Equal((true, 1), await Toggle(guest, visible, token));
        var page = await Page(guest, "/favorites");
        Assert.Contains($"Полироль", page);
        Assert.Contains("Избранное сохранено в этом браузере", page);
        Assert.Contains("В избранном", await Page(guest, $"/product/{slug}"));

        // Снятый с продажи товар отметить нельзя.
        var refused = await Post(guest, $"/favorites/toggle/{hidden}", token);
        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);

        Assert.Equal((false, 0), await Toggle(guest, visible, token));
        Assert.Contains("Здесь пока пусто", await Page(guest, "/favorites"));
    }

    [Fact]
    public async Task Form_without_script_returns_to_the_same_page()
    {
        var (visible, _, slug) = Seed();
        var guest = factory.Browser();
        var token = await TokenFrom(guest, $"/product/{slug}");

        var response = await Post(guest, $"/favorites/toggle/{visible}", token, ("next", $"/product/{slug}"));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal($"/product/{slug}", Uri.UnescapeDataString(response.Headers.Location!.OriginalString));
    }

    [Fact]
    public async Task Guest_favorites_move_to_account_on_registration_and_stay_after_logout()
    {
        var (first, _, slug) = Seed();
        var (second, _, _) = Seed();
        var client = factory.Browser();
        var token = await TokenFrom(client, $"/product/{slug}");
        await Toggle(client, first, token);

        var userName = await Register(client);
        Assert.Equal(new[] { first }, SavedFor(userName).ToArray());

        token = await TokenFrom(client, $"/product/{slug}");
        Assert.Equal((true, 2), await Toggle(client, second, token));
        Assert.Equal(new[] { first, second }.Order().ToArray(), SavedFor(userName).Order().ToArray());

        // Выход и вход с другого «устройства»: избранное на месте, потому что оно в базе.
        var other = factory.Browser();
        var loginToken = await TokenFrom(other, "/accounts/login");
        await Post(other, "/accounts/login", loginToken, ("Input.UserName", userName), ("Input.Password", Password));
        var page = await Page(other, "/favorites");
        Assert.DoesNotContain("сохранено в этом браузере", page);
        Assert.Equal(2, page.Split("class=\"product-card\"").Length - 1);
    }

    [Fact]
    public async Task Guest_favorites_merge_on_login_without_duplicates()
    {
        var (first, _, slug) = Seed();
        var (second, _, _) = Seed();
        var account = factory.Browser();
        var userName = await Register(account);
        var token = await TokenFrom(account, $"/product/{slug}");
        await Toggle(account, first, token);

        var guest = factory.Browser();
        token = await TokenFrom(guest, $"/product/{slug}");
        await Toggle(guest, first, token);
        await Toggle(guest, second, token);
        var loginToken = await TokenFrom(guest, "/accounts/login");
        await Post(guest, "/accounts/login", loginToken, ("Input.UserName", userName), ("Input.Password", Password));

        Assert.Equal(new[] { first, second }.Order().ToArray(), SavedFor(userName).Order().ToArray());
    }
}
