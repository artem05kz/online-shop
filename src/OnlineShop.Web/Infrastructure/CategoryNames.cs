namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Короткие названия разделов для кнопок вверху страницы раздела. Полные названия из 1С длинные
/// («Автошампуни для бесконтактной мойки автомобиля») и в ряд кнопок не помещаются.
/// Неизвестный раздел показывается полным названием.
/// </summary>
public static class CategoryNames
{
    private static readonly Dictionary<string, string> ShortByName = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Автошампуни для бесконтактной мойки автомобиля"] = "Бесконтактная мойка",
        ["Автошампуни для моек самообслуживания"] = "Мойки самообслуживания",
        ["Super сoncentrate автошампуни для моек самообслуживания"] = "Super Concentrate",
        ["Super concentrate автошампуни для моек самообслуживания"] = "Super Concentrate",
        ["Двухкомпонентные автошампуни для коммерческого транспорта"] = "Коммерческий транспорт",
        ["Автошампуни для ручной мойки автомобиля"] = "Ручная мойка",
    };

    public static string Short(string name) =>
        ShortByName.TryGetValue(name.Trim(), out var shortName) ? shortName : name;
}
