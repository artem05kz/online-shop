namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Слайд первого экрана главной: 3D-картинка товара, рукописное название за ней, короткий текст и «Купить».
/// Цена и ссылка берутся из базы по <see cref="ProductSlug"/>: если товар скрыт или переименован в 1С
/// (сменился slug), слайд просто не показывается, битой ссылки не будет.
/// </summary>
/// <param name="ProductSlug">Slug товара (последняя часть адреса /product/…).</param>
/// <param name="Script">Рукописное слово за товаром, обычно название линейки.</param>
/// <param name="Title">Заголовок слайда: что это за товар, простыми словами.</param>
/// <param name="Text">Одна-две короткие строки: чем хорош.</param>
/// <param name="Image">Картинка в wwwroot/img/hero/ (WebP с прозрачным фоном, 1360×800 — ширина 8 колонок сетки).</param>
/// <param name="Theme">Цвет слайда: класс hero-slide--{Theme} в style.css.</param>
public record HeroSlide(string ProductSlug, string Script, string Title, string Text, string Image, string Theme);

/// <summary>
/// Товары, для которых есть 3D-рендер (Blender) для плитки раздела: wwwroot/img/tiles/{файл}.
/// Если в разделе есть такой товар, плитка показывает рендер вместо фото из прайса.
/// </summary>
public static class TileRenders
{
    public static readonly IReadOnlyDictionary<string, string> BySlug = new Dictionary<string, string>
    {
        ["очиститель-стёкол-glass-gloss"] = "glass-gloss.webp",
        ["автошампунь-для-ручной-мойки-nano-soft"] = "nano-soft.webp",
        ["холодный-воск-top-wax"] = "top-wax.webp",
    };
}

public static class HeroSlides
{
    // Порядок = порядок показа. Картинки рендерятся в Blender: design-work/blender/glass-gloss/.
    public static readonly IReadOnlyList<HeroSlide> All =
    [
        new("очиститель-стёкол-glass-gloss", "Glass Gloss",
            "Очиститель стёкол GLASS GLOSS",
            "Готов к применению. Стёкла и зеркала без разводов, быстро высыхает.",
            "glass-gloss.webp", "blue"),
        new("автошампунь-для-ручной-мойки-nano-soft", "Nano Soft",
            "Автошампунь NANO SOFT",
            "Для ручной мойки. Бережёт лак и хром, экономно расходуется, придаёт блеск.",
            "nano-soft.webp", "lime"),
        new("холодный-воск-top-wax", "Top Wax",
            "Холодный воск TOP WAX",
            "Блеск и водоотталкивающий эффект после мойки.",
            "top-wax.webp", "orange"),
    ];
}
