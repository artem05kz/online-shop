using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

public record LayoutData(IReadOnlyList<Brand> NavBrands, SiteContact? Contact, int CartCount, int FavoritesCount);

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

        // Считаем только то, что сейчас продаётся: снятые товары на странице избранного не показываются.
        var favoriteIds = await favorites.IdsAsync();
        var favoritesCount = favoriteIds.Count == 0 ? 0
            : await db.Products.CountAsync(p => favoriteIds.Contains(p.Id) && p.IsActive);

        return _data = new LayoutData(brands, contact, await cart.CountAsync(), favoritesCount);
    }
}
