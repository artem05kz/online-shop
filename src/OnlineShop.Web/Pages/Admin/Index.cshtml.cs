using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin;

/// <summary>
/// Главная панели: частые действия крупными карточками и счётчики того, что требует внимания.
/// Каждый счётчик ведёт сразу в список с нужным фильтром.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public int NewOrders { get; private set; }
    public int Products { get; private set; }
    public int NoPhoto { get; private set; }
    public int NoPrice { get; private set; }
    public int NoStock { get; private set; }

    public async Task OnGetAsync()
    {
        NewOrders = await db.Orders.CountAsync(o => o.Status == OrderStatus.New);
        Products = await db.Products.CountAsync();
        NoPhoto = await db.Products.CountAsync(p => !p.Images.Any());
        NoPrice = await db.Products.CountAsync(p => p.Variants.Any(v => v.IsActive && v.Price == null));
        NoStock = await db.Products.CountAsync(p => !p.Variants.Any(v => v.IsActive && v.StockQty > 0));
    }
}
