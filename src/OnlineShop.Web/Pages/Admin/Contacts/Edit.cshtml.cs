using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Contacts;

public class EditModel(ShopDbContext db) : PageModel
{
    private const string TooLong = "Не длиннее {1} символов.";

    public class InputModel
    {
        [Display(Name = "Название набора")]
        [Required(ErrorMessage = "Обязательное поле."), StringLength(100, ErrorMessage = TooLong)]
        public string Title { get; set; } = "Основные контакты";

        [Display(Name = "Использовать на сайте")] public bool IsActive { get; set; } = true;

        [Display(Name = "Телефон")] [StringLength(50, ErrorMessage = TooLong)] public string? Phone { get; set; }
        [Display(Name = "Дополнительный телефон")] [StringLength(50, ErrorMessage = TooLong)] public string? PhoneExtra { get; set; }

        [Display(Name = "Электронная почта")]
        [EmailAddress(ErrorMessage = "Введите правильный адрес электронной почты."), StringLength(254, ErrorMessage = TooLong)]
        public string? Email { get; set; }

        [Display(Name = "WhatsApp")] [StringLength(200, ErrorMessage = TooLong)] public string? Whatsapp { get; set; }
        [Display(Name = "Telegram")] [StringLength(200, ErrorMessage = TooLong)] public string? Telegram { get; set; }
        [Display(Name = "Instagram")] [StringLength(200, ErrorMessage = TooLong)] public string? Instagram { get; set; }

        [Display(Name = "Город")]
        [Required(ErrorMessage = "Обязательное поле."), StringLength(100, ErrorMessage = TooLong)]
        public string City { get; set; } = "Актау";

        [Display(Name = "Часы работы")] [StringLength(200, ErrorMessage = TooLong)] public string? WorkingHours { get; set; } = "Пн-Сб, 9:00-18:00";
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public int? Id { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is null)
        {
            return Page();
        }
        var c = await db.SiteContacts.FindAsync(id);
        if (c is null)
        {
            return NotFound();
        }
        Input = new InputModel
        {
            Title = c.Title, IsActive = c.IsActive, Phone = c.Phone, PhoneExtra = c.PhoneExtra, Email = c.Email,
            Whatsapp = c.Whatsapp, Telegram = c.Telegram, Instagram = c.Instagram, City = c.City, WorkingHours = c.WorkingHours,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        var contact = id is null ? new SiteContact() : await db.SiteContacts.FindAsync(id);
        if (contact is null)
        {
            return NotFound();
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }
        static string Clean(string? v) => v?.Trim() ?? "";
        contact.Title = Clean(Input.Title);
        contact.IsActive = Input.IsActive;
        contact.Phone = Clean(Input.Phone);
        contact.PhoneExtra = Clean(Input.PhoneExtra);
        contact.Email = Clean(Input.Email);
        contact.Whatsapp = Clean(Input.Whatsapp);
        contact.Telegram = Clean(Input.Telegram);
        contact.Instagram = Clean(Input.Instagram);
        contact.City = Clean(Input.City);
        contact.WorkingHours = Clean(Input.WorkingHours);
        if (contact.Id == 0)
        {
            db.SiteContacts.Add(contact);
        }
        await db.SaveChangesAsync();
        TempData.Flash("Контакты сохранены.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var contact = await db.SiteContacts.FindAsync(id);
        if (contact is null)
        {
            return NotFound();
        }
        db.SiteContacts.Remove(contact);
        await db.SaveChangesAsync();
        TempData.Flash("Набор контактов удалён.");
        return RedirectToPage("Index");
    }
}
