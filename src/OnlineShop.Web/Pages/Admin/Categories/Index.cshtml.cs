using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Categories;

/// <summary>
/// Бренды и категории одним деревом: видно, что во что вложено и сколько где товаров.
/// Порядок меняется стрелками, без чисел очерёдности.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public CatalogTree Tree { get; private set; } = null!;

    public async Task OnGetAsync() => Tree = await CatalogTree.LoadAsync(db);

    /// <summary>Сдвинуть бренд или категорию на одно место вверх (direction = -1) или вниз (+1) среди соседей.</summary>
    public async Task<IActionResult> OnPostMoveAsync(string kind, int id, int direction)
    {
        if (kind == "brand")
        {
            var brands = await db.Brands.Ordered().ToListAsync();
            Swap(brands, brands.FindIndex(b => b.Id == id), direction, (b, order) => b.SortOrder = order);
        }
        else
        {
            var category = await db.Categories.FindAsync(id);
            if (category is null)
            {
                return NotFound();
            }
            var siblings = await db.Categories
                .Where(c => c.BrandId == category.BrandId && c.ParentId == category.ParentId)
                .Ordered().ToListAsync();
            Swap(siblings, siblings.FindIndex(c => c.Id == id), direction, (c, order) => c.SortOrder = order);
        }
        await db.SaveChangesAsync();
        return Redirect($"/admin/categories#{kind}-{id}");
    }

    /// <summary>
    /// Переставляет элемент с соседом и заново нумерует всех по порядку (10, 20, 30...).
    /// Так стрелки работают, даже если раньше у нескольких было одно и то же число.
    /// </summary>
    private static void Swap<T>(List<T> items, int index, int direction, Action<T, int> setOrder)
    {
        var target = index + Math.Sign(direction);
        if (index < 0 || target < 0 || target >= items.Count)
        {
            return;
        }
        (items[index], items[target]) = (items[target], items[index]);
        for (var i = 0; i < items.Count; i++)
        {
            setOrder(items[i], (i + 1) * 10);
        }
    }
}
