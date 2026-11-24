using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

/// <summary>Время на сетке записи и свободно ли оно.</summary>
public record WashSlot(TimeOnly Time, bool Free);

/// <summary>
/// Свободное время на мойке. Бокс занят записью от начала до конца услуги; на одно время может быть
/// столько записей, сколько боксов. Отменённые записи время не занимают.
/// </summary>
public class WashSlots(ShopDbContext db)
{
    /// <summary>Сейчас по Актау.</summary>
    public static DateTime Now() => Format.ToLocal(DateTime.UtcNow);

    /// <summary>Дни, на которые можно записаться: с сегодня на <see cref="CarWashes.DaysAhead"/> дней.</summary>
    public static IReadOnlyList<DateOnly> Days(DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        return Enumerable.Range(0, CarWashes.DaysAhead).Select(today.AddDays).ToList();
    }

    public async Task<IReadOnlyList<WashSlot>> ForDayAsync(CarWash wash, WashService service, DateOnly day, DateTime now, CancellationToken ct = default)
    {
        var from = day.ToDateTime(TimeOnly.MinValue);
        var to = from.AddDays(1);
        var taken = await db.WashBookings
            .Where(b => b.WashKey == wash.Key && b.StartsAt < to && b.StartsAt >= from.AddHours(-12)
                        && (b.Status == WashBookingStatus.New || b.Status == WashBookingStatus.Confirmed))
            .Select(b => new { b.StartsAt, b.Minutes })
            .ToListAsync(ct);

        var earliest = now.AddMinutes(CarWashes.MinLeadMinutes);
        var slots = new List<WashSlot>();
        var close = day.ToDateTime(wash.Closes);
        for (var start = day.ToDateTime(wash.Opens); start.AddMinutes(service.Minutes) <= close; start = start.AddMinutes(CarWashes.SlotStep))
        {
            var end = start.AddMinutes(service.Minutes);
            var overlapping = taken.Count(b => b.StartsAt < end && b.StartsAt.AddMinutes(b.Minutes) > start);
            slots.Add(new WashSlot(TimeOnly.FromDateTime(start), start >= earliest && overlapping < wash.Boxes));
        }
        return slots;
    }

    /// <summary>Можно ли записаться на это время: на сетке, в пределах дней записи и свободно.</summary>
    public async Task<bool> IsFreeAsync(CarWash wash, WashService service, DateTime startsAt, DateTime now, CancellationToken ct = default)
    {
        var day = DateOnly.FromDateTime(startsAt);
        if (!Days(now).Contains(day))
        {
            return false;
        }
        var time = TimeOnly.FromDateTime(startsAt);
        var slots = await ForDayAsync(wash, service, day, now, ct);
        return slots.Any(s => s.Time == time && s.Free);
    }
}
