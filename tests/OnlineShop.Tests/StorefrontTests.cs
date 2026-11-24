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
using OnlineShop.Web.Infrastructure;
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
        public const long ManagerChatId = 700;

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

            var buyer = new ApplicationUser
            {
                UserName = "buyer", NormalizedUserName = "BUYER", FirstName = "Иван",
                Email = "buyer@example.com", NormalizedEmail = "BUYER@EXAMPLE.COM", EmailConfirmed = true,
            };
            buyer.Addresses.Add(new Address
            {
                FullName = "Иван Петров", Phone = "+7 700 000-00-00", Street = "14 микрорайон", House = "5",
                Apartment = "12", IsDefault = true,
            });
            var other = new ApplicationUser { UserName = "other", NormalizedUserName = "OTHER" };
            db.Users.AddRange(buyer, other);
            db.TelegramRecipients.Add(new TelegramRecipient { Phone = "77010000700", ChatId = ManagerChatId, CreatedAt = DateTime.UtcNow });
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
    public async Task Product_page_and_card_show_article_and_stock()
    {
        ResetStock();
        var page = await Page(Client(), "/product/шампунь-тест");
        Assert.Contains("арт. <span id=\"buy-article\">100</span>", page);
        Assert.Contains("В наличии: 5 шт.", page);
        Assert.Contains("max=\"5\" id=\"buy-qty\"", page);
        // Карточка в каталоге: артикул под названием, только видимые фасовки.
        var catalog = await Page(Client(), "/search?q=" + Uri.EscapeDataString("Шампунь Тест"));
        Assert.Contains("<p class=\"card-article\">арт. 100</p>", catalog);
    }

    /// <summary>
    /// Другие тесты оформляют заказы на ту же фасовку, и резерв уменьшает остаток. Тестам, где важно
    /// точное число в наличии, возвращаем исходные 5 шт. без резерва.
    /// </summary>
    private void ResetStock()
    {
        using var scope = _factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<ShopDbContext>().ProductVariants
            .Where(v => v.Id == _factory.InStockVariantId)
            .ExecuteUpdate(u => u.SetProperty(v => v.StockQty, 5).SetProperty(v => v.ReservedQty, 0));
    }

    [Fact]
    public async Task Cart_add_update_and_remove()
    {
        ResetStock();
        var client = Client();
        var token = await TokenFrom(client, "/product/шампунь-тест");

        var added = await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token,
            ("quantity", "2"), ("next", "/product/шампунь-тест"));
        Assert.Equal(HttpStatusCode.Redirect, added.StatusCode);
        Assert.Equal("/product/шампунь-тест", Uri.UnescapeDataString(added.Headers.Location!.OriginalString));

        var cart = await Page(client, "/orders/cart");
        Assert.Contains("3 780 ₸", cart);
        Assert.Contains("«Шампунь Тест, 1 л» в корзине.", cart);
        Assert.Contains("В наличии: 5 шт.", cart);

        await Post(client, $"/orders/cart/update/{_factory.InStockVariantId}", token, ("quantity", "3"));
        Assert.Contains("5 670 ₸", await Page(client, "/orders/cart"));

        // Больше остатка (5 шт.) не ставится ни изменением количества, ни повторным добавлением.
        await Post(client, $"/orders/cart/update/{_factory.InStockVariantId}", token, ("quantity", "9"));
        cart = await Page(client, "/orders/cart");
        Assert.Contains("9 450 ₸", cart);
        Assert.Contains("В наличии только 5 шт., больше заказать нельзя.", cart);
        await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token, ("quantity", "4"));
        cart = await Page(client, "/orders/cart");
        Assert.Contains("9 450 ₸", cart);
        Assert.Contains("В наличии только 5 шт. «Шампунь Тест, 1 л», в корзине 5 шт.", cart);

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
        Assert.Contains("Сейчас нет в наличии или сняты с продажи: Шампунь Тест (20 л).", cart);

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
    public async Task Side_catalog_groups_categories_by_home_sections()
    {
        var html = await Page(Client(), "/");
        // Шесть разделов как на главной; пустые помечены «скоро», у категорий число товаров.
        Assert.Contains("data-cm-sec=\"laundry\"", html);
        Assert.Contains("data-cm-sec=\"car\"", html);
        Assert.Contains("скоро", html);
        Assert.Contains("class=\"cm-cat-count\"", html);
    }

    [Fact]
    public async Task Guest_checks_out_without_account_and_sees_order_by_secret_link()
    {
        var client = Client();
        var token = await TokenFrom(client, "/product/шампунь-тест");
        await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token);
        // Сообщение после добавления — карточка со ссылкой в корзину.
        var afterAdd = await Page(client, "/product/шампунь-тест");
        Assert.Contains("message toast success", afterAdd);
        Assert.Contains("Перейти в корзину", afterAdd);

        // Входить не надо: форма открывается сразу, город уже подставлен, согласие спрашиваем.
        var form = await Page(client, "/orders/checkout");
        Assert.Contains("Без регистрации", form);
        Assert.Contains("value=\"Актау\"", form);
        Assert.Contains("политики конфиденциальности", form);

        var address = new[] { ("Form.ContactName", "Гость"), ("Form.ContactPhone", "+7 701 111-22-33"),
            ("Form.IsPickup", "false"), ("Form.City", "Актау"), ("Form.StreetAndDetails", "3 микрорайон, дом 1") };
        var refused = await Post(client, "/orders/checkout", token, address);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);

        var placed = await Post(client, "/orders/checkout", token, [.. address, ("Form.AcceptPrivacy", "true")]);
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var link = placed.Headers.Location!.OriginalString;
        Assert.Matches(@"^/orders/view/[0-9a-f]{32}$", link);

        using (var scope = _factory.Services.CreateScope())
        {
            var order = scope.ServiceProvider.GetRequiredService<ShopDbContext>().Orders.Single(o => o.ContactPhone == "+7 701 111-22-33");
            Assert.Null(order.UserId);
            Assert.Equal(link, $"/orders/view/{order.AccessToken}");
        }

        var page = await Page(client, link);
        Assert.Contains("принят", page);
        Assert.Contains("Скопировать ссылку", page);
        Assert.DoesNotContain("Мои заказы</a>", page);
        Assert.Contains("Корзина пуста", await Page(client, "/orders/cart"));

        // По ссылке заказ открывается и без куки (с другого устройства), а по выдуманной метке нет.
        Assert.Equal(HttpStatusCode.OK, (await Client().GetAsync(link)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Client().GetAsync("/orders/view/" + new string('0', 32))).StatusCode);
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
            ("Form.City", "Актау"), ("Form.StreetAndDetails", "14 микрорайон, дом 5"), ("Form.Comment", "позвонить заранее"),
            ("Form.AcceptPrivacy", "true"));
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var orderUrl = placed.Headers.Location!.OriginalString;
        Assert.Matches(@"^/orders/my/\d+$", orderUrl);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var order = db.Orders.Include(o => o.Items).OrderBy(o => o.Id).Last();
            Assert.Equal<int?>(_factory.BuyerId, order.UserId);
            Assert.Equal(OrderStatus.New, order.Status);
            var item = Assert.Single(order.Items);
            Assert.Equal(("Шампунь Тест", "1 л", "100", 1890m, 2), (item.ProductName, item.PackLabel, item.Article, item.Price, item.Quantity));
        }

        // Менеджеру в Telegram уходит заказ со ссылкой на него в панели.
        var orderId = orderUrl.Split('/').Last();
        var alert = await _factory.Telegram.WaitForMessage(ShopFactory.ManagerChatId, m => m.Text.StartsWith($"<b>Новый заказ №{orderId}</b>"));
        Assert.Contains("Шампунь Тест, 1 л: 2 × 1 890 ₸", alert.Text);
        Assert.Contains("Комментарий: позвонить заранее", alert.Text);
        Assert.EndsWith($"/admin/orders/{orderId}", alert.Text);

        var page = await Page(client, orderUrl);
        // Сразу после оформления — карточка «Заказ принят» с шагом «Готово».
        Assert.Contains($"Заказ №{orderId} принят", page);
        Assert.Contains("co-step is-current", page);
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

    [Fact]
    public async Task Hero_shows_only_slides_whose_product_is_on_sale()
    {
        // Ни одного товара из слайдов в базе: первого экрана нет, страница открывается как раньше.
        Assert.DoesNotContain("class=\"hero\"", await Page(Client(), "/"));

        int id;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var category = db.Categories.First();
            var wax = new Product
            {
                Category = category, Name = "Холодный воск TOP WAX", Slug = "холодный-воск-top-wax",
                Variants = [new ProductVariant { Article = "212101", PackLabel = "1 кг", Price = 180m, StockQty = 3 }],
            };
            db.Products.Add(wax);
            db.SaveChanges();
            id = wax.Id;
        }
        try
        {
            var html = await Page(Client(), "/");
            Assert.Contains("class=\"hero\"", html);
            Assert.Contains("Холодный воск TOP WAX", html);
            Assert.Contains("img/hero/top-wax.webp", html);
            Assert.Contains(">Купить</a>", html);
            // Один слайд: листать нечего, стрелок нет.
            Assert.DoesNotContain("class=\"hero-controls\"", html);
            Assert.DoesNotContain("glass-gloss.webp", html);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            db.Products.Remove(db.Products.Include(p => p.Variants).First(p => p.Id == id));
            db.SaveChanges();
        }
    }

    [Fact]
    public async Task Home_shows_active_stores_with_route_link()
    {
        int[] ids;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var shown = new Store
            {
                Address = "г. Актау, 7 микрорайон, тест-бутик", Kind = StoreKind.Shop, SortOrder = -2,
                WorkingHours = "Пн-Сб, 9:00-18:00", LunchHours = "с 13:00 до 14:00", Phone = "+7 700 111-22-33",
                Latitude = 43.65, Longitude = 51.16,
            };
            var hidden = new Store { Address = "г. Актау, скрытая тест-точка", SortOrder = -1, IsActive = false };
            var daily = new Store { Address = "г. Актау, 15 микрорайон, тест-ежедневно", Kind = StoreKind.Shop, SortOrder = -3, WorkingHours = "Ежедневно 11:00–18:00" };
            db.Stores.AddRange(shown, hidden, daily);
            db.SaveChanges();
            ids = [shown.Id, hidden.Id, daily.Id];
        }
        try
        {
            var html = await Page(Client(), "/");
            Assert.Contains("Где нас найти", html);
            Assert.Contains("тест-бутик", html);
            Assert.Contains("обед с 13:00 до 14:00", html);
            Assert.Contains("href=\"tel:+7 700 111-22-33\"", html);
            Assert.Contains("Как добраться", html);
            Assert.DoesNotContain("скрытая тест-точка", html);
            // Точка, работающая каждый день, получает плашку «Без выходных»; «г. Актау» в адресе не повторяется.
            var visit = html[html.IndexOf("visit-list", StringComparison.Ordinal)..html.IndexOf("</section>", html.IndexOf("visit-list", StringComparison.Ordinal), StringComparison.Ordinal)];
            Assert.Contains("Без выходных", visit);
            Assert.DoesNotContain("г. Актау, 15", visit);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            db.Stores.RemoveRange(db.Stores.Where(s => ids.Contains(s.Id)));
            db.SaveChanges();
        }
    }

    [Fact]
    public async Task Home_shows_six_big_sections_and_marks_empty_ones()
    {
        var html = await Page(Client(), "/");
        foreach (var title in new[] { "Для стирки", "Для уборки", "Для дома", "Для гигиены", "Для авто", "Для бизнеса" })
        {
            Assert.Contains($"<span class=\"hs-name\">{title}</span>", html);
        }
        // В тестовом каталоге нет товаров для стирки: плитка не ссылка, а «Скоро в каталоге».
        Assert.Contains("Скоро в каталоге", html);
    }

    [Fact]
    public async Task Footer_has_whatsapp_button_and_shops_with_hours_but_no_warehouse()
    {
        int[] storeIds;
        int contactId;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var shop = new Store { Address = "г. Актау, 21 микрорайон, подвал-бутик", Kind = StoreKind.Shop, WorkingHours = "Пн–Пт 9:00–16:00", SortOrder = -5 };
            var warehouse = new Store { Address = "г. Актау, подвал-склад", Kind = StoreKind.Warehouse, SortOrder = -4 };
            var contact = new SiteContact { Phone = "+7 701 245-48-48", Whatsapp = "+7 701 245-48-48", Instagram = "grassaktau" };
            db.Stores.AddRange(shop, warehouse);
            db.SiteContacts.Add(contact);
            db.SaveChanges();
            storeIds = [shop.Id, warehouse.Id];
            contactId = contact.Id;
        }
        try
        {
            var html = await Page(Client(), "/");
            var footer = html[html.IndexOf("<footer", StringComparison.Ordinal)..];
            Assert.Contains("Написать в WhatsApp", footer);
            Assert.Contains("https://wa.me/77012454848?text=", footer);
            Assert.Contains("https://instagram.com/grassaktau", footer);
            // Адрес без «г. Актау»: город уже в заголовке колонки.
            Assert.Contains(">21 микрорайон, подвал-бутик</a>", footer);
            Assert.Contains("Пн–Пт 9:00–16:00", footer);
            Assert.DoesNotContain("подвал-склад", footer);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            db.Stores.RemoveRange(db.Stores.Where(s => storeIds.Contains(s.Id)));
            db.SiteContacts.RemoveRange(db.SiteContacts.Where(c => c.Id == contactId));
            db.SaveChanges();
        }
    }

    [Fact]
    public async Task Category_page_has_pack_buttons_and_puts_in_stock_first()
    {
        int[] ids;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var category = db.Categories.Include(c => c.Brand).First(c => c.Slug == "автошампуни" && c.Brand.Slug == "grass");
            var spray = new Product
            {
                Category = category, Name = "А-тест триггер", Slug = "а-тест-триггер",
                Variants = [new ProductVariant { Article = "990001", PackLabel = "500 мл триггер", Price = 100m, StockQty = 0 }],
            };
            var canister = new Product
            {
                Category = category, Name = "Я-тест канистра", Slug = "я-тест-канистра",
                Variants = [new ProductVariant { Article = "990002", PackLabel = "20 кг", Price = 900m, StockQty = 5 }],
            };
            db.Products.AddRange(spray, canister);
            db.SaveChanges();
            ids = [spray.Id, canister.Id];
        }
        try
        {
            var html = await Page(Client(), "/catalog/grass/автошампуни");
            Assert.Contains("class=\"quick-filters\"", html);
            Assert.Contains("Триггер 500", html);
            Assert.Contains("Только в наличии", html);
            // То, что можно купить сейчас, стоит выше товара «нет в наличии», хотя по алфавиту он первый.
            Assert.True(html.IndexOf("Я-тест канистра") < html.IndexOf("А-тест триггер"));

            var sprays = await Page(Client(), "/catalog/grass/автошампуни?pack=spray");
            Assert.Contains("А-тест триггер", sprays);
            Assert.DoesNotContain("Я-тест канистра", sprays);

            var inStock = await Page(Client(), "/catalog/grass/автошампуни?in_stock=1");
            Assert.Contains("Я-тест канистра", inStock);
            Assert.DoesNotContain("А-тест триггер", inStock);

            // Цена — в том же ряду кнопок; боковой панели фильтров на странице раздела больше нет.
            var cheap = await Page(Client(), "/catalog/grass/автошампуни?price_max=500");
            Assert.Contains("А-тест триггер", cheap);
            Assert.DoesNotContain("Я-тест канистра", cheap);
            Assert.Contains("name=\"price_max\"", cheap);
            Assert.DoesNotContain("id=\"filters\"", cheap);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            db.Products.RemoveRange(db.Products.Include(p => p.Variants).Where(p => ids.Contains(p.Id)));
            db.SaveChanges();
        }
    }

    [Fact]
    public async Task Header_has_new_logo_and_no_top_strip()
    {
        var html = await Page(Client(), "/");
        Assert.DoesNotContain("class=\"topbar\"", html);
        // С 24.11 только IFOAM: GRASS убран из логотипа, рядом город.
        Assert.Contains("img/logo-sun.png", html);
        Assert.Contains("IFOAM<span class=\"logo-bar\"></span>", html);
        Assert.Contains("Бытовая и автохимия", html);
        Assert.DoesNotContain("GRASS<span", html);
    }

    // ----- Доработки витрины 24.11 -----

    [Fact]
    public async Task Delivery_in_aktau_tops_up_the_order_to_the_minimum()
    {
        // Минимальный заказ с доставкой по Актау — 7 000 ₸: меньше — доплата до 7 000 ₸, больше — доставка бесплатная.
        Assert.Equal(5110m, DeliveryPrices.CityFee(1890m));
        Assert.Equal(0m, DeliveryPrices.CityFee(7000m));
        Assert.Equal(0m, DeliveryPrices.CityFee(9450m));
        Assert.True(DeliveryPrices.IsCity("г. Актау"));
        Assert.False(DeliveryPrices.IsCity("Жанаозен"));

        var client = Client();
        var token = await TokenFrom(client, "/product/шампунь-тест");
        await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token);
        var cart = await Page(client, "/orders/cart");
        Assert.Contains(Format.Money(5110m), cart);                 // доставка
        Assert.Contains(Format.Money(7000m), cart);                 // к оплате
        Assert.Contains("При самовывозе — " + Format.Money(1890m), cart);
        Assert.Contains(Format.Money(7000m), await Page(client, "/orders/checkout"));

        // Ещё 3 шт. — товаров на 7 560 ₸, доставка бесплатная.
        await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token, ("quantity", "3"));
        cart = await Page(client, "/orders/cart");
        Assert.Contains("Доставка по Актау бесплатная", cart);
        Assert.Contains(Format.Money(7560m), cart);
        await Post(client, $"/orders/cart/remove/{_factory.InStockVariantId}", token);
    }

    [Fact]
    public async Task Recently_viewed_is_on_product_pages_only_and_there_is_no_whatsapp_order()
    {
        var client = Client();
        var page = await Page(client, "/product/шампунь-тест");
        Assert.DoesNotContain("Вы недавно смотрели", page);
        // Решение пользователя 24.11: заказ только через сайт, кнопки «Заказать в WhatsApp» нет.
        Assert.DoesNotContain("Заказать в WhatsApp", page);
        // Главная «недавние» не показывает, страница самого товара себя в них не показывает.
        Assert.DoesNotContain("Вы недавно смотрели", await Page(client, "/"));
        Assert.DoesNotContain("Вы недавно смотрели", await Page(client, "/product/шампунь-тест"));

        // Открыли другой товар — на нём внизу «Вы недавно смотрели» с шампунем.
        string slug;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var other = db.Products.FirstOrDefault(p => p.Name == "Воск Недавний");
            if (other is null)
            {
                var category = db.Categories.First();
                other = new Product { CategoryId = category.Id, Name = "Воск Недавний", Variants = [new ProductVariant { Article = "300", PackLabel = "1 л", Price = 500m, StockQty = 3 }] };
                db.Products.Add(other);
                db.SaveChanges();
            }
            slug = other.Slug;
        }
        var second = await Page(client, "/product/" + slug);
        Assert.Contains("Вы недавно смотрели", second);
        Assert.Contains("Шампунь Тест", second);
    }

    [Fact]
    public async Task Cabinet_lists_my_wash_bookings_and_denied_page_has_no_empty_name()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var wash = OnlineShop.Web.Infrastructure.CarWashes.All[0];
            db.WashBookings.Add(new WashBooking
            {
                WashKey = wash.Key, WashName = wash.Name, ServiceKey = "complex", ServiceName = "Комплексная мойка", Minutes = 60,
                StartsAt = OnlineShop.Web.Services.WashSlots.Now().Date.AddDays(2).AddHours(12), Name = "Иван", Phone = "77010000000",
                CreatedAt = DateTime.UtcNow, UserId = _factory.BuyerId,
            });
            db.SaveChanges();
        }
        var mine = await Page(Client(_factory.BuyerId), "/accounts/wash");
        Assert.Contains("Записи на мойку", mine);
        Assert.Contains("Комплексная мойка", mine);
        Assert.Contains("Открыть запись", mine);
        Assert.DoesNotContain("Комплексная мойка", await Page(Client(_factory.OtherBuyerId), "/accounts/wash"));
        Assert.Equal(HttpStatusCode.Redirect, (await _factory.Browser().GetAsync("/accounts/wash")).StatusCode);

        var denied = await Page(Client(), "/accounts/denied");
        Assert.Contains("Нет доступа", denied);
        Assert.DoesNotContain("Вы вошли как «»", denied);
    }

    [Fact]
    public async Task Stores_page_shows_the_car_wash()
    {
        var page = await Page(Client(), "/addresses");
        Assert.Contains(OnlineShop.Web.Infrastructure.CarWashes.All[0].Address, page);
        Assert.Contains("href=\"/wash\"", page);
    }
}
