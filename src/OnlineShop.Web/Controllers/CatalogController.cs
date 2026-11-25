using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;
using OnlineShop.Web.ViewModels;

namespace OnlineShop.Web.Controllers;

public class CatalogController(ShopDbContext db, SearchSpelling spelling, IConfiguration config) : Controller
{
    private const int FeaturedCount = 8;
    private const int RelatedCount = 4;
    private const int RecentCount = 4;
    private const int BoughtWithCount = 4;

    [HttpGet("/")]
    public async Task<IActionResult> Home()
    {
        // Баннеры из панели — первыми слайдами первого экрана (решение дизайнера 25.11: готовые картинки 2400 × 880).
        // Выключить их все разом можно настройкой Storefront:ShowBanners = false.
        var banners = config.GetValue("Storefront:ShowBanners", true)
            ? await db.Banners.VisibleAt(DateTime.UtcNow).Ordered().AsNoTracking().ToListAsync()
            : [];

        // Скрытый бренд прячет и свои категории. В Django-версии плитки категорий скрытого бренда
        // оставались на главной.
        var rootCategories = await db.Categories
            .Where(c => c.IsActive && c.ParentId == null && c.Brand.IsActive)
            .Include(c => c.Brand)
            .OrderBy(c => c.Brand.SortOrder).ThenBy(c => c.Brand.Name)
            .ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .AsNoTracking()
            .ToListAsync();
        var productCounts = await db.Products
            .Where(p => p.IsActive)
            .GroupBy(p => p.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        // У плитки категории фото первого товара с фото: категорию узнают по картинке быстрее, чем по названию.
        var tileIds = rootCategories.Where(c => productCounts.GetValueOrDefault(c.Id) > 0).Select(c => c.Id).ToList();
        var tileImages = (await db.ProductImages
                .Where(i => i.Product.IsActive && tileIds.Contains(i.Product.CategoryId))
                .OrderBy(i => i.Product.Name).ThenByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder)
                .Select(i => new { i.Product.CategoryId, i.ImagePath })
                .ToListAsync())
            .GroupBy(x => x.CategoryId)
            .ToDictionary(g => g.Key, g => g.First().ImagePath);

        // Раздел, где есть товар с 3D-рендером, показывает рендер: он чище фото из прайса.
        var renderSlugs = TileRenders.BySlug.Keys.ToList();
        var tileRenders = (await db.Products
                .Where(p => p.IsActive && tileIds.Contains(p.CategoryId) && renderSlugs.Contains(p.Slug))
                .OrderBy(p => p.Name)
                .Select(p => new { p.CategoryId, p.Slug })
                .ToListAsync())
            .GroupBy(x => x.CategoryId)
            .ToDictionary(g => g.Key, g => TileRenders.BySlug[g.First().Slug]);

        var tiles = rootCategories
            .Where(c => tileIds.Contains(c.Id))
            .Select(c => new CategoryTile(c, productCounts[c.Id], tileImages.GetValueOrDefault(c.Id),
                tileRenders.GetValueOrDefault(c.Id)))
            .ToList();

        // «Хиты продаж» — товары из списка заказчицы (HitProducts), сначала те, что есть в наличии.
        // «Есть в наличии» — остальные товары в наличии, чтобы одни и те же карточки не шли два раза подряд.
        var listed = await db.Products.ForListing().AsNoTracking().ToListAsync();
        var hits = listed.Where(HitProducts.IsHit).OrderByDescending(p => p.InStock).ThenBy(p => p.Name).Take(FeaturedCount).ToList();
        var featured = listed
            .Where(p => p.InStock && !HitProducts.IsHit(p))
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name)
            .Take(FeaturedCount)
            .ToList();

        // Первый экран: слайды с товарами из панели. Скрытый товар просто выпадает из слайдов.
        var slides = await db.ProductSlides.Where(s => s.IsActive).OrderBy(s => s.SortOrder).ThenBy(s => s.Id)
            .AsNoTracking().ToListAsync();
        var slideProductIds = slides.Select(s => s.ProductId).ToList();
        var heroProducts = await db.Products.ForListing()
            .Where(p => slideProductIds.Contains(p.Id))
            .AsNoTracking()
            .ToDictionaryAsync(p => p.Id);
        var hero = slides
            .Where(s => heroProducts.ContainsKey(s.ProductId))
            .Select(s => new HeroSlideView(s, heroProducts[s.ProductId]))
            .ToList();

        // «Где нас найти»: первые точки из «Адресов», которые магазин ведёт в панели управления.
        var stores = await db.Stores.Where(s => s.IsActive).Ordered().Take(3).AsNoTracking().ToListAsync();

