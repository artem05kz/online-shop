using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Live;

/// <summary>
/// Автообновление панели (24.11): страница панели раз в 30 секунд спрашивает здесь, что нового.
/// Ответ маленький (JSON): числа для меню, последний заказ и последняя запись на мойку.
/// По «отпечатку» Stamp страница понимает, что что-то поменялось, и обновляет числа, показывает плашку
/// «Новый заказ» и, где можно, перезагружает список. Только для менеджеров — как вся папка /admin.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public async Task<IActionResult> OnGetAsync()
    {
        var washDay = DateOnly.FromDateTime(WashSlots.Now()).ToDateTime(TimeOnly.MinValue);
        var newOrders = await db.Orders.CountAsync(o => o.Status == OrderStatus.New);
        var washWaiting = await db.WashBookings.CountAsync(b => b.Status == WashBookingStatus.New && b.StartsAt >= washDay);
        var pendingReviews = await db.Reviews.CountAsync(r => r.Status == ReviewStatus.Pending);
        // Сумму считаем в памяти: SQLite не умеет складывать decimal в запросе.
        var order = await db.Orders.OrderByDescending(o => o.Id).Include(o => o.Items).AsNoTracking().FirstOrDefaultAsync();
        var booking = await db.WashBookings.OrderByDescending(b => b.Id)
            .Select(b => new { b.Id, b.Name, b.ServiceName, b.StartsAt })
            .FirstOrDefaultAsync();
        // Сумма изменений: число заказов и записей в каждом статусе. Меняется и когда статус поменяли в другой вкладке.
        var orderStates = await db.Orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).OrderBy(x => x.Key).Select(x => x.Count).ToListAsync();
        var washStates = await db.WashBookings.Where(b => b.StartsAt >= washDay).GroupBy(b => b.Status).Select(g => new { g.Key, Count = g.Count() }).OrderBy(x => x.Key).Select(x => x.Count).ToListAsync();

        // Что ещё ждёт ответа (25.11): плашка «Новый заказ / Новая запись» сама исчезает, когда заказ или запись
        // подтвердили — здесь или в другой вкладке, в Telegram.
        var waitingOrderIds = await db.Orders.Where(o => o.Status == OrderStatus.New).OrderByDescending(o => o.Id).Select(o => o.Id).Take(200).ToListAsync();
        var waitingBookingIds = await db.WashBookings.Where(b => b.Status == WashBookingStatus.New).OrderByDescending(b => b.Id).Select(b => b.Id).Take(200).ToListAsync();

        Response.Headers.CacheControl = "no-store";
        return new JsonResult(new
        {
            newOrders,
            washWaiting,
            pendingReviews,
            waitingOrderIds,
            waitingBookingIds,
            lastOrder = order is null ? null : new
            {
                id = order.Id,
                name = string.IsNullOrWhiteSpace(order.ContactName) ? "Без имени" : order.ContactName,
                text = $"{Format.Money(order.Total)} · {(order.IsPickup ? "самовывоз" : "доставка")}",
            },
            lastBooking = booking is null ? null : new
            {
                id = booking.Id,
                name = booking.Name,
                text = $"{booking.StartsAt:dd.MM, HH:mm} · {booking.ServiceName}",
                date = booking.StartsAt.ToString("yyyy-MM-dd"),
            },
            stamp = $"{order?.Id}-{booking?.Id}-{string.Join('.', orderStates)}-{string.Join('.', washStates)}-{pendingReviews}",
        });
    }
}
