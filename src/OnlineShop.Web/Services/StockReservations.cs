using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

/// <summary>
/// Резерв остатков. Остаток (StockQty) приходит из 1С и перезаписывается при каждой загрузке,
/// поэтому заказанное с сайта из него не вычитаем: следующая загрузка вернула бы старую цифру,
/// пока заказ ещё не проведён в 1С, и товар продали бы второй раз. Вместо этого у фасовки хранится
/// резерв: сколько штук лежит в незакрытых заказах. На сайте доступно «остаток минус резерв».
/// Выполненный заказ резерв не держит: к этому моменту продажа уже должна быть проведена в 1С.
/// </summary>
public class StockReservations(ShopDbContext db)
{
    /// <summary>Статусы, в которых товар уже обещан покупателю, но ещё не списан в 1С.</summary>
    public static readonly IReadOnlyList<OrderStatus> Holding = [OrderStatus.New, OrderStatus.Confirmed, OrderStatus.Delivering];

    /// <summary>Пересчитать резерв у фасовок из этих заказов: после оформления, смены статуса или правки заказа.</summary>
    public async Task RecalculateForOrdersAsync(IEnumerable<int> orderIds)
    {
        var ids = orderIds.ToList();
        var variantIds = await db.OrderItems.Where(i => ids.Contains(i.OrderId)).Select(i => i.VariantId).Distinct().ToListAsync();
        await RecalculateAsync(variantIds);
    }

    public async Task RecalculateAsync(IReadOnlyCollection<int> variantIds)
    {
        if (variantIds.Count == 0)
        {
            return;
        }
        var reserved = await db.OrderItems
            .Where(i => variantIds.Contains(i.VariantId) && Holding.Contains(i.Order.Status))
            .GroupBy(i => i.VariantId)
            .Select(g => new { VariantId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .ToDictionaryAsync(x => x.VariantId, x => x.Quantity);
        var variants = await db.ProductVariants.Where(v => variantIds.Contains(v.Id)).ToListAsync();
        foreach (var variant in variants)
        {
            variant.ReservedQty = reserved.GetValueOrDefault(variant.Id);
        }
        await db.SaveChangesAsync();
    }

    /// <summary>Пересчитать резерв у всех фасовок: после переноса базы или на всякий случай из консоли.</summary>
    public async Task RecalculateAllAsync() =>
        await RecalculateAsync(await db.ProductVariants.Select(v => v.Id).ToListAsync());
}
