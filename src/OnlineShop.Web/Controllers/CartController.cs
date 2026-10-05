using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Controllers;

[Route("/orders/cart")]
public class CartController(ShopDbContext db, Cart cart) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index() => View(await cart.LoadAsync());

    [HttpPost("add/{variantId:int}")]
    public async Task<IActionResult> Add(int variantId, int quantity = 1, string? next = null)
    {
        // Купить можно только видимую фасовку с ценой: кнопка «В корзину» на сайте показывается только для таких.
        var variant = await db.ProductVariants
            .Include(v => v.Product)
            .FirstOrDefaultAsync(v => v.Id == variantId && v.IsActive && v.Price != null && v.Product.IsActive);
        if (variant is null)
        {
            return NotFound();
        }

        cart.Add(variant.Id, Math.Max(1, quantity));
        TempData.Flash($"«{variant.Product.Name}» в корзине.");

        // Возвращаем только на страницы своего сайта, чтобы ссылкой нельзя было увести покупателя на чужой.
        return Url.IsLocalUrl(next) ? LocalRedirect(next) : RedirectToAction(nameof(Index));
    }

    [HttpPost("update/{variantId:int}")]
    public IActionResult Update(int variantId, int quantity = 1)
    {
        cart.SetQuantity(variantId, quantity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("remove/{variantId:int}")]
    public IActionResult Remove(int variantId)
    {
        cart.Remove(variantId);
        return RedirectToAction(nameof(Index));
    }
}
