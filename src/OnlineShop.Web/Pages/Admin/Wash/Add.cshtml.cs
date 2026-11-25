using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Wash;

/// <summary>
/// «Новая запись» (CRM, 24.11): администратор записывает клиента, который позвонил, написал в WhatsApp
/// или приехал сам. Запись сразу подтверждена — с клиентом уже договорились. Место можно выбрать
/// или оставить «любое свободное». День, время и место приходят из календаря (нажатие на свободное время).
/// </summary>
public class AddModel(ShopDbContext db, WashSlots slots) : PageModel
{
    [BindProperty] public WashInput Input { get; set; } = new();

    public CarWash Wash => CarWashes.All[0];

    public void OnGet(string? date, string? time, int box = 0, string? name = null, string? phone = null, string? car = null)
    {
        var now = WashSlots.Now();
        Input.Date = DateOnly.TryParseExact(date ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        Input.Time = time ?? "";
        Input.Box = box;
        Input.Service = CarWashes.Services[0].Key;
        Input.Price = CarWashes.Services[0].PriceFrom;
        // «Записать снова» из карточки клиента (CRM, шаг 2): имя, телефон и машина уже заполнены.
        Input.Name = name;
        Input.Phone = PhoneNumbers.Normalize(phone) is { Length: > 0 } digits ? PhoneNumbers.Display(digits) : phone;
        Input.Car = car;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var checkedInput = await Input.ValidateAsync(ModelState, slots, Wash);
        if (checkedInput is not { } ok || !ModelState.IsValid)
        {
            return Page();
        }
        var booking = new WashBooking
        {
            Status = WashBookingStatus.Confirmed,
            ConfirmedBy = User.Identity?.Name ?? "",
            CreatedAt = DateTime.UtcNow,
        };
        Input.Apply(booking, Wash, ok.Service, ok.Start, ok.Box, ok.Phone);
        db.WashBookings.Add(booking);
        await db.SaveChangesAsync();
        TempData.Flash($"Записали: {booking.Name}, {booking.StartsAt:dd.MM} в {booking.StartsAt:HH:mm}, место {booking.Box}.");
        return Redirect($"/admin/wash?date={booking.StartsAt:yyyy-MM-dd}#b-{booking.Id}");
    }
}
