using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Wash;

/// <summary>
/// Карточка записи на мойку (CRM, 24.11). Сверху статус и кнопки следующего шага
/// (Подтвердить → Приехал → Помыли; «Не приехал», «Отменить»). Ниже клиент с телефоном и его прошлые визиты
/// по этому номеру, и форма: перенести, сменить место или услугу, цена и оплата, заметка.
/// </summary>
public class EditModel(ShopDbContext db, WashSlots slots) : PageModel
{
    [BindProperty] public WashInput Input { get; set; } = new();

    public WashBooking Booking { get; private set; } = null!;
    public CarWash Wash => CarWashes.All[0];

    /// <summary>Другие записи этого клиента (по телефону): сколько раз был, сколько не приехал.</summary>
    public List<WashBooking> History { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        Input = WashInput.From(Booking);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id))
        {
            return NotFound();
        }
        var checkedInput = await Input.ValidateAsync(ModelState, slots, Wash, exceptId: id);
        if (checkedInput is not { } ok || !ModelState.IsValid)
        {
            return Page();
        }
        var booking = await db.WashBookings.FindAsync(id);
        Input.Apply(booking!, Wash, ok.Service, ok.Start, ok.Box, ok.Phone);
        await db.SaveChangesAsync();
        TempData.Flash("Запись сохранена.");
        return RedirectToPage(new { id });
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
        TempData.Flash($"Статус записи: «{status.Text()}».");
        return RedirectToPage(new { id });
    }

    private async Task<bool> LoadAsync(int id)
    {
        var booking = await db.WashBookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id);
        if (booking is null)
        {
            return false;
        }
        Booking = booking;
        History = await db.WashBookings.Where(b => b.Phone == booking.Phone && b.Id != id)
            .OrderByDescending(b => b.StartsAt).AsNoTracking().ToListAsync();
        return true;
    }
}
