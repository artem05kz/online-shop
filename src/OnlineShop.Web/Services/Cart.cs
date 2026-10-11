using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

public record CartLine(ProductVariant Variant, int Quantity)
{
    public decimal Price => Variant.Price ?? 0m;
    public decimal Subtotal => Price * Quantity;
}

public record CartContents(IReadOnlyList<CartLine> Lines)
{
    public int Count => Lines.Sum(l => l.Quantity);
    public decimal Total => Lines.Sum(l => l.Subtotal);
    public bool IsEmpty => Lines.Count == 0;
}

/// <summary>
/// Корзина: смотреть каталог и складывать товары можно без входа, вход нужен только при оформлении.
/// В базе лежат только пары «фасовка и количество», цены всегда берутся из каталога.
/// У гостя корзина привязана к метке в куке, после входа переходит к покупателю.
/// </summary>
public class Cart(IHttpContextAccessor httpContextAccessor, ShopDbContext db)
{
    public const string CookieName = "shop.cart";

    // Корзину гостя, к которой не возвращались два месяца, удаляет CartCleanup.
    public static readonly TimeSpan GuestLifetime = TimeSpan.FromDays(60);

    private HttpContext Context => httpContextAccessor.HttpContext!;

    private int? _count;

    /// <summary>Чья корзина: покупателя, если он вошёл, иначе гостя. null, если у гостя ещё нет корзины.</summary>
    private string? Owner(bool create = false)
    {
        if (Context.User.Identity?.IsAuthenticated == true
            && int.TryParse(Context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return UserKey(userId);
        }
        if (Guid.TryParse(Context.Request.Cookies[CookieName], out var guest))
        {
            return GuestKey(guest);
        }
        if (!create)
        {
            return null;
        }
        var id = Guid.NewGuid();
        WriteCookie(id);
        return GuestKey(id);
    }

    private static string UserKey(int userId) => $"u:{userId}";

    private static string GuestKey(Guid guest) => $"g:{guest:N}";

    /// <summary>Сколько штук товаров в корзине, для счётчика в шапке.</summary>
    public async Task<int> CountAsync()
    {
        if (_count is { } count)
        {
            return count;
        }
        var owner = Owner();
        return (_count = owner is null ? 0 : await db.CartItems.Where(i => i.OwnerKey == owner).SumAsync(i => i.Quantity)).Value;
    }

    /// <summary>
    /// Добавляет фасовку, но не больше, чем её можно заказать (остаток минус резерв).
    /// Возвращает, сколько штук этой фасовки теперь в корзине: 0, если товар закончился.
    /// </summary>
    public async Task<int> AddAsync(ProductVariant variant, int quantity)
    {
        var available = variant.AvailableQty;
        if (available <= 0)
        {
            return 0;
        }
        var owner = Owner(create: true)!;
        var item = await db.CartItems.FirstOrDefaultAsync(i => i.OwnerKey == owner && i.VariantId == variant.Id);
        if (item is null)
        {
            db.CartItems.Add(item = new CartItem { OwnerKey = owner, VariantId = variant.Id, Quantity = 0 });
        }
        item.Quantity = Math.Min(item.Quantity + Math.Max(1, quantity), available);
        item.UpdatedAt = DateTime.UtcNow;
        await SaveAsync(owner);
        return item.Quantity;
    }

    /// <summary>
    /// Новое количество с цифрой 0 или меньше убирает позицию из корзины. available: сколько можно
    /// заказать, больше этого в корзину не попадёт.
    /// </summary>
    public async Task SetQuantityAsync(int variantId, int quantity, int? available = null)
    {
        if (available is { } limit && quantity > limit)
        {
            quantity = limit;
        }
        if (Owner() is not { } owner)
        {
            return;
        }
        var item = await db.CartItems.FirstOrDefaultAsync(i => i.OwnerKey == owner && i.VariantId == variantId);
        if (item is null)
        {
            return;
        }
        if (quantity <= 0)
        {
            db.CartItems.Remove(item);
        }
        else
        {
            item.Quantity = quantity;
            item.UpdatedAt = DateTime.UtcNow;
        }
        await SaveAsync(owner);
    }

    public Task RemoveAsync(int variantId) => SetQuantityAsync(variantId, 0);

    public async Task ClearAsync()
    {
        if (Owner() is { } owner)
        {
            await db.CartItems.Where(i => i.OwnerKey == owner).ExecuteDeleteAsync();
        }
        _count = 0;
    }

    /// <summary>После входа: товары из корзины гостя добавляются в корзину покупателя, кука удаляется.</summary>
    public async Task MergeGuestAsync(int userId)
    {
        if (!Guid.TryParse(Context.Request.Cookies[CookieName], out var guest))
        {
            return;
        }
        var guestKey = GuestKey(guest);
        var userKey = UserKey(userId);
        var guestItems = await db.CartItems.Where(i => i.OwnerKey == guestKey).ToListAsync();
        var userItems = await db.CartItems.Where(i => i.OwnerKey == userKey).ToDictionaryAsync(i => i.VariantId);
        foreach (var item in guestItems)
        {
            if (userItems.TryGetValue(item.VariantId, out var existing))
            {
                existing.Quantity += item.Quantity;
                existing.UpdatedAt = DateTime.UtcNow;
                db.CartItems.Remove(item);
            }
            else
            {
                item.OwnerKey = userKey;
                item.UpdatedAt = DateTime.UtcNow;
            }
        }
        await db.SaveChangesAsync();
        ForgetGuest();
    }

    /// <summary>Убрать куку гостя: при выходе следующий человек за этим компьютером начнёт с пустой корзины.</summary>
    public void ForgetGuest()
    {
        Context.Response.Cookies.Delete(CookieName);
        _count = null;
    }

    /// <summary>
    /// Позиции корзины с актуальными ценами в порядке добавления. Если фасовку с тех пор скрыли
    /// или убрали ей цену, она тихо выпадает из корзины: купить её всё равно уже нельзя.
    /// </summary>
    public async Task<CartContents> LoadAsync()
    {
        if (Owner() is not { } owner)
        {
            return new CartContents([]);
        }
        var items = await db.CartItems
            .Where(i => i.OwnerKey == owner)
            .Include(i => i.Variant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .OrderBy(i => i.Id)
            .ToListAsync();

        var gone = items.Where(i => !i.Variant.IsActive || i.Variant.Price is null).ToList();
        if (gone.Count > 0)
        {
            db.CartItems.RemoveRange(gone);
            await SaveAsync(owner);
        }
        return new CartContents(items.Except(gone).Select(i => new CartLine(i.Variant, i.Quantity)).ToList());
    }

    private async Task SaveAsync(string owner)
    {
        await db.SaveChangesAsync();
        _count = null;
        if (owner.StartsWith("g:"))
        {
            // Продлеваем куку при каждом изменении: корзина живёт, пока ею пользуются.
            WriteCookie(Guid.Parse(owner[2..]));
        }
    }

    private void WriteCookie(Guid id) =>
        Context.Response.Cookies.Append(CookieName, id.ToString("N"), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            // Без неё не работает корзина: кука нужна сайту для работы, а не для слежки.
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.Add(GuestLifetime),
        });
}
