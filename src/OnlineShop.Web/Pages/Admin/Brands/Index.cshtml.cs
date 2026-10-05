using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Web.Pages.Admin.Brands;

public class IndexModel(ShopDbContext db) : PageModel
{
    public class Row
    {
        public int Id { get; set; }
        public string Name { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    [BindProperty]
    public List<Row> Rows { get; set; } = [];

    public async Task OnGetAsync()
    {
        Rows = await db.Brands.Ordered()
            .Select(b => new Row { Id = b.Id, Name = b.Name, SortOrder = b.SortOrder, IsActive = b.IsActive })
            .ToListAsync();
    }

    /// <summary>Очерёдность и показ правятся прямо в списке и сохраняются одной кнопкой.</summary>
    public async Task<IActionResult> OnPostSaveListAsync()
    {
        var byId = Rows.ToDictionary(r => r.Id);
        var brands = await db.Brands.Where(b => byId.Keys.Contains(b.Id)).ToListAsync();
        foreach (var brand in brands)
        {
            brand.SortOrder = Math.Max(0, byId[brand.Id].SortOrder);
            brand.IsActive = byId[brand.Id].IsActive;
        }
        await db.SaveChangesAsync();
        TempData.Flash($"Сохранено строк: {brands.Count}.");
        return RedirectToPage();
    }
}
