using System.ComponentModel.DataAnnotations;

namespace OnlineShop.Web.Models;

/// <summary>
/// Слайд карусели на главной. Менеджер сам загружает картинку и выбирает,
/// когда её показывать, поэтому для новой акции код менять не нужно.
/// </summary>
public class Banner
{
    public int Id { get; set; }

    [Display(Name = "Заголовок")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    // Путь к файлу относительно папки загрузок, например banners/2026/10/sale.jpg.
    [Display(Name = "Изображение")]
    [Required, StringLength(255)]
    public string ImagePath { get; set; } = "";

    [Display(Name = "Ссылка при нажатии",
        Description = "Куда вести при клике на баннер, например /catalog/grass/ (необязательно)")]
    [StringLength(300)]
    public string LinkUrl { get; set; } = "";

    [Display(Name = "Очерёдность в слайдере",
        Description = "Определяет, в каком порядке баннеры сменяют друг друга на главной странице: "
            + "от меньшего числа к большему. Например, баннер с числом 1 покажется первым.")]
    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    [Display(Name = "Показывать на сайте")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Показывать с", Description = "Необязательно")]
    public DateTime? StartsAt { get; set; }

    [Display(Name = "Показывать до", Description = "Необязательно")]
    public DateTime? EndsAt { get; set; }

    public override string ToString() => string.IsNullOrEmpty(Title) ? $"Баннер №{Id}" : Title;
}
