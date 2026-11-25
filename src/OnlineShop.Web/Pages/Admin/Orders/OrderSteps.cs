using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using OnlineShop.Web.Services.Email;

namespace OnlineShop.Web.Pages.Admin.Orders;

/// <summary>
/// «Следующий шаг» заказа одной кнопкой (редизайн админки 24.11): Новый → Подтверждён → Доставляется
/// (у самовывоза — «Готов к выдаче») → Выполнен. Отменённый и выполненный дальше не двигаются.
/// После смены статуса пересчитывается резерв и покупателю уходит письмо — как при ручной смене.
/// </summary>
public static class OrderSteps
{
    public static OrderStatus? Next(OrderStatus status) => status switch
    {
        OrderStatus.New => OrderStatus.Confirmed,
        OrderStatus.Confirmed => OrderStatus.Delivering,
        OrderStatus.Delivering => OrderStatus.Completed,
        _ => null,
    };

    /// <summary>Подпись кнопки: что сделает менеджер.</summary>
    public static string? Button(OrderStatus status, bool isPickup) => status switch
    {
        OrderStatus.New => "Подтвердить",
        OrderStatus.Confirmed => isPickup ? "Собран, готов к выдаче" : "Передать в доставку",
        OrderStatus.Delivering => isPickup ? "Выдан покупателю" : "Доставлен",
        _ => null,
    };

    /// <summary>Подпись статуса для менеджера (у самовывоза «Доставляется» = «Готов к выдаче»).</summary>
    public static string Text(OrderStatus status, bool isPickup) =>
        isPickup && status == OrderStatus.Delivering ? "Готов к выдаче" : status.Label();

    /// <summary>Сдвинуть заказ на шаг. Возвращает новый статус или null, если двигать некуда.</summary>
    public static async Task<OrderStatus?> AdvanceAsync(ShopDbContext db, StockReservations reservations, AccountMail mail,
        int id, string siteUrl)
    {
        var order = await db.Orders.FindAsync(id);
        if (order is null || Next(order.Status) is not { } next)
        {
            return null;
        }
        order.Status = next;
        await db.SaveChangesAsync();
        await reservations.RecalculateForOrdersAsync([id]);
        await mail.NotifyStatusChangedAsync([id], siteUrl);
        return next;
    }
}
