using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Categories;

public class IndexModel(ShopDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Brand { get; set; }
    [BindProperty(SupportsGet = true)] public string? Active { get; set; }

    public IReadOnlyList<Brand> Brands { get; private set; } = [];
    public PagedList<Category> List { get; private set; } = null!;

    public async Task OnGetAsync(string? p)
    {
        Brands = await db.Brands.Ordered().AsNoTracking().ToListAsync();

        var query = db.Categories.Include(c => c.Brand).Include(c => c.Parent).AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var needle = Q.Trim().ToLowerInvariant();
            query = query.Where(c => SqliteFunctions.Lower(c.Name).Contains(needle));
        }
        if (Brand is not null)
        {
            query = query.Where(c => c.BrandId == Brand);
        }
        if (Active is "1" or "0")
        {
            var active = Active == "1";
            query = query.Where(c => c.IsActive == active);
        }
        List = await PagedList<Category>.CreateAsync(
            query.OrderBy(c => c.Brand.SortOrder).ThenBy(c => c.Brand.Name).ThenBy(c => c.SortOrder).ThenBy(c => c.Name),
            p, AdminList.PageSize);
    }
}
