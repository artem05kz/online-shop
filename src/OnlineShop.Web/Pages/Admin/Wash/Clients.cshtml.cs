using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Wash;

/// <summary>
/// Клиент мойки (CRM, шаг 2, 25.11) — все записи с одним телефоном. Отдельной таблицы клиентов нет:
/// клиент собирается из записей, поэтому ничего не нужно заводить вручную, и старые записи тоже видны.
/// </summary>
public record WashClient(string Phone, string Name, string Car, IReadOnlyList<WashBooking> Bookings, DateTime Now)
{
    public int Visits => Bookings.Count(b => b.Status == WashBookingStatus.Done);
    public int NoShows => Bookings.Count(b => b.Status == WashBookingStatus.NoShow);
    public int Cancelled => Bookings.Count(b => b.Status == WashBookingStatus.Cancelled);
    public decimal Paid => Bookings.Where(b => b.IsPaid && b.Price is not null).Sum(b => b.Price!.Value);
    public decimal AverageCheck => Bookings.Count(b => b.IsPaid && b.Price is not null) is var n and > 0 ? Paid / n : 0;
    public DateTime FirstAt => Bookings.Min(b => b.StartsAt);
    public DateTime? LastVisit => Bookings.Where(b => b.Status == WashBookingStatus.Done).Select(b => (DateTime?)b.StartsAt).Max();
    public WashBooking? Next => Bookings.Where(b => b.Holds && b.EndsAt > Now).MinBy(b => b.StartsAt);
    /// <summary>Последнее, что было с клиентом: для сортировки списка.</summary>
    public DateTime LastAt => Bookings.Max(b => b.StartsAt);
    public IReadOnlyList<string> Cars => Bookings.Where(b => b.Car.Length > 0).OrderByDescending(b => b.StartsAt)
        .Select(b => b.Car).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    public IReadOnlyList<string> Notes => Bookings.Where(b => b.ManagerNote.Length > 0).OrderByDescending(b => b.StartsAt)
        .Select(b => b.ManagerNote).Distinct().ToList();

    /// <summary>Постоянный — помыли 3 раза и больше.</summary>
    public bool IsRegular => Visits >= RegularFrom;
    public const int RegularFrom = 3;

    public static List<WashClient> From(IEnumerable<WashBooking> bookings, DateTime now) =>
        bookings.GroupBy(b => b.Phone).Select(g =>
        {
            var list = g.OrderByDescending(b => b.StartsAt).ToList();
            var car = list.FirstOrDefault(b => b.Car.Length > 0)?.Car ?? "";
            return new WashClient(g.Key, list[0].Name, car, list, now);
        }).ToList();
}

/// <summary>
/// Клиенты мойки (CRM, шаг 2, 25.11): поиск по имени, телефону и машине; вкладки «Все», «Постоянные»,
/// «Новые за месяц», «Не приезжали». Строка — клиент: сколько раз помыли, сколько не приехал,
/// сколько оплатил, когда был последний раз и ближайшая запись.
/// </summary>
public class ClientsModel(ShopDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string Show { get; set; } = "all";

    public IReadOnlyList<WashClient> Rows { get; private set; } = [];
    public int AllCount { get; private set; }
    public int RegularCount { get; private set; }
    public int NewCount { get; private set; }
    public int NoShowCount { get; private set; }

    public async Task OnGetAsync()
    {
        var now = WashSlots.Now();
        var all = WashClient.From(await db.WashBookings.AsNoTracking().ToListAsync(), now);
        var monthAgo = now.AddDays(-30);
        bool IsNew(WashClient c) => c.FirstAt >= monthAgo;

        AllCount = all.Count;
        RegularCount = all.Count(c => c.IsRegular);
        NewCount = all.Count(IsNew);
        NoShowCount = all.Count(c => c.NoShows > 0);

        IEnumerable<WashClient> rows = Show switch
        {
            "regular" => all.Where(c => c.IsRegular),
            "new" => all.Where(IsNew),
            "noshow" => all.Where(c => c.NoShows > 0),
            _ => all,
        };
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var q = Q.Trim();
            var digits = new string(q.Where(char.IsDigit).ToArray());
            rows = rows.Where(c =>
                c.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                || c.Bookings.Any(b => b.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || b.Car.Contains(q, StringComparison.OrdinalIgnoreCase))
                || (digits.Length >= 3 && c.Phone.Contains(digits)));
        }
        Rows = rows.OrderByDescending(c => c.Next is not null).ThenByDescending(c => c.LastAt).ToList();
    }
}
