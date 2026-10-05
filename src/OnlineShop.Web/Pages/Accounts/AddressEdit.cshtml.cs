using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Accounts;

/// <summary>Новый адрес (/accounts/profile/addresses/add) или правка существующего (/accounts/profile/addresses/5).</summary>
[Authorize]
public class AddressEditModel(ShopDbContext db, IConfiguration configuration) : PageModel
{
    private const string Required = "Обязательное поле.";
    private const string TooLong = "Не длиннее {1} символов.";

    /// <summary>
    /// Поля формы. Выбор точки на карте заполняет город, улицу, дом и координаты,
    /// всё остальное покупатель вписывает сам.
    /// </summary>
    public class InputModel
    {
        [Display(Name = "Название адреса")]
        [StringLength(100, ErrorMessage = TooLong)]
        public string? Label { get; set; }

        [Display(Name = "Имя получателя")]
        [Required(ErrorMessage = Required), StringLength(200, ErrorMessage = TooLong)]
        public string FullName { get; set; } = "";

        [Display(Name = "Телефон")]
        [Required(ErrorMessage = Required), StringLength(30, ErrorMessage = TooLong)]
        public string Phone { get; set; } = "";

        [Display(Name = "Город")]
        [Required(ErrorMessage = Required), StringLength(100, ErrorMessage = TooLong)]
        public string City { get; set; } = "Актау";

        [Display(Name = "Улица / микрорайон")]
        [Required(ErrorMessage = Required), StringLength(200, ErrorMessage = TooLong)]
        public string Street { get; set; } = "";

        [Display(Name = "Дом")]
        [StringLength(30, ErrorMessage = TooLong)]
        public string? House { get; set; }

        [Display(Name = "Квартира / офис")]
        [StringLength(30, ErrorMessage = TooLong)]
        public string? Apartment { get; set; }

        [Display(Name = "Подъезд")]
        [StringLength(30, ErrorMessage = TooLong)]
        public string? Entrance { get; set; }

        [Display(Name = "Этаж")]
        [StringLength(30, ErrorMessage = TooLong)]
        public string? Floor { get; set; }

        [Display(Name = "Домофон")]
        [StringLength(30, ErrorMessage = TooLong)]
        public string? Intercom { get; set; }

        [Display(Name = "Комментарий для курьера")]
        public string? Comment { get; set; }

        // Координаты приходят строкой с точкой (так их пишет скрипт карты), а сайт работает
        // с русской культурой, где дробная часть отделяется запятой. Поэтому разбираем сами.
        public string? Latitude { get; set; }
        public string? Longitude { get; set; }

        [Display(Name = "Использовать по умолчанию")]
        public bool IsDefault { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public bool IsNew { get; private set; }

    /// <summary>Ключ Google Maps. Без него на форме открытые карты OpenStreetMap, им ключ не нужен.</summary>
    public string GoogleMapsKey => configuration["GOOGLE_MAPS_API_KEY"] ?? "";

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        IsNew = id is null;
        if (id is null)
        {
            var user = await db.Users.FindAsync(User.ShopUserId());
            Input.FullName = user?.FullName ?? "";
            return Page();
        }

        var address = await FindOwn(id.Value);
        if (address is null)
        {
            return NotFoundRedirect();
        }
        Input = ToInput(address);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        IsNew = id is null;
        var userId = User.ShopUserId();
        var address = id is null ? new Address { UserId = userId } : await FindOwn(id.Value);
        if (address is null)
        {
            return NotFoundRedirect();
        }

        var latitude = ParseCoordinate(Input.Latitude, 90);
        var longitude = ParseCoordinate(Input.Longitude, 180);
        if (!ModelState.IsValid)
        {
            return Page();
        }

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
        address.Latitude = latitude;
        address.Longitude = longitude;
        address.IsDefault = Input.IsDefault;
        if (address.Id == 0)
        {
            db.Addresses.Add(address);
        }

        await using var transaction = await db.Database.BeginTransactionAsync();
        await db.SaveChangesAsync();
        // Адрес по умолчанию может быть только один: отметка у этого снимает её с остальных.
        if (address.IsDefault)
        {
            await db.Addresses
                .Where(a => a.UserId == userId && a.Id != address.Id && a.IsDefault)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.IsDefault, false));
        }
        await transaction.CommitAsync();

        TempData.Flash("Адрес сохранён.");
        return RedirectToPage("Addresses");
    }

    public static string CoordinateText(double? value) =>
        value?.ToString("0.000000", CultureInfo.InvariantCulture) ?? "";

    private Task<Address?> FindOwn(int id)
    {
        var userId = User.ShopUserId();
        return db.Addresses.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
    }

    private IActionResult NotFoundRedirect()
    {
        TempData.Flash("Адрес не найден.", FlashLevel.Error);
        return RedirectToPage("Addresses");
    }

    private double? ParseCoordinate(string? raw, double limit)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && Math.Abs(value) <= limit)
        {
            return Math.Round(value, 6);
        }
        // Поля скрытые, сюда попадает только испорченный запрос: сообщаем у карты.
        ModelState.AddModelError("Input.Latitude", "Точка на карте не распознана, поставьте метку ещё раз.");
        return null;
    }

    private static string Clean(string? value) => value?.Trim() ?? "";

    private static InputModel ToInput(Address a) => new()
    {
        Label = a.Label, FullName = a.FullName, Phone = a.Phone, City = a.City, Street = a.Street,
        House = a.House, Apartment = a.Apartment, Entrance = a.Entrance, Floor = a.Floor, Intercom = a.Intercom,
        Comment = a.Comment, Latitude = CoordinateText(a.Latitude), Longitude = CoordinateText(a.Longitude),
        IsDefault = a.IsDefault,
    };
}
