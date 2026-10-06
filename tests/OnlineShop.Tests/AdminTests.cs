using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Панель управления целиком, с настоящим входом через куку.</summary>
public class AdminTests(TempShopFactory factory) : IClassFixture<TempShopFactory>, IDisposable
{
    private const string Password = "панель-управления";
    private static int _counter;
    private readonly string _files = Directory.CreateTempSubdirectory("online-shop-admin-").FullName;

    public void Dispose() => Directory.Delete(_files, recursive: true);

    private async Task<string> CreateUser(bool manager)
    {
        var login = $"{(manager ? "менеджер" : "покупатель")}{Interlocked.Increment(ref _counter)}";
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = login };
        Assert.True((await users.CreateAsync(user, Password)).Succeeded);
        if (manager)
        {
            await users.AddToRoleAsync(user, RoleNames.Manager);
        }
        return login;
    }

    private async Task<HttpClient> SignIn(string login)
    {
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/login");
        var response = await Post(client, "/accounts/login", token, ("Input.UserName", login), ("Input.Password", Password));
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        return client;
    }

    private async Task<HttpClient> Manager() => await SignIn(await CreateUser(manager: true));

    private T Db<T>(Func<ShopDbContext, T> query)
    {
        using var scope = factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    private static async Task<HttpResponseMessage> PostFiles(HttpClient client, string url, string token,
        IEnumerable<(string Field, string Path, string FileName)> files, params (string, string)[] fields)
    {
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }
        foreach (var (field, path, fileName) in files)
        {
            form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(path)), field, fileName);
        }
        return await client.PostAsync(url, form);
    }

    private (int ProductId, int VariantId) SeedProduct(string article, decimal? price = 1000m)
    {
        return Db(db =>
        {
            var variant = new ProductVariant { Article = article, Price = price, StockQty = 3 };
            var product = new Product
            {
                Category = new Category { Brand = new Brand { Name = $"Бренд {article}" }, Name = "Категория" },
                Name = $"Товар {article}", Variants = [variant],
            };
            db.Products.Add(product);
            db.SaveChanges();
            return (product.Id, variant.Id);
        });
    }

    [Fact]
    public async Task Only_managers_get_in()
    {
        var guest = await factory.Browser().GetAsync("/admin");
        Assert.Equal("/accounts/login", guest.Headers.Location!.AbsolutePath);

        var buyer = await (await SignIn(await CreateUser(manager: false))).GetAsync("/admin/products");
        Assert.Equal("/accounts/denied", buyer.Headers.Location!.AbsolutePath);

        var manager = await Manager();
        // Кнопка свёрнутого меню для телефона есть на каждой странице панели.
        Assert.Contains("id=\"admin-menu-btn\"", await Page(manager, "/admin/products"));
        var home = await Page(manager, "/");
        Assert.Contains("Админка", home);
        Assert.Contains("Что сделать", await Page(manager, "/admin"));
    }

    [Fact]
    public async Task Edit_links_point_to_record_urls_and_add_links_to_add()
    {
        var client = await Manager();
        var (productId, _) = SeedProduct($"L{Guid.NewGuid():N}"[..12]);
        var list = await Page(client, "/admin/products");
        Assert.Contains($"href=\"/admin/products/{productId}\"", list);
        Assert.Contains("href=\"/admin/products/add\"", list);
        Assert.DoesNotContain("add?id=", list);
    }

    [Theory]
    [InlineData("/admin/orders")]
    [InlineData("/admin/products")]
    [InlineData("/admin/products/add")]
    [InlineData("/admin/variants")]
    [InlineData("/admin/variants/add")]
    [InlineData("/admin/categories")]
    [InlineData("/admin/categories/add")]
    [InlineData("/admin/brands/add")]
    [InlineData("/admin/banners")]
    [InlineData("/admin/banners/add")]
    [InlineData("/admin/stores")]
    [InlineData("/admin/stores/add")]
    [InlineData("/admin/contacts")]
    [InlineData("/admin/contacts/add")]
    [InlineData("/admin/users")]
    [InlineData("/admin/addresses")]
    [InlineData("/admin/import")]
    [InlineData("/admin/import/stock")]
    [InlineData("/admin/telegram")]
    public async Task Every_section_opens(string url)
    {
        var response = await (await Manager()).GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Brand_create_inline_edit_and_protected_delete()
    {
        var client = await Manager();
        var token = await TokenFrom(client, "/admin/brands/add");
        var name = $"Бренд {Guid.NewGuid():N}"[..20];

        var created = await Post(client, "/admin/brands/add", token, ("Input.Name", name), ("Input.SortOrder", "5"), ("Input.IsActive", "true"));
        Assert.Equal(HttpStatusCode.Redirect, created.StatusCode);
        var brand = Db(db => db.Brands.Single(b => b.Name == name));
        Assert.Equal(Web.Infrastructure.Slug.From(name), brand.Slug);

        var duplicate = await Post(client, "/admin/brands/add", token, ("Input.Name", name));
        Assert.Contains("Бренд с таким названием уже есть.", await Text(duplicate));

        // Старый адрес списка брендов ведёт в общее дерево.
        Assert.Equal("/admin/categories", (await client.GetAsync("/admin/brands")).Headers.Location!.OriginalString);

        await Post(client, $"/admin/brands/{brand.Id}", token, ("Input.Name", name), ("Input.SortOrder", "5"), ("Input.IsActive", "false"));
        Assert.False(Db(db => db.Brands.Single(b => b.Id == brand.Id).IsActive));

        var (productId, _) = SeedProduct($"B{Guid.NewGuid():N}"[..12]);
        var productBrand = Db(db => db.Products.Where(p => p.Id == productId).Select(p => p.Category.BrandId).Single());
        await Post(client, $"/admin/brands/{productBrand}?handler=Delete", token);
        Assert.Contains("Нельзя удалить бренд", await Page(client, $"/admin/brands/{productBrand}"));
        Assert.True(Db(db => db.Brands.Any(b => b.Id == productBrand)));

        await Post(client, $"/admin/brands/{brand.Id}?handler=Delete", token);
        Assert.False(Db(db => db.Brands.Any(b => b.Id == brand.Id)));
    }

    [Fact]
    public async Task Tree_shows_nesting_and_arrows_reorder_siblings()
    {
        var client = await Manager();
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (brandId, parentId, firstId, secondId) = Db(db =>
        {
            var brand = new Brand { Name = $"Дерево {suffix}" };
            var parent = new Category { Brand = brand, Name = "Очистители" };
            var first = new Category { Brand = brand, Parent = parent, Name = "Для стёкол", SortOrder = 1 };
            var second = new Category { Brand = brand, Parent = parent, Name = "Для дисков", SortOrder = 2 };
            db.Categories.AddRange(parent, first, second);
            db.SaveChanges();
            return (brand.Id, parent.Id, first.Id, second.Id);
        });

        var tree = await Page(client, "/admin/categories");
        Assert.Contains($"id=\"brand-{brandId}\"", tree);
        Assert.True(tree.IndexOf($"category-{firstId}", StringComparison.Ordinal) < tree.IndexOf($"category-{secondId}", StringComparison.Ordinal));

        // Путь стрелками на странице подкатегории и среди вариантов родителя.
        var edit = await Page(client, $"/admin/categories/{secondId}");
        Assert.Contains($"Дерево {suffix} → Очистители", edit);

        var token = await TokenFrom(client, "/admin/categories");
        List<int> Order() => Db(db => db.Categories.Where(c => c.ParentId == parentId).OrderBy(c => c.SortOrder).Select(c => c.Id).ToList());
        await Post(client, $"/admin/categories?handler=Move&kind=category&id={secondId}&direction=-1", token);
        Assert.Equal([secondId, firstId], Order());

        // Самый верхний выше не сдвигается.
        await Post(client, $"/admin/categories?handler=Move&kind=category&id={secondId}&direction=-1", token);
        Assert.Equal([secondId, firstId], Order());
    }

    [Fact]
    public async Task Product_list_filters_by_missing_photo_and_shows_category_path()
    {
        var client = await Manager();
        var (productId, _) = SeedProduct($"F{Guid.NewGuid():N}"[..12]);
        var page = await Page(client, "/admin/products?filter=nophoto");
        Assert.Contains($"href=\"/admin/products/{productId}\"", page);
        Assert.Contains("нет фото", page);
        var brandName = Db(db => db.Products.Where(p => p.Id == productId).Select(p => p.Category.Brand.Name).Single());
        Assert.Contains($"{brandName} → Категория", page);
    }

    [Fact]
    public async Task Product_form_saves_variants_with_comma_prices_and_checks_articles()
    {
        var client = await Manager();
        var (productId, variantId) = SeedProduct($"P{Guid.NewGuid():N}"[..12]);
        var (_, otherVariantId) = SeedProduct($"O{Guid.NewGuid():N}"[..12]);
        var otherArticle = Db(db => db.ProductVariants.Single(v => v.Id == otherVariantId).Article);
        var categoryId = Db(db => db.Products.Single(p => p.Id == productId).CategoryId);
        var token = await TokenFrom(client, $"/admin/products/{productId}");
        var newArticle = $"N{Guid.NewGuid():N}"[..12];

        (string, string)[] Form(string secondArticle) =>
        [
            ("Input.Name", "Новое название"), ("Input.CategoryId", categoryId.ToString()), ("Input.IsActive", "true"),
            ("Input.Description", "Описание"), ("Input.Ph", "  "),
            ("Variants[0].Id", variantId.ToString()), ("Variants[0].Article", Db(db => db.ProductVariants.Single(v => v.Id == variantId).Article)),
            ("Variants[0].PackLabel", "1 л"), ("Variants[0].Price", "1 890,50"), ("Variants[0].StockQty", "4"), ("Variants[0].IsActive", "true"),
            ("Variants[1].Id", "0"), ("Variants[1].Article", secondArticle), ("Variants[1].PackLabel", "5 л"),
            ("Variants[1].Price", "7490.5"), ("Variants[1].IsActive", "false"),
        ];

        var clash = await Post(client, $"/admin/products/{productId}", token, Form(otherArticle));
        Assert.Contains("Артикул уже есть у товара", await Text(clash));

        var badPrice = Form(newArticle).Select(f => f.Item1 == "Variants[0].Price" ? (f.Item1, "дорого") : f).ToArray();
        Assert.Contains("Введите число", await Text(await Post(client, $"/admin/products/{productId}", token, badPrice)));

        var saved = await Post(client, $"/admin/products/{productId}", token, Form(newArticle));
        Assert.Equal(HttpStatusCode.Redirect, saved.StatusCode);
        var variants = Db(db => db.ProductVariants.Where(v => v.ProductId == productId).OrderBy(v => v.Id)
            .Select(v => new { v.Article, v.Price, v.StockQty, v.IsActive }).ToList());
        Assert.Equal(2, variants.Count);
        Assert.Equal((1890.5m, 4, true), (variants[0].Price!.Value, variants[0].StockQty, variants[0].IsActive));
        Assert.Equal((newArticle, 7490.5m, false), (variants[1].Article, variants[1].Price!.Value, variants[1].IsActive));
        Assert.Equal("Новое название", Db(db => db.Products.Single(p => p.Id == productId).Name));
    }

    [Fact]
    public async Task Primary_photo_is_chosen_by_radio_button()
    {
        var client = await Manager();
        var (productId, variantId) = SeedProduct($"I{Guid.NewGuid():N}"[..12]);
        var png = Path.Combine(_files, "photo.png");
        await File.WriteAllBytesAsync(png, OneCFiles.Png);
        var article = Db(db => db.ProductVariants.Single(v => v.Id == variantId).Article);
        var categoryId = Db(db => db.Products.Single(p => p.Id == productId).CategoryId);
        var token = await TokenFrom(client, $"/admin/products/{productId}");
        (string, string)[] basics =
        [
            ("Input.Name", "С фото"), ("Input.CategoryId", categoryId.ToString()), ("Input.IsActive", "true"),
            ("Variants[0].Id", variantId.ToString()), ("Variants[0].Article", article), ("Variants[0].Price", "1000"),
            ("Variants[0].StockQty", "3"), ("Variants[0].IsActive", "true"),
        ];

        await PostFiles(client, $"/admin/products/{productId}", token, [("NewImages", png, "a.png"), ("NewImages", png, "b.png")], basics);
        var images = Db(db => db.ProductImages.Where(i => i.ProductId == productId).OrderBy(i => i.Id).ToList());
        Assert.Equal(2, images.Count);
        Assert.True(images[0].IsPrimary, "Первое загруженное фото становится главным само.");

        var fields = basics.Concat(
        [
            ("Images[0].Id", images[0].Id.ToString()), ("Images[0].ImagePath", images[0].ImagePath),
            ("Images[1].Id", images[1].Id.ToString()), ("Images[1].ImagePath", images[1].ImagePath),
            ("PrimaryImageId", images[1].Id.ToString()),
        ]).ToArray();
        await Post(client, $"/admin/products/{productId}", token, fields);
        var after = Db(db => db.ProductImages.Where(i => i.ProductId == productId).OrderBy(i => i.Id).Select(i => i.IsPrimary).ToList());
        Assert.Equal([false, true], after);
    }

    [Fact]
    public async Task Ordered_variant_and_product_cannot_be_deleted()
    {
        var client = await Manager();
        var (productId, variantId) = SeedProduct($"D{Guid.NewGuid():N}"[..12]);
        Db(db =>
        {
            var user = new ApplicationUser { UserName = $"заказчик{Guid.NewGuid():N}"[..20] };
            db.Orders.Add(new Order
            {
                User = user, ContactName = "Иван", ContactPhone = "+7", City = "Актау", StreetAndDetails = "1",
                Items = [new OrderItem { VariantId = variantId, ProductName = "Товар", Price = 1000m, Quantity = 1 }],
            });
            return db.SaveChanges();
        });
        var token = await TokenFrom(client, $"/admin/variants/{variantId}");

        await Post(client, $"/admin/variants/{variantId}?handler=Delete", token);
        Assert.Contains("заказывали, удалить его нельзя", await Page(client, $"/admin/variants/{variantId}"));
        await Post(client, $"/admin/products/{productId}?handler=Delete", token);
        Assert.Contains("Нельзя удалить товар", await Page(client, $"/admin/products/{productId}"));
        Assert.True(Db(db => db.ProductVariants.Any(v => v.Id == variantId)));
    }

    [Fact]
    public async Task Variant_list_quick_edit_rejects_bad_numbers_without_partial_save()
    {
        var client = await Manager();
        var (_, first) = SeedProduct($"V{Guid.NewGuid():N}"[..12]);
        var (_, second) = SeedProduct($"W{Guid.NewGuid():N}"[..12]);
        var token = await TokenFrom(client, "/admin/variants");

        await Post(client, "/admin/variants?handler=SaveList", token,
            ("Rows[0].Id", first.ToString()), ("Rows[0].Article", "a"), ("Rows[0].Price", "2 500,5"), ("Rows[0].StockQty", "7"), ("Rows[0].IsActive", "true"),
            ("Rows[1].Id", second.ToString()), ("Rows[1].Article", "b"), ("Rows[1].Price", "абв"), ("Rows[1].StockQty", "1"), ("Rows[1].IsActive", "true"));
        Assert.Contains("Не сохранено", await Page(client, "/admin/variants"));
        Assert.Equal(HttpStatusCode.Redirect, (await Post(client, "/admin/variants?handler=SaveList&p=2", token)).StatusCode);
        Assert.Equal(1000m, Db(db => db.ProductVariants.Single(v => v.Id == first).Price));

        await Post(client, "/admin/variants?handler=SaveList", token,
            ("Rows[0].Id", first.ToString()), ("Rows[0].Article", "a"), ("Rows[0].Price", "2 500,5"), ("Rows[0].StockQty", "7"), ("Rows[0].IsActive", "false"));
        var variant = Db(db => db.ProductVariants.Single(v => v.Id == first));
        Assert.Equal((2500.5m, 7, false), (variant.Price!.Value, variant.StockQty, variant.IsActive));
    }

    [Fact]
    public async Task Order_status_changes_from_the_list()
    {
        var client = await Manager();
        var (_, variantId) = SeedProduct($"S{Guid.NewGuid():N}"[..12]);
        var orderId = Db(db =>
        {
            var order = new Order
            {
                User = new ApplicationUser { UserName = $"статус{Guid.NewGuid():N}"[..20] },
                ContactName = "Иван", ContactPhone = "+7 700 111", City = "Актау", StreetAndDetails = "1",
                Items = [new OrderItem { VariantId = variantId, ProductName = "Товар", Price = 1000m, Quantity = 1 }],
            };
            db.Orders.Add(order);
            db.SaveChanges();
            return order.Id;
        });
        var token = await TokenFrom(client, "/admin/orders?status=new");
        Assert.Contains($"№{orderId}", await Page(client, "/admin/orders?q=700+111"));

        var posted = await Post(client, "/admin/orders?handler=SaveList", token, ("Rows[0].Id", orderId.ToString()), ("Rows[0].Status", "Delivering"));
        Assert.Equal(HttpStatusCode.Redirect, posted.StatusCode);
        Assert.Equal(OrderStatus.Delivering, Db(db => db.Orders.Single(o => o.Id == orderId).Status));
        Assert.Contains("Статус изменён у заказов: 1.", await Page(client, "/admin/orders"));
    }

    [Fact]
    public async Task Import_through_the_browser_applies_only_checked_rows_with_edits()
    {
        var client = await Manager();
        var token = await TokenFrom(client, "/admin/import");
        var price = OneCFiles.PriceList(_files);
        var stock = OneCFiles.Stock(_files);

        var preview = await PostFiles(client, "/admin/import", token,
            [("PriceList", price, "прайс лист.xlsx"), ("Stock", stock, "остатки.xlsx")]);
        var html = await Text(preview);
        Assert.Contains("Проверьте перед записью в базу", html);
        Assert.Contains("Active Foam Pink", html);
        Assert.Contains("без заголовка категории", html);
        var priceTemp = System.Text.RegularExpressions.Regex.Match(html, "name=\"PriceTempName\" value=\"([0-9a-f]{32}\\.xlsx)\"").Groups[1].Value;
        var stockTemp = System.Text.RegularExpressions.Regex.Match(html, "name=\"StockTempName\" value=\"([0-9a-f]{32}\\.xlsx)\"").Groups[1].Value;

        // Подставленное имя файла с переходом в другую папку не принимается.
        var traversal = await Post(client, "/admin/import/confirm", token, ("PriceTempName", "..\\..\\shop.db"));
        Assert.Equal(HttpStatusCode.Redirect, traversal.StatusCode);

        var result = await Post(client, "/admin/import/confirm", token,
            ("PriceTempName", priceTemp), ("StockTempName", stockTemp),
            ("SelectedProducts", "131122"), ("SelectedProducts", "125333"),
            ("name_131122", "Active Foam Pink 2026"), ("category_131122", "Пена"), ("brand_131122", ""),
            ("SelectedStock", "131122"), ("qty_131122", "40"), ("SelectedStock", "555555"));
        var page = await Text(result);
        Assert.Contains("Готово", page);
        Assert.Contains("Артикулов из остатков без пары в каталоге: 1", page);

        Db(db =>
        {
            var foam = db.Products.Include(p => p.Category.Brand).Include(p => p.Variants).Single(p => p.Name == "Active Foam Pink 2026");
            Assert.Equal(("Ifoam", "Пена"), (foam.Category.Brand.Name, foam.Category.Name));
            Assert.Equal(40, foam.Variants.Single(v => v.Article == "131122").StockQty);
            Assert.True(db.ProductVariants.Any(v => v.Article == "125333"));
            Assert.False(db.ProductVariants.Any(v => v.Article == "777"));
            return 0;
        });

        // Временные файлы удалены: повторное подтверждение ничего не находит.
        var again = await Post(client, "/admin/import/confirm", token, ("PriceTempName", priceTemp));
        Assert.Equal(HttpStatusCode.Redirect, again.StatusCode);
    }

    [Fact]
    public async Task Import_rejects_files_that_are_not_1c_reports()
    {
        var client = await Manager();
        var token = await TokenFrom(client, "/admin/import");
        var garbage = Path.Combine(_files, "мусор.xlsx");
        await File.WriteAllTextAsync(garbage, "это не Excel");

        var response = await PostFiles(client, "/admin/import", token, [("PriceList", garbage, "мусор.xlsx")]);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Не смог разобрать файл", await Page(client, "/admin/import"));

        var wrongType = await PostFiles(client, "/admin/import", token, [("Stock", garbage, "остатки.csv")]);
        Assert.Equal(HttpStatusCode.Redirect, wrongType.StatusCode);
        Assert.Contains("не файл Excel", await Page(client, "/admin/import"));
    }

    [Fact]
    public async Task Banner_accepts_only_images_and_replaces_old_file()
    {
        var client = await Manager();
        var token = await TokenFrom(client, "/admin/banners/add");
        var png = Path.Combine(_files, "banner.png");
        await File.WriteAllBytesAsync(png, OneCFiles.Png);
        var fake = Path.Combine(_files, "fake.png");
        await File.WriteAllTextAsync(fake, "<script>");

        var rejected = await PostFiles(client, "/admin/banners/add", token, [("Image", fake, "fake.png")], ("Input.Title", "Поддельный"));
        Assert.Contains("не похоже на картинку", await Text(rejected));

        var title = $"Акция {Guid.NewGuid():N}"[..16];
        await PostFiles(client, "/admin/banners/add", token, [("Image", png, "banner.png")], ("Input.Title", title), ("Input.IsActive", "true"));
        var banner = Db(db => db.Banners.Single(b => b.Title == title));
        var mediaRoot = factory.Services.GetRequiredService<Web.Services.MediaStorage>().Root;
        var firstFile = Path.Combine(mediaRoot, banner.ImagePath);
        Assert.True(File.Exists(firstFile));

        await PostFiles(client, $"/admin/banners/{banner.Id}", token, [("Image", png, "banner.png")], ("Input.Title", title));
        var replaced = Db(db => db.Banners.Single(b => b.Id == banner.Id)).ImagePath;
        Assert.NotEqual(banner.ImagePath, replaced);
        Assert.False(File.Exists(firstFile));
        Assert.True(File.Exists(Path.Combine(mediaRoot, replaced)));
    }

    [Fact]
    public async Task Manager_role_is_granted_from_panel_and_console_but_not_removed_from_self()
    {
        var managerLogin = await CreateUser(manager: true);
        var client = await SignIn(managerLogin);
        var buyerLogin = await CreateUser(manager: false);
        var buyerId = Db(db => db.Users.Single(u => u.UserName == buyerLogin).Id);
        var selfId = Db(db => db.Users.Single(u => u.UserName == managerLogin).Id);
        var token = await TokenFrom(client, "/admin/users");

        await Post(client, $"/admin/users?handler=ToggleManager&id={buyerId}", token);
        Assert.True(await IsManager(buyerLogin));

        await Post(client, $"/admin/users?handler=ToggleManager&id={selfId}", token);
        Assert.Contains("Снять доступ с самого себя нельзя.", await Page(client, "/admin/users"));
        Assert.True(await IsManager(managerLogin));

        var consoleLogin = await CreateUser(manager: false);
        Assert.True(await Cli.RunAsync(["--make-manager", consoleLogin], factory.Services));
        Assert.True(await IsManager(consoleLogin));
    }

    private async Task<bool> IsManager(string login)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await users.IsInRoleAsync((await users.FindByNameAsync(login))!, RoleNames.Manager);
    }
}
