using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

/// <summary>
/// Сохранённый адрес доставки. Разбит на отдельные поля, а не одну строку, чтобы курьер
/// получал детали в предсказуемом виде, а выбор на карте мог подставить улицу,
/// пока остальное покупатель вписывает сам.
/// </summary>
public class Address
{
    public int Id { get; set; }

    [Display(Name = "Покупатель")]
    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [Display(Name = "Название адреса", Description = "Например: Дом, Работа")]
    [StringLength(100)]
    public string Label { get; set; } = "";

    [Display(Name = "Имя получателя")]
    [Required, StringLength(200)]
    public string FullName { get; set; } = "";

    [Display(Name = "Телефон")]
    [Required, StringLength(30)]
    public string Phone { get; set; } = "";

    [Display(Name = "Город")]
    [Required, StringLength(100)]
    public string City { get; set; } = "Актау";

    [Display(Name = "Улица / микрорайон")]
    [Required, StringLength(200)]
    public string Street { get; set; } = "";

    [Display(Name = "Дом")]
    [StringLength(30)]
    public string House { get; set; } = "";

    [Display(Name = "Квартира / офис")]
    [StringLength(30)]
    public string Apartment { get; set; } = "";

    [Display(Name = "Подъезд")]
    [StringLength(30)]
    public string Entrance { get; set; } = "";

    [Display(Name = "Этаж")]
    [StringLength(30)]
    public string Floor { get; set; } = "";

    [Display(Name = "Домофон")]
    [StringLength(30)]
    public string Intercom { get; set; } = "";

    [Display(Name = "Комментарий для курьера")]
    public string Comment { get; set; } = "";

    // Заполняется выбором точки на карте. Необязательно: адрес, введённый руками, тоже годится.
    [Display(Name = "Широта")]
    public double? Latitude { get; set; }

    [Display(Name = "Долгота")]
    public double? Longitude { get; set; }

    [Display(Name = "Использовать по умолчанию")]
    public bool IsDefault { get; set; }

    /// <summary>Улица и дом, например «14 микрорайон, дом 5».</summary>
    [NotMapped]
    public string StreetLine => JoinNonEmpty(Street, string.IsNullOrEmpty(House) ? "" : $"дом {House}");

    /// <summary>Квартира, подъезд, этаж и домофон, пустые пропускаются.</summary>
    [NotMapped]
    public string DetailsLine => JoinNonEmpty(
        string.IsNullOrEmpty(Apartment) ? "" : $"кв./офис {Apartment}",
        string.IsNullOrEmpty(Entrance) ? "" : $"подъезд {Entrance}",
        string.IsNullOrEmpty(Floor) ? "" : $"этаж {Floor}",
        string.IsNullOrEmpty(Intercom) ? "" : $"домофон {Intercom}");

    [NotMapped]
    public string ShortText => JoinNonEmpty(City, StreetLine, DetailsLine);

    private static string JoinNonEmpty(params string[] parts) =>
        string.Join(", ", parts.Where(p => !string.IsNullOrEmpty(p)));

    public override string ToString() => ShortText;
}
