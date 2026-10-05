using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Web.Pages.Admin.Contacts;

public class IndexModel(ShopDbContext db) : PageModel
{
    public class Row
    {
        public int Id { get; set; }
        public string Title { get; set; } = "";
        public string Phone { get; set; } = "";
        public string Email { get; set; } = "";
        public string Whatsapp { get; set; } = "";
        public string Instagram { get; set; } = "";
        public bool IsActive { get; set; }
    }

    [BindProperty] public List<Row> Rows { get; set; } = [];

    public async Task OnGetAsync()
    {
        Rows = await db.SiteContacts.Ordered()
            .Select(c => new Row
            {
                Id = c.Id, Title = c.Title, Phone = c.Phone, Email = c.Email, Whatsapp = c.Whatsapp,
                Instagram = c.Instagram, IsActive = c.IsActive,
            })
            .ToListAsync();
    }

    public async Task<IActionResult> OnPostSaveListAsync()
    {
        var byId = Rows.ToDictionary(r => r.Id);
        var contacts = await db.SiteContacts.Where(c => byId.Keys.Contains(c.Id)).ToListAsync();
        foreach (var contact in contacts)
        {
            contact.IsActive = byId[contact.Id].IsActive;
        }
        await db.SaveChangesAsync();
        var active = contacts.Count(c => c.IsActive);
        TempData.Flash(active > 1
            ? "Сохранено. Галочка стоит у нескольких наборов: на сайте показывается первый из них."
            : "Сохранено.", active > 1 ? FlashLevel.Info : FlashLevel.Success);
        return RedirectToPage();
    }
}
