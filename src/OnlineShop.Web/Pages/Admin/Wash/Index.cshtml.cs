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
/// Записи на мойку по дням. Страница рассчитана и на телефон: у администратора мойки нет компьютера.
/// Подтвердить, отменить (время освободится) или отметить «Помыли».
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Date { get; set; }

    public DateOnly Day { get; private set; }
    public IReadOnlyList<DateOnly> Days { get; private set; } = [];
    public IReadOnlyList<WashBooking> Bookings { get; private set; } = [];

    /// <summary>Сколько новых (неподтверждённых) записей на каждый день — для отметки на кнопках дней.</summary>
    public IReadOnlyDictionary<DateOnly, int> NewByDay { get; private set; } = new Dictionary<DateOnly, int>();

    public async Task OnGetAsync()
    {
        var now = WashSlots.Now();
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
    }

    public async Task<IActionResult> OnPostStatusAsync(int id, WashBookingStatus status)
    {
        var booking = await db.WashBookings.FindAsync(id);
        if (booking is null)
        {
            return NotFound();
        }
        booking.Status = status;
        await db.SaveChangesAsync();
        TempData.Flash(status switch
        {
            WashBookingStatus.Confirmed => $"Запись {booking.Name} на {booking.StartsAt:HH:mm} подтверждена.",
            WashBookingStatus.Cancelled => $"Запись {booking.Name} отменена, время освободилось.",
            WashBookingStatus.Done => $"Отмечено: {booking.Name} — помыли.",
            _ => "Статус изменён.",
        });
        return RedirectToPage(new { date = DateOnly.FromDateTime(booking.StartsAt).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) });
    }
}
