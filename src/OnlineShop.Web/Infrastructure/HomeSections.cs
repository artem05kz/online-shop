namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Крупные разделы на главной (как на grass.su): «Средства для стирки», «…для уборки» и т. д. Это витрина, а не
/// категории каталога: в раздел входят категории, в названии которых есть одно из слов <see cref="Section.Match"/>.
/// Пока в каталоге только автохимия Ifoam, поэтому у стирки, уборки и дома список пуст — на плитке «Скоро».
/// Когда появятся товары, достаточно дописать слова сюда (или перенести разделы в админку — это к Артёму).
/// </summary>
public static class HomeSections
{
    /// <param name="Title">Название на плитке.</param>
    /// <param name="Color">Цвет плитки. Яркие, как на баннере; белый текст читается на каждом.</param>
    /// <param name="Match">Слова из названий категорий каталога, которые входят в раздел.</param>
    /// <param name="Image">Картинка из wwwroot/img/sections (рендер), когда будет готова.</param>
    public record Section(string Key, string Title, string Color, string[] Match, string? Image = null);

    // Все названия по одной схеме «Для …» (решение дизайнера 23.11). «Для бизнеса» — как на grass.su: автомойки, клининг, коммерческий транспорт (было «Профессиональные средства»,
    // не помещалось в узкую плитку).
    public static readonly IReadOnlyList<Section> All =
    [
        new("laundry", "Для стирки", "#2E9BE0", []),
        new("cleaning", "Для уборки", "#36B37E", []),
        new("home", "Для дома", "#F08A24", []),
        new("hygiene", "Для гигиены", "#E8579E", []),
        new("car", "Для авто", "#33332F", ["ручной мойки", "Воски", "Очистители"]),
        new("pro", "Для бизнеса", "#7B61E8", ["бесконтактной", "самообслуживания", "коммерческого"]),
    ];

    public static bool Contains(Section section, string categoryName) =>
        section.Match.Any(word => categoryName.Contains(word, StringComparison.OrdinalIgnoreCase));
}
