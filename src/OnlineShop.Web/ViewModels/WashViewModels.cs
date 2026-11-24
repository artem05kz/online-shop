using System.ComponentModel.DataAnnotations;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.ViewModels;

/// <summary>Форма записи на мойку.</summary>
public class WashForm
{
    private const string Required = "Обязательное поле.";
    private const string TooLong = "Не длиннее {1} символов.";

    [Required(ErrorMessage = "Выберите мойку.")]
    public string Wash { get; set; } = "";

    [Required(ErrorMessage = "Выберите услугу.")]
    public string Service { get; set; } = "";

    /// <summary>День, yyyy-MM-dd.</summary>
    [Required(ErrorMessage = "Выберите день.")]
    public string Date { get; set; } = "";

    /// <summary>Время, HH:mm.</summary>
    [Required(ErrorMessage = "Выберите время.")]
    public string Time { get; set; } = "";

    [Display(Name = "Как к вам обращаться")]
    [Required(ErrorMessage = Required), StringLength(100, ErrorMessage = TooLong)]
    public string Name { get; set; } = "";

    [Display(Name = "Телефон")]
    [Required(ErrorMessage = Required), StringLength(30, ErrorMessage = TooLong)]
    public string Phone { get; set; } = "";

    [Display(Name = "Машина")]
    [StringLength(100, ErrorMessage = TooLong)]
    public string? Car { get; set; }

    [Display(Name = "Комментарий")]
    [StringLength(500, ErrorMessage = TooLong)]
    public string? Comment { get; set; }

    public bool AcceptPrivacy { get; set; }
}

public class WashPageViewModel
{
    public required WashForm Form { get; init; }
    public required IReadOnlyList<DateOnly> Days { get; init; }
    public required IReadOnlyList<WashSlot> Slots { get; init; }
    public CarWash? Wash => CarWashes.Find(Form.Wash);
    public WashService? Service => CarWashes.FindService(Form.Service);
}

public class WashBookedViewModel
{
    public required WashBooking Booking { get; init; }
    public CarWash? Wash => CarWashes.Find(Booking.WashKey);
    public required IReadOnlyList<Product> Products { get; init; }
    public bool JustBooked { get; init; }
    public bool CanCancel { get; init; }
}

/// <summary>«Мои записи на мойку» в кабинете: предстоящие и прошедшие.</summary>
public class MyWashViewModel
{
    public required IReadOnlyList<WashBooking> Upcoming { get; init; }
    public required IReadOnlyList<WashBooking> Past { get; init; }
}
