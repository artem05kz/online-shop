using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Controllers;

[Authorize]
[Route("/orders/my")]
public class OrdersController(ShopDbContext db, UserManager<ApplicationUser> userManager) : Controller
{
    /// <summary>Страница заказа. Чужой заказ отвечает «не найдено», чтобы по номеру нельзя было узнать, что он существует.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        var userId = int.Parse(userManager.GetUserId(User)!);
        var order = await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        return order is null ? NotFound() : View(order);
    }
}
