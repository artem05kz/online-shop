using ClosedXML.Excel;

namespace OnlineShop.Tests;

/// <summary>
/// Excel-файлы в формате отчётов 1С для тестов импорта: настоящих выгрузок в репозитории нет,
/// они с ценами и не должны туда попадать.
/// </summary>
public static class OneCFiles
{
    // PNG 1x1 пиксель: самая маленькая настоящая картинка для «фото товара».
    public static readonly byte[] Png = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

    /// <summary>
    /// Прайс-лист: шапка с логотипом, строка заголовков и строка подзаголовков, группы с брендом
    /// в конце названия, товары с продолжениями-фасовками, фото в колонке «Фото».
    /// </summary>
    public static string PriceList(string dir)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Прайс");
        ws.Cell(1, 1).Value = "ТОО «Демо», прайс-лист";
        // Строка 3: заголовки (номер строки 0-based = 2), строка 4: подзаголовки.
        string[] header = ["№", "Фото", "Название", "Описание", "pH", "Артикул", "Объем", "Количество", "", "Цена опт"];
        for (var c = 0; c < header.Length; c++)
        {
            ws.Cell(3, c + 1).Value = header[c];
        }
        ws.Cell(4, 8).Value = "в коробке";
        ws.Cell(4, 9).Value = "на паллете";

        void Row(int r, string? name, object? article, string? pack = null, double? box = null, double? pallet = null,
            object? price = null, string? description = null, string? ph = null)
        {
            if (name is not null) ws.Cell(r, 3).Value = name;
            if (description is not null) ws.Cell(r, 4).Value = description;
            if (ph is not null) ws.Cell(r, 5).Value = ph;
            switch (article)
            {
                case double d: ws.Cell(r, 6).Value = d; break;
                case string s: ws.Cell(r, 6).Value = s; break;
            }
            if (pack is not null) ws.Cell(r, 7).Value = pack;
            if (box is not null) ws.Cell(r, 8).Value = box.Value;
            if (pallet is not null) ws.Cell(r, 9).Value = pallet.Value;
            switch (price)
            {
                case double d: ws.Cell(r, 10).Value = d; break;
                case string s: ws.Cell(r, 10).Value = s; break;
            }
        }

        // Строка товара без группы выше: парсер должен предупредить.
        Row(5, "Пробный товар без группы", 900001d, "1 л", 12, 600, 990d);
        Row(6, " Автошампуни для ручной мойки ifoam", null);
        Row(7, "Active Foam Pink", 131122d, "22 кг", 1, 32, 15990.5, "Пена для бесконтактной мойки", "12,5");
        Row(8, null, "131123 ", "5,5 кг", 4, 128, 4990d);
        Row(9, null, 131124d, "1 л", 12, 720, "по запросу");
        Row(10, null, null);
        Row(11, "Очистители ГРАСС GRASS", null);
        Row(12, "Очиститель дисков", 125333d, "500 мл", 20, 1200, 1290d);
        Row(13, "Средства без бренда", null);
        Row(14, "Губка", 777d, "шт", null, null, 150d);

        using (var png = new MemoryStream(Png))
        {
            // Логотип в углу листа (колонка не «Фото») не должен стать фото товара.
            ws.AddPicture(png).MoveTo(ws.Cell(1, 1));
        }
        using (var png = new MemoryStream(Png))
        {
            ws.AddPicture(png).MoveTo(ws.Cell(7, 2));
        }

