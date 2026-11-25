using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Wash;

/// <summary>
/// Записи на мойку (редизайн 24.11). Два вида: «День» — карточки записей выбранного дня с кнопками
/// «Подтвердить», «Помыли», «Отменить»; «Неделя» — календарь на все дни записи, где видно занятое время
/// по боксам. Сверху у каждого дня — сколько записей, сколько ждут подтверждения и насколько занята мойка.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Date { get; set; }
    [BindProperty(SupportsGet = true)] public string? View { get; set; }

    public bool IsWeek => View == "week";

    /// <summary>Сводка дня: записей (без отменённых), ждут подтверждения, занятость мойки в процентах.</summary>
    public record DayInfo(int Count, int Waiting, int LoadPercent);

    /// <summary>Запись в календаре недели: дорожка (бокс) и сколько дорожек занято в этот день.</summary>
    public record Block(WashBooking Booking, int Lane, int Lanes);

    public IReadOnlyDictionary<DateOnly, DayInfo> Info { get; private set; } = new Dictionary<DateOnly, DayInfo>();
    public IReadOnlyDictionary<DateOnly, List<Block>> Week { get; private set; } = new Dictionary<DateOnly, List<Block>>();
    public CarWash Wash => CarWashes.All[0];

    /// <summary>Календарь дня по местам (CRM, 24.11): номер места → записи. Отменённые не показываются.</summary>
    public IReadOnlyDictionary<int, List<WashBooking>> ByBox { get; private set; } = new Dictionary<int, List<WashBooking>>();
    public DateTime Now { get; private set; }

    public DateOnly Day { get; private set; }
    public IReadOnlyList<DateOnly> Days { get; private set; } = [];
    public IReadOnlyList<WashBooking> Bookings { get; private set; } = [];

    /// <summary>Сколько новых (неподтверждённых) записей на каждый день — для отметки на кнопках дней.</summary>
    public IReadOnlyDictionary<DateOnly, int> NewByDay { get; private set; } = new Dictionary<DateOnly, int>();

    public async Task OnGetAsync()
    {
        var now = WashSlots.Now();
        Now = now;
        Days = WashSlots.Days(now);
        Day = DateOnly.TryParseExact(Date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : Days[0];
        var from = Day.ToDateTime(TimeOnly.MinValue);
        Bookings = await db.WashBookings.Where(b => b.StartsAt >= from && b.StartsAt < from.AddDays(1))
            .OrderBy(b => b.StartsAt).ThenBy(b => b.WashKey).AsNoTracking().ToListAsync();

        var first = Days[0].ToDateTime(TimeOnly.MinValue);
        var last = Days[^1].ToDateTime(TimeOnly.MinValue).AddDays(1);
        NewByDay = (await db.WashBookings.Where(b => b.Status == WashBookingStatus.New && b.StartsAt >= first && b.StartsAt < last)
                .Select(b => b.StartsAt).ToListAsync())
            .GroupBy(DateOnly.FromDateTime).ToDictionary(g => g.Key, g => g.Count());

        // Все записи дней записи (без отменённых) — для сводки по дням и календаря недели.
        var all = await db.WashBookings.Where(b => b.StartsAt >= first && b.StartsAt < last && b.Status != WashBookingStatus.Cancelled)
            .OrderBy(b => b.StartsAt).AsNoTracking().ToListAsync();
        var open = (Wash.Closes - Wash.Opens).TotalMinutes * Wash.Boxes;
        Info = Days.ToDictionary(day => day, day =>
        {
            var list = all.Where(b => DateOnly.FromDateTime(b.StartsAt) == day).ToList();
            var minutes = list.Sum(b => b.Minutes);
            return new DayInfo(list.Count, list.Count(b => b.Status == WashBookingStatus.New),
                open <= 0 ? 0 : (int)Math.Min(100, Math.Round(minutes / open * 100)));
        });
        Week = Days.ToDictionary(day => day, day => Lanes(all.Where(b => DateOnly.FromDateTime(b.StartsAt) == day).ToList()));

        // Записи без места (старые) ставим в свободную колонку только для показа — в базе не меняем.
        var columns = Enumerable.Range(1, Wash.Boxes).ToDictionary(n => n, _ => new List<WashBooking>());
        foreach (var b in Bookings.Where(b => b.Status != WashBookingStatus.Cancelled).OrderBy(b => b.Box == 0).ThenBy(b => b.StartsAt))
        {
            var box = b.Box;
            if (box < 1 || box > Wash.Boxes)
            {
                var busy = columns.Where(c => c.Value.Any(o => o.StartsAt < b.EndsAt && o.EndsAt > b.StartsAt)).Select(c => c.Key).ToList();
                box = WashSlots.PickBox(busy, Wash.Boxes);
                if (box == 0)
                {
                    box = 1;
                }
            }
            columns[box].Add(b);
        }
        ByBox = columns;
    }

    /// <summary>Раскладывает записи дня по дорожкам, чтобы пересекающиеся по времени стояли рядом, а не друг на друге.</summary>
    private static List<Block> Lanes(List<WashBooking> day)
    {
        var ends = new List<DateTime>();
        var placed = new List<(WashBooking B, int Lane)>();
        foreach (var b in day.OrderBy(b => b.StartsAt))
        {
            var lane = ends.FindIndex(e => e <= b.StartsAt);
            if (lane < 0)
            {
                ends.Add(b.EndsAt);
                lane = ends.Count - 1;
            }
            else
            {
                ends[lane] = b.EndsAt;
            }
            placed.Add((b, lane));
        }
        var lanes = Math.Max(1, ends.Count);
        return placed.Select(p => new Block(p.B, p.Lane, lanes)).ToList();
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, WashBookingStatus status)
    {
        var booking = await db.WashBookings.FindAsync(id);
        if (booking is null)
        {
            return NotFound();
        }
        booking.Status = status;
        if (status == WashBookingStatus.Confirmed && booking.ConfirmedBy.Length == 0)
        {
            booking.ConfirmedBy = User.Identity?.Name ?? "";
        }
        await db.SaveChangesAsync();
        TempData.Flash(status switch
        {
            WashBookingStatus.Confirmed => $"Запись {booking.Name} на {booking.StartsAt:HH:mm} подтверждена.",
            WashBookingStatus.Cancelled => $"Запись {booking.Name} отменена, время освободилось.",
            WashBookingStatus.Done => $"Отмечено: {booking.Name} — помыли.",
            WashBookingStatus.Arrived => $"{booking.Name} приехал — моем.",
            WashBookingStatus.NoShow => $"Отмечено: {booking.Name} не приехал.",
            _ => "Статус изменён.",
        });
        return RedirectToPage(new { date = DateOnly.FromDateTime(booking.StartsAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), view = View });
    }
}
