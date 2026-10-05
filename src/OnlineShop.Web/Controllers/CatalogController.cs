using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Services;
using OnlineShop.Web.ViewModels;

namespace OnlineShop.Web.Controllers;

public class CatalogController(ShopDbContext db) : Controller
{
    private const int FeaturedCount = 8;
    private const int SectionSize = 4;
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

        var tiles = rootCategories
            .Where(c => productCounts.GetValueOrDefault(c.Id) > 0)
            .Select(c => new CategoryTile(c, productCounts[c.Id]))
            .ToList();

        var featured = await db.Products.ForListing()
            .Where(p => p.Variants.Any(v => v.IsActive && v.StockQty > 0))
            .OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name)
            .Take(FeaturedCount)
            .AsNoTracking()
            .ToListAsync();

        // Категорий немного, поэтому по запросу на каждую проще, чем грузить весь каталог ради четырёх товаров.
        var sections = new List<CategorySection>();
        foreach (var tile in tiles)
        {
            var products = await db.Products.ForListing()
                .Where(p => p.CategoryId == tile.Category.Id)
                .Ordered()
                .Take(SectionSize)
                .AsNoTracking()
                .ToListAsync();
            sections.Add(new CategorySection(tile.Category.Brand, tile.Category, products));
        }

        return View(new HomeViewModel { Banners = banners, Tiles = tiles, Featured = featured, Sections = sections });
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

        return View(new ProductListViewModel
        {
            Filter = filter,
            Category = category,
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

    [Route("/not-found")]
    public IActionResult PageNotFound()
    {
        Response.StatusCode = StatusCodes.Status404NotFound;
        return View("NotFound");
    }
}
