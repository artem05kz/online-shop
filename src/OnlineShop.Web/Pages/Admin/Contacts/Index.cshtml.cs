using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Contacts;

/// <summary>
/// Контакты сайта (25.11.2026): сверху набор, который сейчас на сайте, — как он выглядит в шапке и внизу страницы,
/// плитки с каждым контактом и кнопкой «Проверить». Ниже запасные наборы: «Сделать основным» включает набор
/// и выключает остальные, чтобы на сайте всегда был ровно один.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public SiteContact? Live { get; private set; }
    public List<SiteContact> Others { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var all = await db.SiteContacts.Ordered().AsNoTracking().ToListAsync();
        Live = all.FirstOrDefault(c => c.IsActive);
        Others = all.Where(c => c != Live).ToList();
    }

    public async Task<IActionResult> OnPostMakeLiveAsync(int id)
    {
        var all = await db.SiteContacts.ToListAsync();
        var chosen = all.FirstOrDefault(c => c.Id == id);
        if (chosen is null)
        {
            return NotFound();
        }
        foreach (var contact in all)
        {
            contact.IsActive = contact == chosen;
        }
        await db.SaveChangesAsync();
        TempData.Flash($"Теперь на сайте набор «{chosen.Title}».");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var contact = await db.SiteContacts.FindAsync(id);
        if (contact is null)
        {
            return NotFound();
        }
        if (contact.IsActive)
        {
            TempData.Flash("Набор, который сейчас на сайте, удалить нельзя: сначала сделайте основным другой.", FlashLevel.Error);
            return RedirectToPage();
        }
        db.SiteContacts.Remove(contact);
        await db.SaveChangesAsync();
        TempData.Flash($"Набор «{contact.Title}» удалён.");
        return RedirectToPage();
    }
}
