using System.ComponentModel.DataAnnotations;

namespace OnlineShop.Web.Models;

/// <summary>
/// Слайд с товаром на первом экране главной (25.11.2026, раньше список был в коде — HeroSlides.cs).
/// 3D-рендер товара (прозрачный фон), рукописное слово за ним, заголовок, короткий текст; цена и ссылка — из товара.
/// Цвет фона по умолчанию берётся с этикетки на рендере (<see cref="Services.SlideColors"/>), менеджер может поправить.
/// Если товар скрыт, слайд не показывается.
/// </summary>
public class ProductSlide
{
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    /// <summary>Рукописное слово за товаром, обычно название линейки («Glass Gloss»).</summary>
    [StringLength(40)]
    public string Script { get; set; } = "";

    [StringLength(120)]
    public string Title { get; set; } = "";

    [StringLength(200)]
    public string Text { get; set; } = "";

    /// <summary>
    /// Рендер: путь в папке загрузок (slides/2026/11/x.webp) или, у первых трёх слайдов, адрес картинки сайта
    /// (/img/hero/glass-gloss.webp) — такой путь начинается с «/», и файл при удалении слайда не трогаем.
    /// </summary>
    [Required, StringLength(255)]
    public string ImagePath { get; set; } = "";

    /// <summary>Цвет фона, #rrggbb.</summary>
    [StringLength(7)]
    public string BgColor { get; set; } = "#2c6bd3";

    /// <summary>Цвет рукописного слова, #rrggbb.</summary>
    [StringLength(7)]
    public string ScriptColor { get; set; } = "#5b98f0";

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSiteImage => ImagePath.StartsWith('/');

    public override string ToString() => Title.Length > 0 ? Title : $"Слайд №{Id}";
}
