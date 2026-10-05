using System.ComponentModel.DataAnnotations;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.ViewModels;

/// <summary>
/// Поля формы оформления заказа. Сохранённый адрес только подставляет значения в поля скриптом,
/// а в заказ записывается то, что в полях в итоге оказалось: так можно поправить адрес на один раз.
/// </summary>
public class CheckoutForm
{
    private const string Required = "Обязательное поле.";
    private const string TooLong = "Не длиннее {1} символов.";

    [Display(Name = "Выбрать сохранённый адрес")]
    public int? SavedAddressId { get; set; }

    [Display(Name = "Имя получателя")]
    [Required(ErrorMessage = Required), StringLength(200, ErrorMessage = TooLong)]
    public string ContactName { get; set; } = "";

    [Display(Name = "Телефон")]
    [Required(ErrorMessage = Required), StringLength(30, ErrorMessage = TooLong)]
    public string ContactPhone { get; set; } = "";

    [Display(Name = "Город")]
    [Required(ErrorMessage = Required), StringLength(100, ErrorMessage = TooLong)]
    public string City { get; set; } = "";

    [Display(Name = "Адрес доставки")]
    [Required(ErrorMessage = Required), StringLength(300, ErrorMessage = TooLong)]
    public string StreetAndDetails { get; set; } = "";

    [Display(Name = "Комментарий к заказу")]
    public string? Comment { get; set; }

    public static string AddressLine(Address address) =>
        string.Join(", ", new[] { address.StreetLine, address.DetailsLine }.Where(p => p.Length > 0));
}

public class CheckoutViewModel
{
    public required CheckoutForm Form { get; init; }
    public required CartContents Cart { get; init; }
    public required IReadOnlyList<Address> Addresses { get; init; }
}
