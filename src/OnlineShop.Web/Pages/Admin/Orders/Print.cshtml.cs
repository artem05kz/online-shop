using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Orders;

/// <summary>Лист заказа для печати: всё, что нужно, чтобы собрать заказ и отдать его покупателю.</summary>
public class PrintModel(ShopDbContext db) : PageModel
{
    public Order Order { get; private set; } = null!;
    public SiteContact? Contact { get; private set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var order = await db.Orders.Include(o => o.Items).AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);
        if (order is null)
        {
            return NotFound();
        }
        Order = order;
        Contact = await db.SiteContacts.Where(c => c.IsActive).OrderBy(c => c.Id).AsNoTracking().FirstOrDefaultAsync();
        return Page();
    }
}
