using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

public enum StoreKind
{
    Pickup,
    Shop,
    Warehouse,
}

public static class StoreKindInfo
{
    // Коды совпадают с Django-версией, чтобы данные переносились без перекодировки.
    public static string ToCode(this StoreKind kind) => kind switch
    {
        StoreKind.Shop => "shop",
        StoreKind.Warehouse => "warehouse",
        _ => "pickup",
    };

    public static StoreKind FromCode(string code) => code switch
    {
        "shop" => StoreKind.Shop,
        "warehouse" => StoreKind.Warehouse,
        _ => StoreKind.Pickup,
    };

    public static string Label(this StoreKind kind) => kind switch
    {
        StoreKind.Shop => "Магазин",
        StoreKind.Warehouse => "Склад",
        _ => "Пункт самовывоза",
    };
}

/// <summary>Физический магазин или пункт самовывоза. Менеджер добавляет их сам, без правки кода.</summary>
public class Store
{
    public int Id { get; set; }

    [Display(Name = "Город")]
    [Required, StringLength(100)]
    public string City { get; set; } = "Актау";

    [Display(Name = "Адрес",
        Description = "Как показывать покупателю целиком. Например: "
            + "г. Актау, 21 микрорайон, рынок «Алем», бутик №74")]
    [Required, StringLength(300)]
    public string Address { get; set; } = "";

    [Display(Name = "Тип точки")]
    public StoreKind Kind { get; set; } = StoreKind.Pickup;

    [Display(Name = "Часы работы", Description = "Например: Пн-Сб, 9:00-18:00")]
    [StringLength(200)]
    public string WorkingHours { get; set; } = "";

    [Display(Name = "Обед", Description = "Например: с 13:00 до 14:00. Оставьте пустым, если обеда нет.")]
    [StringLength(100)]
    public string LunchHours { get; set; } = "";

    [Display(Name = "Телефон")]
    [StringLength(50)]
    public string Phone { get; set; } = "";

    [Display(Name = "Широта")]
    public double? Latitude { get; set; }

    [Display(Name = "Долгота")]
    public double? Longitude { get; set; }

    [Display(Name = "Ссылка «как добраться»",
        Description = "Необязательно. Ссылка на точку в картах для кнопки «Как добраться».")]
    [Url, StringLength(500)]
    public string MapUrl { get; set; } = "";

    [Display(Name = "Очерёдность в списке", Description = "Точки выводятся по этому числу от меньшего к большему.")]
    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    [Display(Name = "Показывать на сайте")]
    public bool IsActive { get; set; } = true;

    /// <summary>Режим работы одной строкой для карточки на сайте.</summary>
    [NotMapped]
    public string HoursLine
    {
        get
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(WorkingHours))
            {
                parts.Add(WorkingHours);
            }
            if (!string.IsNullOrEmpty(LunchHours))
            {
                parts.Add($"обед {LunchHours}");
            }
            return string.Join(" | ", parts);
        }
    }

    public override string ToString() => string.IsNullOrEmpty(Address) ? $"Точка №{Id}" : Address;
}
