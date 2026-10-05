using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Tests;

/// <summary>
/// Приложение целиком: настоящие запросы, куки, сессия и защита форм. Входа на сайте пока нет
/// (этап 3), поэтому покупатель в тестах «входит» заголовком X-Test-User.
/// </summary>
public class StorefrontTests : IClassFixture<StorefrontTests.ShopFactory>
{
    private readonly ShopFactory _factory;

    public StorefrontTests(ShopFactory factory)
    {
        _factory = factory;
        _factory.Seed();
    }

    public sealed class ShopFactory : WebApplicationFactory<Program>
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "online-shop-tests", Guid.NewGuid().ToString("N"));

        public int BuyerId { get; private set; }
        public int OtherBuyerId { get; private set; }
        public int AddressId { get; private set; }
        public int InStockVariantId { get; private set; }
        public int HiddenVariantId { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            Directory.CreateDirectory(_dir);
            builder.UseSetting("ConnectionStrings:Shop", $"Data Source={Path.Combine(_dir, "shop.db")}");
            builder.UseSetting("Media:Root", Path.Combine(_dir, "media"));
            // Подменяем только проверку входа: гостя сайт по-прежнему отправляет на страницу входа.
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.Scheme, _ => { });
                services.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = TestAuth.Scheme);
            });
        }

        public void Seed()
        {
            using var scope = Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            if (db.Brands.Any())
            {
                return;
            }

            var category = new Category { Brand = new Brand { Name = "Grass" }, Name = "Автошампуни" };
            var visible = new ProductVariant { Article = "100", PackLabel = "1 л", Price = 1890m, StockQty = 5 };
            var hidden = new ProductVariant { Article = "101", PackLabel = "20 л", Price = 9000m, StockQty = 5, IsActive = false };
            db.Products.Add(new Product { Category = category, Name = "Шампунь Тест", Variants = [visible, hidden] });
            db.Products.Add(new Product { Category = category, Name = "Снятый товар", IsActive = false });

            var buyer = new ApplicationUser { UserName = "buyer", NormalizedUserName = "BUYER", FirstName = "Иван" };
            buyer.Addresses.Add(new Address
            {
                FullName = "Иван Петров", Phone = "+7 700 000-00-00", Street = "14 микрорайон", House = "5",
                Apartment = "12", IsDefault = true,
            });
            var other = new ApplicationUser { UserName = "other", NormalizedUserName = "OTHER" };
            db.Users.AddRange(buyer, other);
            db.SaveChanges();

            BuyerId = buyer.Id;
            OtherBuyerId = other.Id;
            AddressId = buyer.Addresses[0].Id;
            InStockVariantId = visible.Id;
            HiddenVariantId = hidden.Id;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            try
            {
                Directory.Delete(_dir, recursive: true);
            }
            catch (IOException)
            {
                // SQLite может ещё держать файл: временная папка не стоит падения тестов.
            }
        }
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Scheme = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var id))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], Scheme);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme)));
        }
    }

    private HttpClient Client(int? userId = null)
    {
        var client = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        }
        return client;
    }

    // Текст страницы так, как его видит покупатель: коды вроде &#xA0; уже раскодированы.
    private static async Task<string> Page(HttpClient client, string url) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(url));

    // Защита от подделки запросов: форму можно отправить только с токеном со страницы сайта.
    private static async Task<string> TokenFrom(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    private static Task<HttpResponseMessage> Post(HttpClient client, string url, string token, params (string, string)[] fields) =>
        client.PostAsync(url, new FormUrlEncodedContent(
            fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))
                .Append(new KeyValuePair<string, string>("__RequestVerificationToken", token))));

    [Theory]
    [InlineData("/")]
    [InlineData("/search?q=шампунь")]
    [InlineData("/catalog/grass/автошампуни")]
    [InlineData("/product/шампунь-тест")]
    [InlineData("/addresses")]
    [InlineData("/orders/cart")]
    public async Task Storefront_pages_open(string url)
    {
        var response = await Client().GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("/product/снятый-товар")]
    [InlineData("/catalog/grass/нет-такой")]
    [InlineData("/no/such/page")]
    public async Task Missing_and_hidden_pages_answer_404_with_own_page(string url)
    {
        var response = await Client().GetAsync(url);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Такой страницы нет", WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync()));
    }

    [Fact]
    public async Task Product_page_shows_only_visible_packs()
    {
        var html = await Page(Client(), "/product/шампунь-тест");
        Assert.Contains("1 890 ₸", html);
        Assert.DoesNotContain("9 000 ₸", html);
    }

    [Fact]
    public async Task Cart_add_update_and_remove()
    {
        var client = Client();
        var token = await TokenFrom(client, "/product/шампунь-тест");

        var added = await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token,
            ("quantity", "2"), ("next", "/product/шампунь-тест"));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Equal("/product/шампунь-тест", Uri.UnescapeDataString(added.Headers.Location!.OriginalString));

        var cart = await Page(client, "/orders/cart");
        Assert.Contains("3 780 ₸", cart);
        Assert.Contains("«Шампунь Тест» в корзине.", cart);

        await Post(client, $"/orders/cart/update/{_factory.InStockVariantId}", token, ("quantity", "3"));
        Assert.Contains("5 670 ₸", await Page(client, "/orders/cart"));

        await Post(client, $"/orders/cart/remove/{_factory.InStockVariantId}", token);
        Assert.Contains("Корзина пуста", await Page(client, "/orders/cart"));
    }

    [Fact]
    public async Task Cart_rejects_hidden_packs_foreign_redirects_and_missing_token()
    {
        var client = Client();
        var token = await TokenFrom(client, "/product/шампунь-тест");

        var hidden = await Post(client, $"/orders/cart/add/{_factory.HiddenVariantId}", token);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);

        var foreign = await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token, ("next", "https://evil.example/"));
        Assert.Equal("/orders/cart", foreign.Headers.Location!.OriginalString);

        var noToken = await client.PostAsync($"/orders/cart/add/{_factory.InStockVariantId}", new FormUrlEncodedContent([]));
        Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
    }

    [Fact]
    public async Task Checkout_requires_login()
    {
        var response = await Client().GetAsync("/orders/checkout");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/accounts/login", response.Headers.Location!.AbsolutePath);
    }

    [Fact]
    public async Task Checkout_creates_order_with_price_snapshot_and_clears_cart()
    {
        var client = Client(_factory.BuyerId);

        var token = await TokenFrom(client, "/product/шампунь-тест");
        await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token, ("quantity", "2"));

        // Форма заранее заполнена адресом по умолчанию.
        var form = await Page(client, "/orders/checkout");
        Assert.Contains("value=\"Иван Петров\"", form);
        Assert.Contains("14 микрорайон, дом 5, кв./офис 12", form);

        var invalid = await Post(client, "/orders/checkout", token, ("Form.ContactName", ""), ("Form.ContactPhone", "+7"),
            ("Form.City", "Актау"), ("Form.StreetAndDetails", "1"));
        Assert.Equal(HttpStatusCode.OK, invalid.StatusCode);
        Assert.Contains("Обязательное поле.", WebUtility.HtmlDecode(await invalid.Content.ReadAsStringAsync()));

        var placed = await Post(client, "/orders/checkout", token,
            ("Form.SavedAddressId", _factory.AddressId.ToString()),
            ("Form.ContactName", "Иван Петров"), ("Form.ContactPhone", "+7 700 000-00-00"),
            ("Form.City", "Актау"), ("Form.StreetAndDetails", "14 микрорайон, дом 5"), ("Form.Comment", "позвонить заранее"));
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var orderUrl = placed.Headers.Location!.OriginalString;
        Assert.Matches(@"^/orders/my/\d+$", orderUrl);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var order = db.Orders.Include(o => o.Items).OrderBy(o => o.Id).Last();
            Assert.Equal(_factory.BuyerId, order.UserId);
            Assert.Equal(OrderStatus.New, order.Status);
            var item = Assert.Single(order.Items);
            Assert.Equal(("Шампунь Тест", "1 л", "100", 1890m, 2), (item.ProductName, item.PackLabel, item.Article, item.Price, item.Quantity));
        }

        var page = await Page(client, orderUrl);
        Assert.Contains("оформлен. Оплата при получении.", page);
        Assert.Contains("3 780 ₸", page);
        Assert.Contains("Корзина пуста", await Page(client, "/orders/cart"));

        // Чужой заказ по прямой ссылке не открывается.
        var foreign = await Client(_factory.OtherBuyerId).GetAsync(orderUrl);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Checkout_with_empty_cart_goes_home()
    {
        var checkout = await Client(_factory.OtherBuyerId).GetAsync("/orders/checkout");
        Assert.Equal(HttpStatusCode.Redirect, checkout.StatusCode);
        Assert.Equal("/", checkout.Headers.Location!.OriginalString);
    }
}
