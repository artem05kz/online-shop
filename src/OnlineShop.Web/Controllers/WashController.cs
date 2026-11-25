using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using OnlineShop.Web.Services.Telegram;
using OnlineShop.Web.ViewModels;

namespace OnlineShop.Web.Controllers;

/// <summary>
/// Запись на автомойку без регистрации: мойка, услуга, день и время, имя и телефон. Запись сразу занимает
/// время (второй человек его уже не выберет) и уходит администратору в Telegram. Клиент видит свою запись
/// по ссылке и может её отменить.
/// </summary>
public class WashController(ShopDbContext db, WashSlots slots, OrderAlerts alerts) : Controller
{
    public const string PageUrl = "/wash";
    private const int ProductsShown = 5;

    [HttpGet(PageUrl)]
    public async Task<IActionResult> Index(string? wash, string? service, string? date)
    {
        var now = WashSlots.Now();
        var form = new WashForm
        {
            Wash = (CarWashes.Find(wash) ?? CarWashes.All[0]).Key,
            Service = (CarWashes.FindService(service) ?? CarWashes.Services[0]).Key,
        };
        // День: из адреса или первый, где есть свободное время.
        var chosen = CarWashes.Find(form.Wash)!;
        var svc = CarWashes.FindService(form.Service)!;
        var days = WashSlots.Days(now);
        DateOnly day;
        if (!TryDay(date, out day) || !days.Contains(day))
        {
            day = days[0];
            foreach (var d in days)
            {
                if ((await slots.ForDayAsync(chosen, svc, d, now)).Any(s => s.Free))
                {
                    day = d;
                    break;
                }
            }
        }
        form.Date = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return View(await Page(form, now));
    }

    /// <summary>Свободное время на день: для страницы, когда меняют мойку, услугу или день.</summary>
    [HttpGet(PageUrl + "/slots")]
    public async Task<IActionResult> Slots(string? wash, string? service, string? date)
    {
        if (CarWashes.Find(wash) is not { } w || CarWashes.FindService(service) is not { } s || !TryDay(date, out var day))
        {
            return BadRequest();
        }
        var list = await slots.ForDayAsync(w, s, day, WashSlots.Now());
        return Json(list.Select(x => new { time = x.Time.ToString("HH:mm", CultureInfo.InvariantCulture), free = x.Free }));
    }

