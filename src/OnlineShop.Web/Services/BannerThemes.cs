namespace OnlineShop.Web.Services;

/// <summary>
/// Цвета блока с текстом у баннера первого экрана. Менеджер выбирает из списка, а не любой цвет:
/// так белый или чёрный текст на блоке всегда читается. Классы hero-slide--{Key} в style.css.
/// </summary>
public static class BannerThemes
{
    /// <param name="Light">Текст на блоке белый (тёмный фон).</param>
    public record Theme(string Key, string Name, string Color, bool Light);

    public static readonly IReadOnlyList<Theme> All =
    [
        new("navy", "Тёмно-синий", "#0b1a4d", true),
        new("ink", "Чёрный", "#0f0f0f", true),
        new("blue", "Синий", "#2c6bd3", true),
        new("red", "Красный", "#e0190f", true),
        new("lime", "Салатовый", "#c6ec5e", false),
        new("orange", "Оранжевый", "#ff9447", false),
        new("sun", "Жёлтый", "#ffd23f", false),
    ];

    public static Theme Get(string? key) => All.FirstOrDefault(t => t.Key == key) ?? All[0];

    public static bool IsKnown(string? key) => All.Any(t => t.Key == key);
}
