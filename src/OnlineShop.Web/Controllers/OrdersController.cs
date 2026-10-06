using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Controllers;

[Authorize]
[Route("/orders/my")]
public class OrdersController(ShopDbContext db, Cart cart) : Controller
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

    /// <summary>
    /// «Повторить заказ»: те же фасовки в том же количестве в корзину, по сегодняшним ценам
    /// (цена в корзине всегда берётся из каталога). Снятые с продажи позиции пропускаются.
    /// </summary>
    [HttpPost("{id:int}/repeat")]
    public async Task<IActionResult> Repeat(int id)
    {
        var userId = User.ShopUserId();
        var order = await db.Orders
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v.Product)
            .AsNoTracking()
            .FirstOrDefaultAsync(o => o.Id == id && o.UserId == userId);
        if (order is null)
        {
            return NotFound();
        }

        var added = 0;
        var skipped = new List<string>();
        foreach (var item in order.Items.OrderBy(i => i.Id))
        {
            var v = item.Variant;
            if (v.IsActive && v.Price is not null && v.Product.IsActive)
            {
                cart.Add(v.Id, item.Quantity);
                added++;
            }
            else
            {
                skipped.Add(string.IsNullOrEmpty(item.PackLabel) ? item.ProductName : $"{item.ProductName} ({item.PackLabel})");
            }
        }

        if (added > 0)
        {
            TempData.Flash($"В корзину добавлено позиций из заказа №{order.Id}: {added}. Цены сегодняшние, проверьте итог.");
        }
        if (skipped.Count > 0)
        {
            TempData.Flash("Сейчас нет в продаже: " + string.Join(", ", skipped) + ".", added > 0 ? FlashLevel.Info : FlashLevel.Error);
        }
        return RedirectToAction("Index", "Cart");
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
