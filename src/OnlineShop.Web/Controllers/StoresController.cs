using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.ViewModels;

namespace OnlineShop.Web.Controllers;

public class StoresController(ShopDbContext db, IConfiguration configuration) : Controller
{
    /// <summary>Адреса физических точек: пункты самовывоза и магазины.</summary>
    [HttpGet("/addresses")]
    public async Task<IActionResult> Index(string? city)
    {
        var stores = await db.Stores.Where(s => s.IsActive).Ordered().AsNoTracking().ToListAsync();
        var selected = (city ?? "").Trim();

        return View(new StoreListViewModel
        {
            Cities = stores.Select(s => s.City).Distinct().Order().ToList(),
            SelectedCity = selected,
            Stores = selected.Length == 0 ? stores : stores.Where(s => s.City == selected).ToList(),
            MapKey = TwoGis.Key(configuration),
        });
    }
}
