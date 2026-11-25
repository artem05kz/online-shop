namespace OnlineShop.Web.Infrastructure;

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

// Слайды первого экрана с 25.11.2026 хранятся в базе (Models/ProductSlide.cs) и собираются в панели.
