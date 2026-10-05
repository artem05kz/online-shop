using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Accounts;

[Authorize]
public class AddressesModel(ShopDbContext db) : PageModel
{
    public IReadOnlyList<Address> Addresses { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var userId = User.ShopUserId();
        Addresses = await db.Addresses.Where(a => a.UserId == userId).Ordered().AsNoTracking().ToListAsync();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var userId = User.ShopUserId();
        // Ищем только среди адресов этого покупателя: чужой адрес по номеру не удалить.
        var deleted = await db.Addresses.Where(a => a.Id == id && a.UserId == userId).ExecuteDeleteAsync();
        TempData.Flash(deleted > 0 ? "Адрес удалён." : "Адрес не найден.", deleted > 0 ? FlashLevel.Success : FlashLevel.Error);
        return RedirectToPage();
    }
}
