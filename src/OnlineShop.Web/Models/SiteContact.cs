using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

/// <summary>
/// Контакты магазина для шапки и подвала сайта. На сайте используется одна запись,
/// у которой стоит галочка «Использовать на сайте», остальные можно держать как черновики.
/// </summary>
public class SiteContact
{
    public int Id { get; set; }

    [Display(Name = "Название набора", Description = "Только для вас, на сайте не показывается.")]
    [Required, StringLength(100)]
    public string Title { get; set; } = "Основные контакты";

    [Display(Name = "Телефон")]
    [StringLength(50)]
    public string Phone { get; set; } = "";

    [Display(Name = "Дополнительный телефон")]
    [StringLength(50)]
    public string PhoneExtra { get; set; } = "";

    [Display(Name = "Электронная почта")]
    [EmailAddress, StringLength(254)]
    public string Email { get; set; } = "";

    [Display(Name = "WhatsApp", Description = "Номер или ссылка. Например: +7 701 245-48-48")]
    [StringLength(200)]
    public string Whatsapp { get; set; } = "";

    [Display(Name = "Telegram", Description = "Имя пользователя или ссылка. Например: @grass_aktau")]
    [StringLength(200)]
    public string Telegram { get; set; } = "";

    [Display(Name = "Instagram", Description = "Имя пользователя или ссылка. Например: grass_aktau")]
    [StringLength(200)]
    public string Instagram { get; set; } = "";

    [Display(Name = "Город")]
    [Required, StringLength(100)]
    public string City { get; set; } = "Актау";

    [Display(Name = "Часы работы")]
    [StringLength(200)]
    public string WorkingHours { get; set; } = "Пн-Сб, 9:00-18:00";

    [Display(Name = "Использовать на сайте",
        Description = "Показывается тот набор, у которого стоит эта галочка.")]
    public bool IsActive { get; set; } = true;

    // Менеджер вписывает номер или имя пользователя как удобно, а ссылки собираются здесь.

    [NotMapped]
    public string PhoneLink => string.IsNullOrEmpty(Phone) ? "" : "+" + Digits(Phone);

    [NotMapped]
    public string WhatsappLink
    {
        get
        {
            if (string.IsNullOrEmpty(Whatsapp))
            {
                return "";
            }
            if (Whatsapp.StartsWith("http"))
            {
                return Whatsapp;
            }
            var digits = Digits(Whatsapp);
            return digits.Length > 0 ? $"https://wa.me/{digits}" : "";
        }
    }

    [NotMapped]
    public string TelegramLink => ProfileLink(Telegram, "https://t.me/");

    [NotMapped]
    public string InstagramLink => ProfileLink(Instagram, "https://instagram.com/");

    private static string Digits(string value) => new(value.Where(char.IsDigit).ToArray());

    private static string ProfileLink(string value, string prefix)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "";
        }
        return value.StartsWith("http") ? value : prefix + value.TrimStart('@');
    }

    public override string ToString() => Title;
}
