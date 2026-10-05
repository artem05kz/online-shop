using System.Text.Json;
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
/// Корзина в сессии: смотреть каталог и складывать товары можно без входа, вход нужен только
/// при оформлении. В сессии лежат только пары «артикул и количество», цены всегда берутся из базы.
/// </summary>
public class Cart(IHttpContextAccessor httpContextAccessor, ShopDbContext db)
{
    private const string SessionKey = "cart";

    private ISession Session => httpContextAccessor.HttpContext!.Session;

    /// <summary>Сколько штук товаров в корзине, для счётчика в шапке. В базу не ходит.</summary>
    public int Count => Read().Values.Sum();

    public void Add(int variantId, int quantity)
    {
        var items = Read();
        items[variantId] = items.GetValueOrDefault(variantId) + quantity;
        Write(items);
    }

    /// <summary>Новое количество с цифрой 0 или меньше убирает позицию из корзины.</summary>
    public void SetQuantity(int variantId, int quantity)
    {
        var items = Read();
        if (quantity <= 0)
        {
            items.Remove(variantId);
        }
        else if (items.ContainsKey(variantId))
        {
            items[variantId] = quantity;
        }
        Write(items);
    }

    public void Remove(int variantId)
    {
        var items = Read();
        items.Remove(variantId);
        Write(items);
    }

    public void Clear() => Write([]);

    /// <summary>
    /// Позиции корзины с актуальными ценами в порядке добавления. Если фасовку с тех пор скрыли
    /// или убрали ей цену, она тихо выпадает из корзины: купить её всё равно уже нельзя.
    /// </summary>
    public async Task<CartContents> LoadAsync()
    {
        var items = Read();
        if (items.Count == 0)
        {
            return new CartContents([]);
        }

        var ids = items.Keys.ToList();
        var variants = await db.ProductVariants
            .Where(v => ids.Contains(v.Id) && v.IsActive && v.Price != null)
            .Include(v => v.Product).ThenInclude(p => p.Images)
            .ToDictionaryAsync(v => v.Id);

        var lines = items
            .Where(pair => variants.ContainsKey(pair.Key))
            .Select(pair => new CartLine(variants[pair.Key], pair.Value))
            .ToList();

        if (lines.Count != items.Count)
        {
            Write(lines.ToDictionary(l => l.Variant.Id, l => l.Quantity));
        }
        return new CartContents(lines);
    }

    private Dictionary<int, int> Read()
    {
        var json = Session.GetString(SessionKey);
        return json is null ? [] : JsonSerializer.Deserialize<Dictionary<int, int>>(json) ?? [];
    }

    private void Write(Dictionary<int, int> items) =>
        Session.SetString(SessionKey, JsonSerializer.Serialize(items));
}
