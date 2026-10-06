using static OnlineShop.Tests.TestSite;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Tests;

/// <summary>
/// Витрина, корзина и оформление заказа. Чтобы не проходить вход в каждом тесте,
/// покупатель здесь «входит» заголовком X-Test-User. Сам вход проверяют AccountTests.
/// </summary>
public class StorefrontTests : IClassFixture<StorefrontTests.ShopFactory>
{
    private readonly ShopFactory _factory;

    public StorefrontTests(ShopFactory factory)
    {
        _factory = factory;
        _factory.Seed();
    }

    public sealed class ShopFactory : TempShopFactory
    {
        public int BuyerId { get; private set; }
        public int OtherBuyerId { get; private set; }
        public int AddressId { get; private set; }
        public int InStockVariantId { get; private set; }
        public int HiddenVariantId { get; private set; }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            base.ConfigureWebHost(builder);
            // Подменяем только проверку входа: гостя сайт по-прежнему отправляет на страницу входа.
            builder.ConfigureTestServices(services =>
            {
                services.AddAuthentication()
                    .AddScheme<AuthenticationSchemeOptions, TestAuth>(TestAuth.SchemeName, _ => { });
                services.PostConfigure<AuthenticationOptions>(o => o.DefaultAuthenticateScheme = TestAuth.SchemeName);
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
    }

    private sealed class TestAuth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string SchemeName = "Test";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var id))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }

    private HttpClient Client(int? userId = null)
    {
        var client = _factory.Browser();
        if (userId is not null)
        {
            client.DefaultRequestHeaders.Add("X-Test-User", userId.ToString());
        }
        return client;
    }

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
        Assert.Contains("Такой страницы нет", await TestSite.Text(response));
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
    public async Task Product_page_buys_the_pack_chosen_in_the_form()
    {
        var client = Client();
        var page = await Page(client, "/product/шампунь-тест");
        Assert.Contains("name=\"variantId\"", page);
        Assert.Contains("В корзину", page);

        var token = await TokenFrom(client, "/product/шампунь-тест");
        var added = await Post(client, "/orders/cart/add", token, ("variantId", _factory.InStockVariantId.ToString()), ("quantity", "3"));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Contains("5\u00a0670 ₸", await Page(client, "/orders/cart"));

        // Скрытую фасовку подставить в форму нельзя.
        var hidden = await Post(client, "/orders/cart/add", token, ("variantId", _factory.HiddenVariantId.ToString()));
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
    }

    [Fact]
    public async Task Thumbnails_are_small_webp_cached_and_removed_with_original()
    {
        var media = _factory.Services.GetRequiredService<OnlineShop.Web.Services.MediaStorage>();
        // Настоящая картинка побольше: 1200x900, чтобы было что уменьшать.
        using var bitmap = new SkiaSharp.SKBitmap(1200, 900);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(SkiaSharp.SKColors.SeaGreen);
        }
        using var png = SkiaSharp.SKImage.FromBitmap(bitmap).Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        var path = await media.SaveAsync(png.ToArray(), "products", "big.png");

        var client = Client();
        var thumb = await client.GetAsync($"/media/thumb/400/{path}");
        Assert.Equal(HttpStatusCode.OK, thumb.StatusCode);
        Assert.Equal("image/webp", thumb.Content.Headers.ContentType!.MediaType);
        var bytes = await thumb.Content.ReadAsByteArrayAsync();
        using (var decoded = SkiaSharp.SKBitmap.Decode(bytes))
        {
            Assert.Equal((400, 300), (decoded.Width, decoded.Height));
        }
        Assert.Contains("max-age", thumb.Headers.CacheControl!.ToString());

        var copy = media.FullPath($"_thumbs/400/{path}.webp")!;
        Assert.True(File.Exists(copy));

        // Размер, которого сайт не просит, и выход за папку загрузок не обрабатываются.
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync($"/media/thumb/333/{path}")).StatusCode);
        Assert.Equal(HttpStatusCode.Redirect, (await client.GetAsync("/media/thumb/400/..%2F..%2Fshop.db")).StatusCode);

        media.Delete(path);
        Assert.False(File.Exists(copy));
    }

    [Fact]
    public async Task Search_suggestions_return_matching_products_with_photo_and_price()
    {
        var client = Client();
        using var json = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/search/suggest?q=ШАМП"));
        var first = json.RootElement.EnumerateArray().Single();
        Assert.Equal("Шампунь Тест", first.GetProperty("name").GetString());
        Assert.Equal("1 890 ₸", first.GetProperty("price").GetString());
        Assert.Equal("/img/no-photo.svg", first.GetProperty("image").GetString());
        Assert.StartsWith("/product/", first.GetProperty("url").GetString());

        Assert.Equal("[]", await client.GetStringAsync("/search/suggest?q=ш"));
        Assert.Contains("search-suggest", await Page(client, "/"));
    }

    [Fact]
    public async Task Repeat_order_puts_available_items_back_into_cart()
    {
        int orderId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var order = new Order
            {
                UserId = _factory.BuyerId, ContactName = "Иван", ContactPhone = "+7", City = "Актау", StreetAndDetails = "1",
                Items =
                [
                    new OrderItem { VariantId = _factory.InStockVariantId, ProductName = "Шампунь Тест", PackLabel = "1 л", Price = 1500m, Quantity = 2 },
                    new OrderItem { VariantId = _factory.HiddenVariantId, ProductName = "Шампунь Тест", PackLabel = "20 л", Price = 9000m, Quantity = 1 },
                ],
            };
            db.Orders.Add(order);
            db.SaveChanges();
            orderId = order.Id;
        }

        var client = Client(_factory.BuyerId);
        var token = await TokenFrom(client, $"/orders/my/{orderId}");
        var repeated = await Post(client, $"/orders/my/{orderId}/repeat", token);
        Assert.Equal("/orders/cart", repeated.Headers.Location!.OriginalString);

        var cart = await Page(client, "/orders/cart");
        // Цена сегодняшняя (1890), а не из заказа (1500); скрытая фасовка пропущена с объяснением.
        Assert.Contains("3 780 ₸", cart);
        Assert.Contains("добавлено позиций из заказа", cart);
        Assert.Contains("Сейчас нет в продаже: Шампунь Тест (20 л).", cart);

        // Чужой заказ повторить нельзя.
        var stranger = Client(_factory.OtherBuyerId);
        var strangerToken = await TokenFrom(stranger, "/product/шампунь-тест");
        Assert.Equal(HttpStatusCode.NotFound, (await Post(stranger, $"/orders/my/{orderId}/repeat", strangerToken)).StatusCode);
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
        Assert.Contains("Обязательное поле.", await TestSite.Text(invalid));

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
