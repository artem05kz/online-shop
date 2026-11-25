using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Wash;

/// <summary>
/// Карточка клиента мойки (CRM, шаг 2, 25.11): все записи по этому телефону, сколько помыли и оплатил,
/// сколько раз не приехал, машины и заметки администратора. Кнопка «Записать снова» открывает новую
/// запись с уже заполненными именем, телефоном и машиной.
/// </summary>
public class ClientModel(ShopDbContext db) : PageModel
{
    public WashClient Client { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(string phone)
    {
        var digits = new string((phone ?? "").Where(char.IsDigit).ToArray());
        var bookings = await db.WashBookings.Where(b => b.Phone == digits).AsNoTracking().ToListAsync();
        if (bookings.Count == 0)
        {
            return NotFound();
        }
        Client = WashClient.From(bookings, WashSlots.Now())[0];
        return Page();
    }
}
