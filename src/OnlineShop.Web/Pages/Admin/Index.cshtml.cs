using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin;

/// <summary>Главная панели: частые действия крупными карточками, чтобы не искать их по разделам.</summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public int NewOrders { get; private set; }
    public int Products { get; private set; }
    public int OutOfStock { get; private set; }

    public async Task OnGetAsync()
    {
        NewOrders = await db.Orders.CountAsync(o => o.Status == OrderStatus.New);
        Products = await db.Products.CountAsync();
        OutOfStock = await db.ProductVariants.CountAsync(v => v.IsActive && v.StockQty <= 0);
    }
}
