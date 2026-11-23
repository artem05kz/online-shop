using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Controllers;

[Route("/favorites")]
public class FavoritesController(ShopDbContext db, Favorites favorites) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var ids = await favorites.IdsAsync();
        // Снятые с продажи товары не показываем, но из избранного не убираем: вернутся в продажу, появятся снова.
        var products = await db.Products
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .ForListing()
            .OrderBy(p => p.Name)
            .ToListAsync();
        return View(products);
    }

    /// <summary>
    /// Кнопка с сердечком. Скрипт на странице просит ответ JSON и меняет кнопку на месте, без скрипта
    /// форма отправляется как обычно и возвращает на ту же страницу.
    /// </summary>
    [HttpPost("toggle/{productId:int}")]
    public async Task<IActionResult> Toggle(int productId, string? next = null)
    {
        var product = await db.Products.Where(p => p.Id == productId && p.IsActive)
            .Select(p => new { p.Id, p.Name }).FirstOrDefaultAsync();
        if (product is null)
        {
            return NotFound();
        }

        var active = await favorites.ToggleAsync(product.Id);
        if (Request.Headers.Accept.ToString().Contains("application/json"))
        {
            var ids = await favorites.IdsAsync();
            var count = await db.Products.CountAsync(p => ids.Contains(p.Id) && p.IsActive);
            return Json(new { active, count });
        }

        if (active)
        {
            TempData.Flash($"«{product.Name}» в избранном.", "/favorites", "Открыть избранное");
        }
        else
        {
            TempData.Flash($"«{product.Name}» убран из избранного.");
        }
        return Url.IsLocalUrl(next) ? LocalRedirect(next) : RedirectToAction(nameof(Index));
    }
}
