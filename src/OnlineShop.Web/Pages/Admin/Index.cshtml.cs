using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin;

/// <summary>
/// Главная панели (редизайн 24.11): сводка «что происходит сегодня». Сверху цифры дня и недели
/// (выручка, заказы, записи на мойку) со сравнением; ниже то, что ждёт действия: новые заказы с кнопкой
/// «Позвонить», записи на мойку сегодня, товары, которые заканчиваются, отзывы на проверке.
/// В самом низу частые действия и цифры по каталогу. Дни считаются по времени Актау, отменённые заказы
/// в выручку не входят (как в «Отчётах»).
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    /// <summary>Сколько штук считается «заканчивается».</summary>
    public const int LowStock = 3;

    public record DayBar(DateOnly Day, decimal Revenue, int Orders);
    public record NewOrder(int Id, string Name, string Phone, decimal Total, int Quantity, bool IsPickup, DateTime CreatedAt);
    public record LowItem(int ProductId, string Name, string Pack, string Article, int Left);

    public DateTime Now { get; private set; }

    public decimal TodayRevenue { get; private set; }
    public int TodayOrders { get; private set; }
    public decimal YesterdayRevenue { get; private set; }
    public decimal WeekRevenue { get; private set; }
    public int WeekOrders { get; private set; }
    public decimal PrevWeekRevenue { get; private set; }
    public List<DayBar> Week { get; private set; } = [];

    public int NewOrders { get; private set; }
    public List<NewOrder> NewOrderList { get; private set; } = [];
    public int InWork { get; private set; }

    public List<WashBooking> WashToday { get; private set; } = [];
    public int WashWaiting { get; private set; }

    public List<LowItem> Low { get; private set; } = [];
    public int PendingReviews { get; private set; }

    public int Products { get; private set; }
    public int NoPhoto { get; private set; }
    public int NoPrice { get; private set; }
    public int NoStock { get; private set; }

    /// <summary>Изменение в процентах к прошлому периоду; null — сравнивать не с чем.</summary>
    public static int? Change(decimal now, decimal before) =>
        before == 0 ? null : (int)Math.Round((now - before) / before * 100m);

    public async Task OnGetAsync()
    {
        Now = WashSlots.Now();
        var today = DateOnly.FromDateTime(Now);
        var from = Format.FromLocal(today.AddDays(-13).ToDateTime(TimeOnly.MinValue));

        // Заказы за две недели: этого хватает на «сегодня», «вчера», «7 дней» и «7 дней до этого».
        var recent = (await db.Orders.Where(o => o.CreatedAt >= from && o.Status != OrderStatus.Canceled)
                .Include(o => o.Items).AsNoTracking().ToListAsync())
            .Select(o => (Day: DateOnly.FromDateTime(Format.ToLocal(o.CreatedAt)), o.Total))
            .ToList();
        decimal Sum(DateOnly a, DateOnly b) => recent.Where(r => r.Day >= a && r.Day <= b).Sum(r => r.Total);
        int Count(DateOnly a, DateOnly b) => recent.Count(r => r.Day >= a && r.Day <= b);

        TodayRevenue = Sum(today, today);
        TodayOrders = Count(today, today);
        YesterdayRevenue = Sum(today.AddDays(-1), today.AddDays(-1));
        WeekRevenue = Sum(today.AddDays(-6), today);
        WeekOrders = Count(today.AddDays(-6), today);
        PrevWeekRevenue = Sum(today.AddDays(-13), today.AddDays(-7));
        Week = Enumerable.Range(0, 7).Select(i => today.AddDays(i - 6))
            .Select(d => new DayBar(d, Sum(d, d), Count(d, d))).ToList();

        NewOrders = await db.Orders.CountAsync(o => o.Status == OrderStatus.New);
        InWork = await db.Orders.CountAsync(o => o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Delivering);
        NewOrderList = (await db.Orders.Where(o => o.Status == OrderStatus.New)
                .OrderBy(o => o.CreatedAt).Take(6).Include(o => o.Items).AsNoTracking().ToListAsync())
            .Select(o => new NewOrder(o.Id, o.ContactName, o.ContactPhone, o.Total, o.TotalQuantity, o.IsPickup, o.CreatedAt))
            .ToList();

        var dayStart = today.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        WashToday = await db.WashBookings
            .Where(b => b.StartsAt >= dayStart && b.StartsAt < dayEnd
                && (b.Status == WashBookingStatus.New || b.Status == WashBookingStatus.Confirmed || b.Status == WashBookingStatus.Arrived))
            .OrderBy(b => b.StartsAt).AsNoTracking().ToListAsync();
        WashWaiting = await db.WashBookings.CountAsync(b => b.Status == WashBookingStatus.New && b.StartsAt >= dayStart);

        // Заканчивается: фасовки на сайте, которых осталось от 1 до 3 штук (с учётом резерва).
        Low = (await db.ProductVariants
                .Where(v => v.IsActive && v.Product.IsActive && v.StockQty - v.ReservedQty > 0 && v.StockQty - v.ReservedQty <= LowStock)
                .OrderBy(v => v.StockQty - v.ReservedQty).ThenBy(v => v.Product.Name)
                .Select(v => new { v.ProductId, v.Product.Name, v.PackLabel, v.Article, Left = v.StockQty - v.ReservedQty })
                .Take(8).ToListAsync())
            .Select(v => new LowItem(v.ProductId, v.Name, v.PackLabel, v.Article, v.Left)).ToList();

        PendingReviews = await db.Reviews.CountAsync(r => r.Status == ReviewStatus.Pending);
        Products = await db.Products.CountAsync();
        NoPhoto = await db.Products.CountAsync(p => !p.Images.Any());
        NoPrice = await db.Products.CountAsync(p => p.Variants.Any(v => v.IsActive && v.Price == null));
        NoStock = await db.Products.CountAsync(p => !p.Variants.Any(v => v.IsActive && v.StockQty > v.ReservedQty));
    }
}
