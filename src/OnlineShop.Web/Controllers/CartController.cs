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

        await cart.AddAsync(variant.Id, Math.Max(1, quantity));
        TempData.Flash($"«{variant.Product.Name}» в корзине.");

        // Возвращаем только на страницы своего сайта, чтобы ссылкой нельзя было увести покупателя на чужой.
        return Url.IsLocalUrl(next) ? LocalRedirect(next) : RedirectToAction(nameof(Index));
    }

    /// <summary>Добавление со страницы товара, где фасовка выбирается переключателем в той же форме.</summary>
    [HttpPost("add")]
    public Task<IActionResult> AddSelected([FromForm] int variantId, int quantity = 1, string? next = null) =>
        Add(variantId, quantity, next);

    [HttpPost("update/{variantId:int}")]
    public async Task<IActionResult> Update(int variantId, int quantity = 1)
    {
        await cart.SetQuantityAsync(variantId, quantity);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("remove/{variantId:int}")]
    public async Task<IActionResult> Remove(int variantId)
    {
        await cart.RemoveAsync(variantId);
        return RedirectToAction(nameof(Index));
    }
}
