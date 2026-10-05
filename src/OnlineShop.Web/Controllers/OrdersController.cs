using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Web.Controllers;

[Authorize]
[Route("/orders/my")]
public class OrdersController(ShopDbContext db) : Controller
{
    /// <summary>История заказов покупателя, новые сверху.</summary>
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = User.ShopUserId();
        var orders = await db.Orders
            .Where(o => o.UserId == userId)
            .Include(o => o.Items)
            .Ordered()
            .AsNoTracking()
            .ToListAsync();
        return View(orders);
    }

    /// <summary>Страница заказа. Чужой заказ отвечает «не найдено», чтобы по номеру нельзя было узнать, что он существует.</summary>
    [HttpGet("{id:int}")]
    public async Task<IActionResult> Detail(int id)
    {
        var userId = User.ShopUserId();
        var order = await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .AsSplitQuery()
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);

        return order is null ? NotFound() : View(order);
    }
}
