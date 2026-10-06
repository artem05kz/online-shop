using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Stores;

public class EditModel(ShopDbContext db, IConfiguration configuration) : PageModel
{
    private const string TooLong = "Не длиннее {1} символов.";

    public class InputModel
    {
        [Display(Name = "Город")]
        [Required(ErrorMessage = "Обязательное поле."), StringLength(100, ErrorMessage = TooLong)]
        public string City { get; set; } = "Актау";

        [Display(Name = "Адрес")]
        [Required(ErrorMessage = "Обязательное поле."), StringLength(300, ErrorMessage = TooLong)]
        public string Address { get; set; } = "";

        [Display(Name = "Тип точки")] public StoreKind Kind { get; set; } = StoreKind.Pickup;

        [Display(Name = "Часы работы")]
        [StringLength(200, ErrorMessage = TooLong)]
        public string? WorkingHours { get; set; }

        [Display(Name = "Обед")]
        [StringLength(100, ErrorMessage = TooLong)]
        public string? LunchHours { get; set; }

        [Display(Name = "Телефон")]
        [StringLength(50, ErrorMessage = TooLong)]
        public string? Phone { get; set; }

        [Display(Name = "Ссылка «как добраться»")]
        [Url(ErrorMessage = "Введите ссылку целиком, начиная с https://"), StringLength(500, ErrorMessage = TooLong)]
        public string? MapUrl { get; set; }

        // Координаты строкой: менеджер может вставить их из карт и с точкой, и с запятой.
        [Display(Name = "Широта")] public string? Latitude { get; set; }
        [Display(Name = "Долгота")] public string? Longitude { get; set; }

        [Display(Name = "Очерёдность в списке")]
        [Range(0, int.MaxValue, ErrorMessage = "Число не меньше 0.")]
        public int SortOrder { get; set; }

        [Display(Name = "Показывать на сайте")] public bool IsActive { get; set; } = true;
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    public int? Id { get; private set; }

    public string MapKey => TwoGis.Key(configuration);

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is null)
        {
            return Page();
        }
        var s = await db.Stores.FindAsync(id);
        if (s is null)
        {
            return NotFound();
        }
        Input = new InputModel
        {
            City = s.City, Address = s.Address, Kind = s.Kind, WorkingHours = s.WorkingHours, LunchHours = s.LunchHours,
            Phone = s.Phone, MapUrl = s.MapUrl, SortOrder = s.SortOrder, IsActive = s.IsActive,
            Latitude = s.Latitude?.ToString(CultureInfo.InvariantCulture),
            Longitude = s.Longitude?.ToString(CultureInfo.InvariantCulture),
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        var store = id is null ? new Store() : await db.Stores.FindAsync(id);
        if (store is null)
        {
            return NotFound();
        }
        var latitude = Coordinate(Input.Latitude, 90, "Input.Latitude");
        var longitude = Coordinate(Input.Longitude, 180, "Input.Longitude");
        if (!ModelState.IsValid)
        {
            return Page();
        }

        store.City = Input.City.Trim();
        store.Address = Input.Address.Trim();
        store.Kind = Input.Kind;
        store.WorkingHours = Input.WorkingHours?.Trim() ?? "";
        store.LunchHours = Input.LunchHours?.Trim() ?? "";
        store.Phone = Input.Phone?.Trim() ?? "";
        store.MapUrl = Input.MapUrl?.Trim() ?? "";
        store.Latitude = latitude;
        store.Longitude = longitude;
        store.SortOrder = Input.SortOrder;
        store.IsActive = Input.IsActive;
        if (store.Id == 0)
        {
            db.Stores.Add(store);
        }
        await db.SaveChangesAsync();
        TempData.Flash("Адрес магазина сохранён.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var store = await db.Stores.FindAsync(id);
        if (store is null)
        {
            return NotFound();
        }
        db.Stores.Remove(store);
        await db.SaveChangesAsync();
        TempData.Flash("Адрес магазина удалён.");
        return RedirectToPage("Index");
    }

    private double? Coordinate(string? raw, double limit, string key)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }
        if (double.TryParse(raw.Trim().Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && Math.Abs(value) <= limit)
        {
            return Math.Round(value, 6);
        }
        ModelState.AddModelError(key, $"Введите число от -{limit} до {limit}, например 43.6525.");
        return null;
    }
}
