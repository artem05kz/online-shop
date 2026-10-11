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

        var name = variant.PackLabel.Length > 0 ? $"{variant.Product.Name}, {variant.PackLabel}" : variant.Product.Name;
        var inCart = await cart.AddAsync(variant, quantity);
        if (inCart == 0)
        {
            TempData.Flash($"«{name}» закончился, положить в корзину нельзя.", FlashLevel.Error);
        }
        else if (inCart < quantity || inCart == variant.AvailableQty)
        {
            // Попросили больше, чем есть, или упёрлись в остаток вместе с тем, что уже лежало в корзине.
            TempData.Flash($"В наличии только {variant.AvailableQty} шт. «{name}», в корзине {inCart} шт.", FlashLevel.Info);
        }
        else
        {
            TempData.Flash($"«{name}» в корзине.");
        }

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
        var variant = await db.ProductVariants.AsNoTracking().FirstOrDefaultAsync(v => v.Id == variantId);
        var available = variant?.AvailableQty ?? 0;
        if (quantity > available && available > 0)
        {
            TempData.Flash($"В наличии только {available} шт., больше заказать нельзя.", FlashLevel.Info);
        }
        await cart.SetQuantityAsync(variantId, quantity, available > 0 ? available : null);
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("remove/{variantId:int}")]
    public async Task<IActionResult> Remove(int variantId)
    {
        await cart.RemoveAsync(variantId);
        return RedirectToAction(nameof(Index));
    }
}