        return View(new HomeViewModel { Hero = hero, Banners = banners, Tiles = tiles, Featured = featured, Hits = hits, Stores = stores });
    }

    [HttpGet("/catalog/{brandSlug}/{categorySlug}")]
    public async Task<IActionResult> Category(string brandSlug, string categorySlug, string? page)
    {
        var category = await db.Categories
            .Include(c => c.Brand)
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Brand.Slug == brandSlug && c.Slug == categorySlug
                && c.IsActive && c.Brand.IsActive);
        if (category is null)
        {
            return NotFound();
        }

        var filter = CatalogFilter.FromQuery(Request.Query);

        // Фасовки раздела разбираются в памяти: в 1С это строки вида «5,5 кг» или «500мл триггер».
        var packs = (await db.ProductVariants
                .Where(v => v.IsActive && v.Product.IsActive && v.Product.CategoryId == category.Id)
                .Select(v => new { v.ProductId, v.PackLabel })
                .ToListAsync())
            .Select(v => (v.ProductId, Pack: PackSize.Parse(v.PackLabel)))
            .Where(v => v.Pack is not null)
            .Select(v => (v.ProductId, Pack: v.Pack!.Value))
            .ToList();
        var packChips = PackSize.Order
            .Select(g => new PackChip(g, PackSize.Label(g, packs.Select(p => p.Pack)),
                packs.Where(p => p.Pack.Group == g).Select(p => p.ProductId).Distinct().Count()))
            .Where(c => c.Count > 0)
            .ToList();

        var query = db.Products.ForListing()
            .Where(p => p.CategoryId == category.Id)
            .ApplyFilter(filter)
            .ApplySort(filter.Sort)
            .AsNoTracking();
        if (filter.Pack.Length > 0)
        {
            var ids = packs.Where(p => p.Pack.Group == filter.Pack).Select(p => p.ProductId).Distinct().ToList();
            query = query.Where(p => ids.Contains(p.Id));
        }

        var siblings = await db.Categories
            .Where(c => c.BrandId == category.BrandId && c.ParentId == null && c.IsActive)
            .Ordered()
            .Select(c => new { Category = c, Count = c.Products.Count(p => p.IsActive) })
            .AsNoTracking()
            .ToListAsync();

        return View(new ProductListViewModel
        {
            Filter = filter,
            Category = category,
            SideCategories = siblings.Where(x => x.Count > 0 || x.Category.Id == category.Id)
                .Select(x => new CategoryTile(x.Category, x.Count, null)).ToList(),
            // Одна группа на весь раздел ничего не отбирает: кнопки показываем, только если групп хотя бы две.
            PackChips = packChips.Count > 1 ? packChips : [],
            Page = await PagedList<Models.Product>.CreateAsync(query, page, CatalogQuery.PageSize),
        });
    }

    [HttpGet("/search")]
    public async Task<IActionResult> Search(string? q, string? page)
    {
        var text = (q ?? "").Trim();
        var filter = CatalogFilter.FromQuery(Request.Query);

        IQueryable<Models.Product> Find(string what) =>
            db.Products.ForListing().Search(what).ApplyFilter(filter).ApplySort(filter.Sort);

        var query = text.Length == 0 ? db.Products.Where(_ => false) : Find(text);
        string? corrected = null;
        // Ничего не нашлось: возможно, опечатка или не та раскладка. Ищем по исправленному запросу.
        if (text.Length > 0 && !await query.AnyAsync() && await spelling.CorrectAsync(text) is { } fixedText)
        {
            corrected = fixedText;
            query = Find(fixedText);
        }

        // Кнопки категорий в шапке поиска: в каких категориях нашлись товары и сколько (без учёта фильтров,
        // чтобы кнопки не пропадали, когда выбрана одна из них).
        var found = text.Length == 0
            ? []
            : await db.Products.ForListing().Search(corrected ?? text)
                .GroupBy(p => p.CategoryId)
                .Select(g => new { CategoryId = g.Key, Count = g.Count() })
                .ToListAsync();
        var foundIds = found.Select(f => f.CategoryId).ToList();
        var foundCategories = await db.Categories.Where(c => foundIds.Contains(c.Id)).Include(c => c.Brand)
            .Ordered().AsNoTracking().ToListAsync();

        return View(new ProductListViewModel
        {
            Filter = filter,
            Query = text,
            CorrectedQuery = corrected,
            SideCategories = foundCategories
                .Select(c => new CategoryTile(c, found.First(f => f.CategoryId == c.Id).Count, null)).ToList(),
            Page = await PagedList<Models.Product>.CreateAsync(query.AsNoTracking(), page, CatalogQuery.PageSize),
            AllBrands = await db.Brands.Where(b => b.IsActive).Ordered().AsNoTracking().ToListAsync(),
            AllCategories = await db.Categories
                .Where(c => c.IsActive && c.ParentId == null && c.Brand.IsActive)
                .Include(c => c.Brand)
                .Ordered()
                .AsNoTracking()
                .ToListAsync(),
        });
    }

    /// <summary>
    /// Подсказки под строкой поиска: до шести товаров с фото и ценой. Сначала те, у которых
    /// название начинается с набранного, потом остальные совпадения.
    /// </summary>
    [HttpGet("/search/suggest")]
    public async Task<IActionResult> Suggest(string? q)
    {
        var text = (q ?? "").Trim();
        if (text.Length < 2)
        {
            return Json(Array.Empty<object>());
        }
        async Task<List<Models.Product>> Find(string what)
        {
            var needle = what.ToLowerInvariant();
            return await db.Products.ForListing()
                .Search(what)
                .OrderBy(p => SqliteFunctions.Lower(p.Name).StartsWith(needle) ? 0 : 1)
                .ThenBy(p => p.Name)
                .Take(6)
                .AsNoTracking()
                .ToListAsync();
        }
        var products = await Find(text);
        if (products.Count == 0 && await spelling.CorrectAsync(text) is { } fixedText)
        {
            products = await Find(fixedText);
        }

        return Json(products.Select(p => new
        {
            name = p.Name,
            url = Url.Product(p),
            image = Media.PhotoUrl(p, Media.Small),
            price = p.MinPrice is { } price ? (p.HasSeveralPacks ? "от " : "") + Format.Money(price) : "цена уточняется",
            inStock = p.InStock,
        }));
    }

    [HttpGet("/product/{slug}")]
    public async Task<IActionResult> Product(string slug)
    {
        var product = await db.Products.ForListing().AsNoTracking().FirstOrDefaultAsync(p => p.Slug == slug);
        if (product is null)
        {
            return NotFound();
        }

        var related = await db.Products.ForListing()
            .Where(p => p.CategoryId == product.CategoryId && p.Id != product.Id)
            // как в списках: сначала то, что можно купить сейчас
            .ApplySort(CatalogFilter.DefaultSort)
            .Take(RelatedCount)
            .AsNoTracking()
            .ToListAsync();

        // «С этим покупают» (24.11): товары из других разделов, которые лежали в тех же заказах, — чаще всего первыми.
        // Пока заказов мало, остаток добираем хитами продаж из других разделов (сначала те, что в наличии).
        var withOrders = db.OrderItems.Where(i => i.Variant.ProductId == product.Id).Select(i => i.OrderId);
        var boughtIds = await db.OrderItems
            .Where(i => withOrders.Contains(i.OrderId) && i.Variant.ProductId != product.Id && i.Variant.Product.CategoryId != product.CategoryId)
            .GroupBy(i => i.Variant.ProductId)
            .Select(g => new { ProductId = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(BoughtWithCount * 2)
            .Select(x => x.ProductId)
            .ToListAsync();
        var otherSections = await db.Products.ForListing().Where(p => p.CategoryId != product.CategoryId).AsNoTracking().ToListAsync();
        var boughtWith = RecentlyViewed.InOrder(otherSections, p => p.Id, boughtIds).Where(p => p.InStock).ToList();
        boughtWith.AddRange(otherSections
            .Where(p => HitProducts.IsHit(p) && !boughtWith.Contains(p))
            .OrderByDescending(p => p.InStock).ThenBy(p => p.Name));
        // Хитов в других разделах может не хватить — добираем товарами в наличии из других разделов.
        boughtWith.AddRange(otherSections.Where(p => p.InStock && !boughtWith.Contains(p)).OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name));
        boughtWith = boughtWith.Take(BoughtWithCount).ToList();

        // Недавно смотрели: до запоминания текущего, сам товар не показываем.
        var recentIds = RecentlyViewed.Read(Request).Where(id => id != product.Id).Take(RecentCount).ToList();
        var recentProducts = recentIds.Count == 0 ? [] : await db.Products.ForListing().Where(p => recentIds.Contains(p.Id)).AsNoTracking().ToListAsync();
        var recent = RecentlyViewed.InOrder(recentProducts, p => p.Id, recentIds);
        RecentlyViewed.Remember(HttpContext, product.Id);

        var reviews = await db.Reviews.Where(r => r.ProductId == product.Id && r.Status == ReviewStatus.Approved)
            .Include(r => r.User).OrderByDescending(r => r.CreatedAt).AsNoTracking().ToListAsync();
        var userId = User.Identity?.IsAuthenticated == true ? User.ShopUserId() : (int?)null;
        var canReview = userId is { } uid && await ReviewsController.CanReviewAsync(db, uid, product.Id);
        var myReview = userId is { } me ? await db.Reviews.AsNoTracking().FirstOrDefaultAsync(r => r.ProductId == product.Id && r.UserId == me) : null;

        return View(new ProductPageViewModel
        {
            Product = product, Related = related, Reviews = reviews, CanReview = canReview, MyReview = myReview,
            BoughtWith = boughtWith, Recent = recent,
        });
    }

    /// <summary>
    /// Страница ошибки вместо пустого ответа. Сюда же попадают и формы (POST), поэтому проверку
    /// токена формы здесь не делаем: иначе ошибка «форма устарела» сама вызвала бы ошибку.
    /// </summary>
    [Route("/error/{code:int}")]
    [IgnoreAntiforgeryToken]
    public IActionResult Error(int code)
    {
        Response.StatusCode = code is >= 400 and <= 599 ? code : StatusCodes.Status500InternalServerError;
        return code == StatusCodes.Status404NotFound ? View("NotFound") : View("Error", Response.StatusCode);
    }
}
