using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

/// <summary>
/// Избранное. Отмечать товары можно и без входа, как и складывать в корзину: у гостя номера товаров
/// лежат в куке, а при входе переносятся в базу и дальше видны с любого устройства.
/// Кука, а не запись в базе: гостю не нужно заводить в базе строку на каждое сердечко.
/// </summary>
public class Favorites(IHttpContextAccessor httpContextAccessor, ShopDbContext db)
{
    public const string CookieName = "shop.favorites";

    // Кука ограничена 4 КБ. 200 номеров по 4-6 цифр в неё помещаются с запасом.
    private const int GuestLimit = 200;

    private HashSet<int>? _ids;

    private HttpContext Context => httpContextAccessor.HttpContext!;

    private int? UserId => Context.User.Identity?.IsAuthenticated == true
        && int.TryParse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    /// <summary>Номера товаров в избранном. Один запрос к базе на страницу, сколько бы карточек на ней ни было.</summary>
    public async Task<IReadOnlySet<int>> IdsAsync()
    {
        if (_ids is not null)
        {
            return _ids;
        }
        _ids = UserId is { } userId
            ? (await db.Favorites.Where(f => f.UserId == userId).Select(f => f.ProductId).ToListAsync()).ToHashSet()
            : ReadCookie();
        return _ids;
    }

    /// <summary>Добавляет или убирает товар. Возвращает true, если товар теперь в избранном.</summary>
    public async Task<bool> ToggleAsync(int productId)
    {
        var ids = (HashSet<int>)await IdsAsync();
        var added = !ids.Remove(productId);
        if (added)
        {
            ids.Add(productId);
        }

        if (UserId is { } userId)
        {
            if (added)
            {
                // Запоминаем наличие сразу: если товара нет, письмо придёт, как только он появится.
                var inStock = await db.Products.AnyAsync(p => p.Id == productId
                    && p.Variants.Any(v => v.IsActive && v.StockQty > v.ReservedQty));
                db.Favorites.Add(new Favorite { UserId = userId, ProductId = productId, CreatedAt = DateTime.UtcNow, WasInStock = inStock });
            }
            else
            {
                await db.Favorites.Where(f => f.UserId == userId && f.ProductId == productId).ExecuteDeleteAsync();
            }
            await db.SaveChangesAsync();
        }
        else
        {
            if (added && ids.Count > GuestLimit)
            {
                ids.Remove(productId);
                return false;
            }
            WriteCookie(ids);
        }
        return added;
    }

    /// <summary>После входа: отмеченное гостем переходит в избранное покупателя, кука удаляется.</summary>
    public async Task MergeGuestAsync(int userId)
    {
        var guest = ReadCookie();
        if (guest.Count == 0)
        {
            return;
        }
        var existing = await db.Favorites.Where(f => f.UserId == userId).Select(f => f.ProductId).ToListAsync();
        var products = await db.Products.Where(p => guest.Contains(p.Id)).Select(p => p.Id).ToListAsync();
        var now = DateTime.UtcNow;
        db.Favorites.AddRange(products.Except(existing)
            .Select(id => new Favorite { UserId = userId, ProductId = id, CreatedAt = now }));
        await db.SaveChangesAsync();
        Context.Response.Cookies.Delete(CookieName);
        _ids = null;
    }

    private HashSet<int> ReadCookie()
    {
        var raw = Context.Request.Cookies[CookieName] ?? "";
        return raw.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, out var id) ? id : 0)
            .Where(id => id > 0)
            .Take(GuestLimit)
            .ToHashSet();
    }

    private void WriteCookie(HashSet<int> ids)
    {
        if (ids.Count == 0)
        {
            Context.Response.Cookies.Delete(CookieName);
            return;
        }
        Context.Response.Cookies.Append(CookieName, string.Join('.', ids), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            // Как корзина: нужна сайту для работы, а не для слежки.
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddYears(1),
        });
    }
}
