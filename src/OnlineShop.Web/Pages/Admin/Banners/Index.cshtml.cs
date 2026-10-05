using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Web.Pages.Admin.Banners;

public class IndexModel(ShopDbContext db) : PageModel
{
    public class Row
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string ImagePath { get; set; } = "";
        public DateTime? StartsAt { get; set; }
        public DateTime? EndsAt { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    [BindProperty] public List<Row> Rows { get; set; } = [];

    public async Task OnGetAsync()
    {
        Rows = await db.Banners.Ordered()
            .Select(b => new Row
            {
                Id = b.Id, Title = b.Title, ImagePath = b.ImagePath, StartsAt = b.StartsAt, EndsAt = b.EndsAt,
                SortOrder = b.SortOrder, IsActive = b.IsActive,
            })
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostSaveListAsync()
    {
        var byId = Rows.ToDictionary(r => r.Id);
        var banners = await db.Banners.Where(b => byId.Keys.Contains(b.Id)).ToListAsync();
        foreach (var banner in banners)
        {
            banner.SortOrder = Math.Max(0, byId[banner.Id].SortOrder);
            banner.IsActive = byId[banner.Id].IsActive;
        }
        await db.SaveChangesAsync();
        TempData.Flash($"Сохранено строк: {banners.Count}.");
        return RedirectToPage();
    }
}
