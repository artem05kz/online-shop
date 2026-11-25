using System.ComponentModel.DataAnnotations;

namespace OnlineShop.Web.Models;

/// <summary>
/// Слайд первого экрана главной из панели: слева фото (товары сдвинуты в центр этой части по <see cref="FocusX"/>),
/// справа цветной блок с текстом, который менеджер пишет сам. Для новой акции код менять не нужно.
/// </summary>
public class Banner
{
    public int Id { get; set; }

    [Display(Name = "Заголовок")]
    [StringLength(200)]
    public string Title { get; set; } = "";

    /// <summary>Рукописное слово над заголовком (название линейки, «Новинка»). Необязательно.</summary>
    [Display(Name = "Рукописное слово")]
    [StringLength(40)]
    public string Script { get; set; } = "";

    [Display(Name = "Текст")]
    [StringLength(300)]
    public string Text { get; set; } = "";

    [Display(Name = "Надпись на кнопке")]
    [StringLength(40)]
    public string ButtonText { get; set; } = "";

    /// <summary>Цвет блока с текстом: ключ из <see cref="Services.BannerThemes"/>.</summary>
    [Display(Name = "Цвет блока с текстом")]
    [StringLength(20)]
    public string Theme { get; set; } = "navy";

    /// <summary>
    /// Где на фото главное (товары), в процентах ширины слева: 0 — у левого края, 50 — посередине.
    /// Определяется сам при загрузке (<see cref="Services.BannerFocus"/>), менеджер может поправить щелчком по превью.
    /// </summary>
    [Range(0, 100)]
    public int FocusX { get; set; } = 50;

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
