using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Addresses;

/// <summary>Правка адреса покупателя менеджером, например если курьер уточнил подъезд по телефону.</summary>
public class EditModel(ShopDbContext db) : PageModel
{
    private const string Required = "Обязательное поле.";
    private const string TooLong = "Не длиннее {1} символов.";

    public class InputModel
    {
        [Display(Name = "Название адреса")] [StringLength(100, ErrorMessage = TooLong)] public string? Label { get; set; }
        [Display(Name = "Имя получателя")] [Required(ErrorMessage = Required), StringLength(200, ErrorMessage = TooLong)] public string FullName { get; set; } = "";
        [Display(Name = "Телефон")] [Required(ErrorMessage = Required), StringLength(30, ErrorMessage = TooLong)] public string Phone { get; set; } = "";
        [Display(Name = "Город")] [Required(ErrorMessage = Required), StringLength(100, ErrorMessage = TooLong)] public string City { get; set; } = "";
        [Display(Name = "Улица / микрорайон")] [Required(ErrorMessage = Required), StringLength(200, ErrorMessage = TooLong)] public string Street { get; set; } = "";
        [Display(Name = "Дом")] [StringLength(30, ErrorMessage = TooLong)] public string? House { get; set; }
        [Display(Name = "Квартира / офис")] [StringLength(30, ErrorMessage = TooLong)] public string? Apartment { get; set; }
        [Display(Name = "Подъезд")] [StringLength(30, ErrorMessage = TooLong)] public string? Entrance { get; set; }
        [Display(Name = "Этаж")] [StringLength(30, ErrorMessage = TooLong)] public string? Floor { get; set; }
        [Display(Name = "Домофон")] [StringLength(30, ErrorMessage = TooLong)] public string? Intercom { get; set; }
        [Display(Name = "Комментарий для курьера")] public string? Comment { get; set; }
        [Display(Name = "Использовать по умолчанию")] public bool IsDefault { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public Address Address { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var address = await Load(id);
        if (address is null)
        {
            return NotFound();
        }
        Address = address;
        Input = new InputModel
        {
            Label = address.Label, FullName = address.FullName, Phone = address.Phone, City = address.City, Street = address.Street,
            House = address.House, Apartment = address.Apartment, Entrance = address.Entrance, Floor = address.Floor,
            Intercom = address.Intercom, Comment = address.Comment, IsDefault = address.IsDefault,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var address = await Load(id);
        if (address is null)
        {
            return NotFound();
        }
        Address = address;
        if (!ModelState.IsValid)
        {
            return Page();
        }
        static string Clean(string? v) => v?.Trim() ?? "";
        address.Label = Clean(Input.Label);
        address.FullName = Clean(Input.FullName);
        address.Phone = Clean(Input.Phone);
        address.City = Clean(Input.City);
        address.Street = Clean(Input.Street);
        address.House = Clean(Input.House);
        address.Apartment = Clean(Input.Apartment);
        address.Entrance = Clean(Input.Entrance);
        address.Floor = Clean(Input.Floor);
        address.Intercom = Clean(Input.Intercom);
        address.Comment = Clean(Input.Comment);
        address.IsDefault = Input.IsDefault;

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        // Как в кабинете покупателя: адрес по умолчанию у покупателя только один.
        if (address.IsDefault)
        {
            await db.Addresses.Where(a => a.UserId == address.UserId && a.Id != address.Id && a.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false));
        }
        await transaction.CommitAsync();
        TempData.Flash("Адрес сохранён.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var deleted = await db.Addresses.Where(a => a.Id == id).ExecuteDeleteAsync();
        TempData.Flash(deleted > 0 ? "Адрес удалён." : "Адрес не найден.", deleted > 0 ? FlashLevel.Success : FlashLevel.Error);
        return RedirectToPage("Index");
    }

    private Task<Address?> Load(int id) => db.Addresses.Include(a => a.User).FirstOrDefaultAsync(a => a.Id == id);
}
