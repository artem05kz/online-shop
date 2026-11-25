using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

public record LayoutData(IReadOnlyList<Brand> NavBrands, SiteContact? Contact, int CartCount, int FavoritesCount,
    IReadOnlyList<Store> Stores, IReadOnlyDictionary<int, int> CategoryCounts)
{
    /// <summary>Сколько товаров в продаже в категории верхнего уровня (вместе с подкатегориями).</summary>
    public int CountIn(Category category) => CategoryCounts.GetValueOrDefault(category.Id);
}

/// <summary>
/// Данные, нужные на каждой странице витрины: каталог в шапке, контакты в шапке и подвале,
/// счётчики корзины и избранного. Аналог context processor site_nav из Django, загружается один раз за запрос.
/// </summary>
public class StorefrontLayout(ShopDbContext db, Cart cart, Favorites favorites)
{
    private LayoutData? _data;

    public async Task<LayoutData> GetAsync()
    {
        if (_data is not null)
        {
            return _data;
        }

        var brands = await db.Brands
            .Where(b => b.IsActive)
            .Ordered()
            .Include(b => b.Categories
                .Where(c => c.IsActive && c.ParentId == null)
                .OrderBy(c => c.SortOrder).ThenBy(c => c.Name))
            .AsNoTracking()
            .ToListAsync();

        var contact = await db.SiteContacts
            .Where(c => c.IsActive)
            .OrderBy(c => c.Id)
            .AsNoTracking()
            .FirstOrDefaultAsync();

        // Точки для подвала: адрес и часы каждого магазина (у филиалов разные часы работы). Склады покупателю не нужны.
        var stores = await db.Stores.Where(st => st.IsActive && st.Kind != StoreKind.Warehouse).OrderBy(st => st.SortOrder).ThenBy(st => st.Id)
            .Take(3).AsNoTracking().ToListAsync();
        // Считаем только то, что сейчас продаётся: снятые товары на странице избранного не показываются.
        var favoriteIds = await favorites.IdsAsync();
        var favoritesCount = favoriteIds.Count == 0 ? 0
            : await db.Products.CountAsync(p => favoriteIds.Contains(p.Id) && p.IsActive);

        // Число товаров у категорий в каталоге сбоку. Товар подкатегории считается и у её родителя.
        var counts = (await db.Products
                .Where(p => p.IsActive && p.Category.IsActive)
                .Select(p => new { p.CategoryId, p.Category.ParentId })
                .ToListAsync())
            .GroupBy(p => p.ParentId ?? p.CategoryId)
            .ToDictionary(g => g.Key, g => g.Count());

        return _data = new LayoutData(brands, contact, await cart.CountAsync(), favoritesCount, stores, counts);
    }
}
