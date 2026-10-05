using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Stores;

public class IndexModel(ShopDbContext db) : PageModel
{
    public class Row
    {
        public int Id { get; set; }
        public string Address { get; set; } = "";
        public string City { get; set; } = "";
        public StoreKind Kind { get; set; }
        public string WorkingHours { get; set; } = "";
        public string Phone { get; set; } = "";
        public int SortOrder { get; set; }
        public bool IsActive { get; set; }
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? City { get; set; }
    [BindProperty(SupportsGet = true)] public string? Kind { get; set; }
    [BindProperty(SupportsGet = true)] public string? Active { get; set; }

    [BindProperty] public List<Row> Rows { get; set; } = [];
    public IReadOnlyList<string> Cities { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Cities = await db.Stores.Select(s => s.City).Distinct().OrderBy(c => c).ToListAsync();
        var stores = await db.Stores.Ordered().AsNoTracking().ToListAsync();

        // Точек немного, поэтому фильтруем в памяти: так проще и поиск не зависит от регистра.
        IEnumerable<Store> filtered = stores;
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var needle = Q.Trim();
            filtered = filtered.Where(s => new[] { s.Address, s.City, s.Phone }
                .Any(f => f.Contains(needle, StringComparison.OrdinalIgnoreCase)));
        }
        if (!string.IsNullOrEmpty(City))
        {
            filtered = filtered.Where(s => s.City == City);
        }
        if (!string.IsNullOrEmpty(Kind))
        {
            filtered = filtered.Where(s => s.Kind.ToCode() == Kind);
        }
        if (Active is "1" or "0")
        {
            filtered = filtered.Where(s => s.IsActive == (Active == "1"));
        }
        Rows = filtered.Select(s => new Row
        {
            Id = s.Id, Address = s.Address, City = s.City, Kind = s.Kind, WorkingHours = s.WorkingHours,
            Phone = s.Phone, SortOrder = s.SortOrder, IsActive = s.IsActive,
        }).ToList();
    }

    public async Task<IActionResult> OnPostSaveListAsync()
    {
        var byId = Rows.ToDictionary(r => r.Id);
        var stores = await db.Stores.Where(s => byId.Keys.Contains(s.Id)).ToListAsync();
        foreach (var store in stores)
        {
            store.SortOrder = Math.Max(0, byId[store.Id].SortOrder);
            store.IsActive = byId[store.Id].IsActive;
        }
        await db.SaveChangesAsync();
        TempData.Flash($"Сохранено строк: {stores.Count}.");
        return RedirectToPage(new { q = Q, city = City, kind = Kind, active = Active });
    }
}
