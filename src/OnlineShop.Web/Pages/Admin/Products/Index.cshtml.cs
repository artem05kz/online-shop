using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Products;

public class IndexModel(ShopDbContext db) : PageModel
{
    public record Row(int Id, string Name, string Brand, string Category, int VariantCount, bool IsActive, DateTime UpdatedAt);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Brand { get; set; }
    [BindProperty(SupportsGet = true)] public int? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? Active { get; set; }
    [BindProperty(SupportsGet = true)] public string? O { get; set; }

    public IReadOnlyList<Brand> Brands { get; private set; } = [];
    public IReadOnlyList<Category> Categories { get; private set; } = [];
    public PagedList<Row> List { get; private set; } = null!;

    public async Task OnGetAsync(string? p)
    {
        Brands = await db.Brands.Ordered().AsNoTracking().ToListAsync();
        Categories = await db.Categories.Include(c => c.Brand)
            .Where(c => Brand == null || c.BrandId == Brand)
            .OrderBy(c => c.Brand.Name).ThenBy(c => c.Name).AsNoTracking().ToListAsync();

        var query = db.Products.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            // Поиск по названию, описанию и артикулу, как в Django-админке.
            query = query.Search(Q);
        }
        if (Brand is not null)
        {
            query = query.Where(p => p.Category.BrandId == Brand);
        }
        if (Category is not null)
        {
            query = query.Where(p => p.CategoryId == Category);
        }
        if (Active is "1" or "0")
        {
            var active = Active == "1";
            query = query.Where(p => p.IsActive == active);
        }

        query = O switch
        {
            "-name" => query.OrderByDescending(p => p.Name),
            "brand" => query.OrderBy(p => p.Category.Brand.Name).ThenBy(p => p.Name),
            "-brand" => query.OrderByDescending(p => p.Category.Brand.Name).ThenBy(p => p.Name),
            "category" => query.OrderBy(p => p.Category.Name).ThenBy(p => p.Name),
            "-category" => query.OrderByDescending(p => p.Category.Name).ThenBy(p => p.Name),
            "active" => query.OrderBy(p => p.IsActive).ThenBy(p => p.Name),
            "-active" => query.OrderByDescending(p => p.IsActive).ThenBy(p => p.Name),
            "updated" => query.OrderBy(p => p.UpdatedAt),
            "-updated" => query.OrderByDescending(p => p.UpdatedAt),
            _ => query.OrderBy(p => p.Name),
        };

        var rows = query.Select(p => new Row(p.Id, p.Name, p.Category.Brand.Name, p.Category.Name,
            p.Variants.Count, p.IsActive, p.UpdatedAt));
        List = await PagedList<Row>.CreateAsync(rows, p, AdminList.PageSize);
    }
}