        var path = Path.Combine(dir, $"price-{Guid.NewGuid():N}.xlsx");
        wb.SaveAs(path);
        return path;
    }

    /// <summary>
    /// Товар блоком, как в настоящем прайсе: «Фото», «Описание» и «pH» объединены на все строки фасовок,
    /// а название стоит во второй строке блока. Выше идёт обычный товар, к которому первую фасовку
    /// легко приписать по ошибке.
    /// </summary>
    public static string PriceListWithShiftedName(string dir)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Прайс");
        string[] header = ["Название", "Фото", "Описание товара", "PH", "Артикул", "Объем", "Фасовка, шт", "", "Цена"];
        for (var c = 0; c < header.Length; c++)
        {
            ws.Cell(1, c + 1).Value = header[c];
        }
        ws.Cell(2, 7).Value = "В коробке";
        ws.Cell(2, 8).Value = "На паллете";
        ws.Cell(3, 1).Value = "Очистители и полироли ifoam";
        ws.Range(3, 1, 3, 8).Merge();

        // Обычный товар: название в первой строке своего блока.
        ws.Cell(4, 1).Value = "GLASS GLOSS"; ws.Cell(4, 3).Value = "Для стёкол"; ws.Cell(4, 4).Value = "pH = 7";
        ws.Cell(4, 5).Value = 202105; ws.Cell(4, 6).Value = "5 кг"; ws.Cell(4, 9).Value = 495.9;
        ws.Cell(5, 5).Value = 202101; ws.Cell(5, 6).Value = "1 кг"; ws.Cell(5, 9).Value = 127.8;
        foreach (var c in new[] { 2, 3, 4 }) ws.Range(4, c, 5, c).Merge();

        // Товар со сдвигом: блок строк 6-8, название в строке 7.
        ws.Cell(6, 3).Value = "Для двигателя"; ws.Cell(6, 4).Value = "pH = 12";
        ws.Cell(6, 5).Value = 206122; ws.Cell(6, 6).Value = "22 кг"; ws.Cell(6, 9).Value = 2234.4;
        ws.Cell(7, 1).Value = "MOTOR SHINE"; ws.Cell(7, 5).Value = 206105; ws.Cell(7, 6).Value = "5 кг"; ws.Cell(7, 9).Value = 576.2;
        ws.Cell(8, 5).Value = 206101; ws.Cell(8, 6).Value = "1 кг"; ws.Cell(8, 9).Value = 153.3;
        foreach (var c in new[] { 2, 3, 4 }) ws.Range(6, c, 8, c).Merge();
        using (var png = new MemoryStream(Png))
        {
            ws.AddPicture(png).MoveTo(ws.Cell(6, 2));
        }

        var path = Path.Combine(dir, $"shifted-{Guid.NewGuid():N}.xlsx");
        wb.SaveAs(path);
        return path;
    }

    /// <summary>Остатки: строка организации, заголовки, группа без артикула, повтор и остаток не числом.</summary>
    public static string Stock(string dir, params (object Article, object Qty)[] extra)
    {
        using var wb = new XLWorkbook();
        var ws = wb.AddWorksheet("Остатки");
        ws.Cell(1, 1).Value = "ТОО «Демо», склад";
        ws.Cell(2, 1).Value = "Номенклатура";
        ws.Cell(2, 2).Value = "Артикул";
        ws.Cell(2, 3).Value = "Количество";
        var r = 3;
        void Row(string name, object? article, object? qty)
        {
            ws.Cell(r, 1).Value = name;
            switch (article)
            {
                case double d: ws.Cell(r, 2).Value = d; break;
                case string s: ws.Cell(r, 2).Value = s; break;
            }
            switch (qty)
            {
                case double d: ws.Cell(r, 3).Value = d; break;
                case string s: ws.Cell(r, 3).Value = s; break;
            }
            r++;
        }
        Row("Автохимия", null, null);
        Row("Active Foam 22", 131122d, 5d);
        Row("Active Foam 22 (второй склад)", "131122", 2d);
        Row("Пена 5,5", "131123", 9d);
        Row("Непонятный остаток", "X-1", "много");
        Row("Нет в каталоге", 555555d, 3d);
        foreach (var (article, qty) in extra)
        {
            Row("Дополнительно", article, qty);
        }

        var path = Path.Combine(dir, $"stock-{Guid.NewGuid():N}.xlsx");
        wb.SaveAs(path);
        return path;
    }
}
