using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;
using OnlineShop.Web.ViewModels;

namespace OnlineShop.Web.Controllers;

public class CatalogController(ShopDbContext db) : Controller
{
    private const int FeaturedCount = 8;
    private const int RelatedCount = 4;

    [HttpGet("/")]
    public async Task<IActionResult> Home()
    {
        var banners = await db.Banners.VisibleAt(DateTime.UtcNow).Ordered().AsNoTracking().ToListAsync();

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

        var tiles = rootCategories
            .Where(c => tileIds.Contains(c.Id))
            .Select(c => new CategoryTile(c, productCounts[c.Id], tileImages.GetValueOrDefault(c.Id)))
            .ToList();

        var featured = await db.Products.ForListing()
            .Where(p => p.Variants.Any(v => v.IsActive && v.StockQty > 0))
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name)
            .Take(FeaturedCount)
            .AsNoTracking()
            .ToListAsync();

        return View(new HomeViewModel { Banners = banners, Tiles = tiles, Featured = featured });
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
        var query = db.Products.ForListing()
            .Where(p => p.CategoryId == category.Id)
            .ApplyFilter(filter)
            .ApplySort(filter.Sort)
            .AsNoTracking();

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
            Page = await PagedList<Models.Product>.CreateAsync(query, page, CatalogQuery.PageSize),
        });
    }

    [HttpGet("/search")]
    public async Task<IActionResult> Search(string? q, string? page)
    {
        var text = (q ?? "").Trim();
        var filter = CatalogFilter.FromQuery(Request.Query);

        var query = text.Length == 0
            ? db.Products.Where(_ => false)
            : db.Products.ForListing().Search(text).ApplyFilter(filter).ApplySort(filter.Sort);

        return View(new ProductListViewModel
        {
            Filter = filter,
            Query = text,
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
        var needle = text.ToLowerInvariant();
        var products = await db.Products.ForListing()
            .Search(text)
            .OrderBy(p => SqliteFunctions.Lower(p.Name).StartsWith(needle) ? 0 : 1)
            .ThenBy(p => p.Name)
            .Take(6)
            .AsNoTracking()
            .ToListAsync();

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
            .Ordered()
            .Take(RelatedCount)
            .AsNoTracking()
            .ToListAsync();

        return View(new ProductPageViewModel { Product = product, Related = related });
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