    [HttpPost(PageUrl)]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book([Bind(Prefix = "Form")] WashForm form)
    {
        var now = WashSlots.Now();
        var wash = CarWashes.Find(form.Wash);
        var service = CarWashes.FindService(form.Service);
        if (wash is null) ModelState.AddModelError("Form.Wash", "Выберите мойку.");
        if (service is null) ModelState.AddModelError("Form.Service", "Выберите услугу.");
        // Пустое поле приходит как null (а не ""): ошибку «обязательное поле» уже показывает [Required].
        var phone = PhoneNumbers.Normalize(form.Phone);
        if (!string.IsNullOrWhiteSpace(form.Phone) && phone.Length == 0)
        {
            ModelState.AddModelError("Form.Phone", "Похоже, в номере ошибка. Например: +7 701 123-45-67.");
        }
        if (!form.AcceptPrivacy)
        {
            ModelState.AddModelError("Form.AcceptPrivacy", "Нужно согласие на обработку данных, чтобы администратор смог вам позвонить.");
        }
        DateTime startsAt = default;
        if (!TryDay(form.Date, out var day) || !TimeOnly.TryParseExact(form.Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            ModelState.AddModelError("Form.Time", "Выберите время.");
        }
        else if (wash is not null && service is not null)
        {
            startsAt = day.ToDateTime(time);
            if (!await slots.IsFreeAsync(wash, service, startsAt, now))
            {
                ModelState.AddModelError("Form.Time", "Это время уже занято. Выберите другое.");
            }
        }
        if (!ModelState.IsValid)
        {
            return View("Index", await Page(form, now));
        }

        var booking = new WashBooking
        {
            WashKey = wash!.Key, WashName = wash.Name,
            ServiceKey = service!.Key, ServiceName = service.Name, Minutes = service.Minutes,
            StartsAt = startsAt,
            Name = (form.Name ?? "").Trim(), Phone = phone,
            Car = form.Car?.Trim() ?? "", Comment = form.Comment?.Trim() ?? "",
            CreatedAt = DateTime.UtcNow,
            UserId = User.Identity?.IsAuthenticated == true ? User.ShopUserId() : null,
            Source = WashSources.Site,
            Price = service.PriceFrom,
            // Место назначается сразу, администратор может поменять его в панели.
            Box = await slots.FreeBoxAsync(wash, startsAt, service.Minutes),
        };
        db.WashBookings.Add(booking);
        await db.SaveChangesAsync();
        alerts.EnqueueBooking(booking.Id, $"{Request.Scheme}://{Request.Host}");
        TempData["WashBooked"] = true;
        return Redirect(LinkPath(booking));
    }

    public const string MyUrl = "/accounts/wash";

    /// <summary>
    /// «Мои записи на мойку» в кабинете (24.11): записи, сделанные под этим аккаунтом. Сначала предстоящие
    /// (ближайшая первой), ниже прошедшие и отменённые — новые сверху. Записи гостей сюда не попадают.
    /// </summary>
    [Microsoft.AspNetCore.Authorization.Authorize]
    [HttpGet(MyUrl)]
    public async Task<IActionResult> My()
    {
        var userId = User.ShopUserId();
        var all = await db.WashBookings.Where(b => b.UserId == userId).AsNoTracking().ToListAsync();
        var now = WashSlots.Now();
        var upcoming = all.Where(b => b.Holds && b.EndsAt > now).OrderBy(b => b.StartsAt).ToList();
        var past = all.Except(upcoming).OrderByDescending(b => b.StartsAt).ToList();
        return View(new MyWashViewModel { Upcoming = upcoming, Past = past });
    }

    public static string LinkPath(WashBooking booking) => $"{PageUrl}/booking/{booking.AccessToken}";

    [HttpGet(PageUrl + "/booking/{token:length(32)}")]
    public async Task<IActionResult> Booked(string token)
    {
        var booking = await db.WashBookings.AsNoTracking().FirstOrDefaultAsync(b => b.AccessToken == token);
        if (booking is null)
        {
            return NotFound();
        }

        // Пока человек здесь — средства для машины из раздела «Для авто», что есть в наличии.
        var car = HomeSections.All.First(s => s.Key == "car");
        var products = (await db.Products.ForListing().AsNoTracking().ToListAsync())
            .Where(p => p.InStock && HomeSections.Contains(car, p.Category.Name))
            .OrderBy(p => p.Name)
            .Take(ProductsShown)
            .ToList();

        return View(new WashBookedViewModel
        {
            Booking = booking,
            Products = products,
            JustBooked = TempData["WashBooked"] is true,
            CanCancel = booking.Holds && booking.StartsAt > WashSlots.Now(),
        });
    }

    [HttpPost(PageUrl + "/booking/{token:length(32)}/cancel")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Cancel(string token)
    {
        var booking = await db.WashBookings.FirstOrDefaultAsync(b => b.AccessToken == token);
        if (booking is null)
        {
            return NotFound();
        }
        if (booking.Holds && booking.StartsAt > WashSlots.Now())
        {
            booking.Status = WashBookingStatus.Cancelled;
            await db.SaveChangesAsync();
            alerts.EnqueueBooking(booking.Id, $"{Request.Scheme}://{Request.Host}", cancelled: true);
            TempData.Flash("Запись отменена. Время освободилось для других.");
        }
        return Redirect(LinkPath(booking));
    }

    private async Task<WashPageViewModel> Page(WashForm form, DateTime now)
    {
        var wash = CarWashes.Find(form.Wash) ?? CarWashes.All[0];
        var service = CarWashes.FindService(form.Service) ?? CarWashes.Services[0];
        var days = WashSlots.Days(now);
        var day = TryDay(form.Date, out var d) && days.Contains(d) ? d : days[0];
        return new WashPageViewModel
        {
            Form = form,
            Days = days,
            Slots = await slots.ForDayAsync(wash, service, day, now),
        };
    }

    private static bool TryDay(string? value, out DateOnly day) =>
        DateOnly.TryParseExact(value ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out day);
}
