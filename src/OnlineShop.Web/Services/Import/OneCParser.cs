using System.Globalization;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace OnlineShop.Web.Services.Import;

public record ParsedVariant(
    string Article,
    string PackLabel,
    decimal? VolumeValue,
    string VolumeUnit,
    int? BoxQty,
    int? PalletQty,
    decimal? WholesalePrice);

public class ParsedProduct
{
    public required string Name { get; set; }
    public required string Brand { get; set; }
    /// <summary>Очищенное название категории для показа.</summary>
    public required string CategoryPath { get; set; }
    /// <summary>Строка группы из выгрузки как есть, для отладки импорта.</summary>
    public required string SourceGroupPath { get; init; }
    public required string Description { get; init; }
    public required string Ph { get; init; }
    public List<ParsedVariant> Variants { get; } = [];
    public byte[]? ImageBytes { get; init; }
    public string ImageExt { get; init; } = "";

    /// <summary>
    /// Устойчивый ключ товара для галочек на странице предпросмотра: артикул первой фасовки.
    /// У каждого разобранного товара есть хотя бы одна фасовка, так парсер и узнаёт строку товара.
    /// </summary>
    public string Key => Variants[0].Article;
}

public record PriceListResult(List<ParsedProduct> Products, List<string> Warnings, IReadOnlyDictionary<string, int?> ColumnMap);

public record StockResult(Dictionary<string, int> Quantities, List<string> Warnings);

/// <summary>
/// Разбор двух Excel-отчётов, которые менеджер выгружает из 1С:
/// «прайс лист» (название, фото, описание, pH, артикул, фасовка, количество в коробке и на паллете,
/// оптовая цена) и «остатки» (артикул и количество на складе).
///
/// Оба отчёта 1С выгружает плоским листом, где строки-заголовки групп (только название, без данных)
/// перемешаны со строками товаров. Чистой таблицы нет, поэтому строки разбираются по одной.
/// Модуль только читает файлы и ничего не пишет в базу: так можно показать предпросмотр перед записью.
/// Перенесено из catalog/importers/onec.py Django-версии с той же логикой.
/// </summary>
public static partial class OneCParser
{
    // Бренд в выгрузке приписан в конец названия группы, отдельной колонки для него нет.
    private static readonly string[] BrandKeywords = ["ifoam", "grass"];
    public const string UnknownBrand = "Без бренда (уточнить)";
    private const int HeaderSearchRows = 10;

    /// <summary>
    /// 1С иногда дополняет артикул неразрывными пробелами, а Excel хранит числовые артикулы числом
    /// (131122 вместо "131122"). Приводим обе стороны сопоставления к одной строке.
    /// </summary>
    public static string NormalizeArticle(object? value) => value switch
    {
        null => "",
        double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(CultureInfo.InvariantCulture),
        _ => Text(value),
    };

    /// <summary>«22 кг» превращается в (22, «кг»), «5,5 кг» в (5.5, «кг»).</summary>
    public static (decimal? Value, string Unit) ParsePackLabel(object? raw)
    {
        if (raw is null)
        {
            return (null, "");
        }
        var text = Text(raw);
        var match = PackRegex().Match(text);
        if (!match.Success)
        {
            return (null, text);
        }
        var value = decimal.TryParse(match.Groups[1].Value.Replace(',', '.'), NumberStyles.Number,
            CultureInfo.InvariantCulture, out var number) ? number : (decimal?)null;
        return (value, match.Groups[2].Value.Trim());
    }

    /// <summary>
    /// Строки групп выглядят как « Автошампуни для ... ifoam»: бренд дописан в конец названия.
    /// Вытаскиваем его, а если не узнали, помечаем явно, чтобы товары не легли не туда молча.
    /// </summary>
    public static (string Brand, string Category) GuessBrandAndCategory(string rawHeader)
    {
        var text = rawHeader.Trim();
        var lowered = text.ToLowerInvariant();
        foreach (var brand in BrandKeywords)
        {
            if (lowered.EndsWith(brand))
            {
                var clean = Regex.Replace(text, $@"\s*{brand}\s*$", "", RegexOptions.IgnoreCase).Trim();
                return (char.ToUpperInvariant(brand[0]) + brand[1..], clean.Length > 0 ? clean : text);
            }
        }
        return (UnknownBrand, text);
    }

    public static PriceListResult ParsePriceList(string path)
    {
        using var workbook = Open(path);
        var sheet = workbook.Worksheets.First();
        var rows = ReadRows(sheet);
        var warnings = new List<string>();

        var headerIdx = FindHeaderRow(rows, ["артикул", "объем"]) ?? FindHeaderRow(rows, ["артикул"])
            ?? throw new ImportFormatException("Не нашёл строку заголовков (со словом «Артикул») в первых 10 строках прайс-листа.");

        var header = rows[headerIdx];
        var subHeader = headerIdx + 1 < rows.Count ? rows[headerIdx + 1] : [];

        var col = new Dictionary<string, int?>
        {
            ["name"] = FindCol(header, "название"),
            ["description"] = FindCol(header, "описание"),
            ["ph"] = FindCol(header, "ph"),
            ["article"] = FindCol(header, "артикул"),
            ["pack_label"] = OrPython(FindCol(header, "объем"), () => FindCol(header, "объём")),
            ["box_qty"] = FindCol(subHeader, "в коробке"),
        };
        // В некоторых выгрузках подзаголовок не повторяется. Тогда «в коробке» сразу после «Объем»:
        // так было во всех выгрузках, что попадались.
        col["box_qty"] ??= (col["pack_label"] ?? 0) + 1;
        col["pallet_qty"] = col["box_qty"] + 1;
        col["price"] = col["box_qty"] + 2;

        foreach (var (key, label) in new[] { ("name", "название"), ("article", "артикул") })
        {
            if (col[key] is null)
            {
                throw new ImportFormatException($"Не нашёл колонку «{label}» в прайс-листе, проверьте формат файла.");
            }
        }

        LiftNamesToBlockStart(sheet, rows, col["name"]!.Value,
            new[] { PhotoColumn(header), col["description"], col["ph"] }.OfType<int>().ToArray(), headerIdx + 2);

        // Фото привязываются к строке по ячейке, к которой прикреплены. Берём только колонку «Фото»:
        // картинка в углу листа обычно логотип компании, а не фото товара.
        var photoCol = PhotoColumn(header) ?? 1;
        var photos = new Dictionary<int, (byte[] Bytes, string Ext)>();
        foreach (var picture in sheet.Pictures)
        {
            if (picture.TopLeftCell.Address.ColumnNumber - 1 != photoCol)
            {
                continue;
            }
            using var copy = new MemoryStream();
            picture.ImageStream.Position = 0;
            picture.ImageStream.CopyTo(copy);
            photos[picture.TopLeftCell.Address.RowNumber - 1] = (copy.ToArray(), picture.Format.ToString().ToLowerInvariant());
        }

        var products = new List<ParsedProduct>();
        var currentBrand = UnknownBrand;
        var currentCategory = "";
        var currentCategoryRaw = "";
        ParsedProduct? current = null;

        // Пропускаем строку заголовков и строку подзаголовков.
        for (var rowIdx = headerIdx + 2; rowIdx < rows.Count; rowIdx++)
        {
            var row = rows[rowIdx];
            var name = TextAt(row, col["name"]);
            var article = NormalizeArticle(At(row, col["article"]));

            if (name.Length == 0 && article.Length == 0)
            {
                continue;
            }
            if (name.Length > 0 && article.Length == 0)
            {
                (currentBrand, currentCategory) = GuessBrandAndCategory(name);
                currentCategoryRaw = name;
                current = null;
                continue;
            }

            var (volumeValue, volumeUnit) = ParsePackLabel(At(row, col["pack_label"]));
            var variant = new ParsedVariant(
                article,
                TextAt(row, col["pack_label"]),
                volumeValue,
                volumeUnit,
                AsInt(At(row, col["box_qty"])),
                AsInt(At(row, col["pallet_qty"])),
                At(row, col["price"]) is double price ? ToDecimal(price) : null);

            if (name.Length > 0)
            {
                if (currentCategory.Length == 0)
                {
                    warnings.Add($"Товар «{name}» (арт. {article}) идёт без заголовка категории выше, проверьте файл.");
                }
                photos.TryGetValue(rowIdx, out var photo);
                current = new ParsedProduct
                {
                    Name = name,
                    Brand = currentBrand,
                    CategoryPath = currentCategory.Length > 0 ? currentCategory : "Без категории",
                    SourceGroupPath = currentCategoryRaw,
                    Description = TextAt(row, col["description"]),
                    Ph = TextAt(row, col["ph"]),
                    ImageBytes = photo.Bytes,
                    ImageExt = photo.Ext ?? "",
                };
                current.Variants.Add(variant);
                products.Add(current);
            }
            else if (current is null)
            {
                warnings.Add($"Строка с артикулом {article} без названия и без товара выше, пропустил.");
            }
            else
            {
                // Продолжение: ещё одна фасовка того же товара.
                current.Variants.Add(variant);
            }
        }

        // Импорт ищет товар по категории и названию. Два разных товара с одним названием (в прайсе
        // с образцом так у концентрата и готового «MOTOR SHINE») сольются в одну карточку с описанием
        // второго. Предупреждаем, чтобы на предпросмотре одно из названий можно было поправить.
        foreach (var same in products.GroupBy(p => (p.Brand, p.CategoryPath, p.Name)).Where(g => g.Count() > 1))
        {
            warnings.Add($"Товар «{same.Key.Name}» встречается в файле {same.Count()} раза (артикулы "
                + string.Join("; ", same.Select(p => string.Join(", ", p.Variants.Select(v => v.Article))))
                + "): при загрузке это будет одна карточка. Если это разные товары, поправьте название ниже.");
        }

        return new PriceListResult(products, warnings, col);
    }

    public static StockResult ParseStock(string path)
    {
        using var workbook = Open(path);
        var rows = ReadRows(workbook.Worksheets.First());
        var warnings = new List<string>();

        var headerIdx = FindHeaderRow(rows, ["артикул", "количество"])
            ?? throw new ImportFormatException("Не нашёл строку заголовков (со словами «Артикул» и «Количество») в файле остатков.");
        var header = rows[headerIdx];
        var articleCol = FindCol(header, "артикул");
        var qtyCol = FindCol(header, "количество");
        if (articleCol is null || qtyCol is null)
        {
            throw new ImportFormatException("Не нашёл колонки «Артикул» и «Количество» в файле остатков.");
        }

        var quantities = new Dictionary<string, int>();
        foreach (var row in rows.Skip(headerIdx + 1))
        {
            var article = NormalizeArticle(At(row, articleCol));
            if (article.Length == 0)
            {
                // Строка группы или название организации: сопоставлять не по чему.
                continue;
            }
            int qty;
            if (At(row, qtyCol) is double number)
            {
                qty = (int)number;
            }
            else
            {
                var raw = At(row, qtyCol);
                warnings.Add(raw is null
                    ? $"Артикул {article}: остаток не указан, поставил 0."
                    : $"Артикул {article}: остаток не число ({Text(raw)}), поставил 0.");
                qty = 0;
            }
            if (quantities.TryGetValue(article, out var existing))
            {
                warnings.Add($"Артикул {article} встретился в остатках дважды, суммирую.");
                quantities[article] = existing + qty;
            }
            else
            {
                quantities[article] = qty;
            }
        }
        return new StockResult(quantities, warnings);
    }

    /// <summary>
    /// Книга читается из своего потока целиком в память: если файл битый, поток всё равно закроется,
    /// и временный файл можно будет удалить (иначе Windows держит его занятым).
    /// </summary>
    private static XLWorkbook Open(string path)
    {
        using var stream = File.OpenRead(path);
        var memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;
        return new XLWorkbook(memory);
    }

    /// <summary>Все строки листа значениями: число как double, текст как string, пустая ячейка как null.</summary>
    private static List<object?[]> ReadRows(IXLWorksheet sheet)
    {
        var lastRow = sheet.LastRowUsed()?.RowNumber() ?? 0;
        var lastCol = sheet.LastColumnUsed()?.ColumnNumber() ?? 0;
        var rows = new List<object?[]>(lastRow);
        for (var r = 1; r <= lastRow; r++)
        {
            var values = new object?[lastCol];
            for (var c = 1; c <= lastCol; c++)
            {
                var cell = sheet.Cell(r, c);
                // Как openpyxl с data_only=True: для формулы берём сохранённое в файле значение.
                var value = cell.HasFormula ? cell.CachedValue : cell.Value;
                values[c - 1] = value.Type switch
                {
                    XLDataType.Number => value.GetNumber(),
                    XLDataType.Text => value.GetText(),
                    XLDataType.Boolean => value.GetBoolean().ToString(),
                    XLDataType.DateTime => value.GetDateTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
                    _ => null,
                };
            }
            rows.Add(values);
        }
        return rows;
    }

    private static int? FindHeaderRow(List<object?[]> rows, string[] required)
    {
        for (var i = 0; i < Math.Min(HeaderSearchRows, rows.Count); i++)
        {
            var cells = rows[i].Where(c => c is not null).Select(c => Text(c).ToLowerInvariant()).ToList();
            if (required.All(h => cells.Any(c => c.Contains(h))))
            {
                return i;
            }
        }
        return null;
    }

    private static int? FindCol(object?[] row, string label)
    {
        for (var i = 0; i < row.Length; i++)
        {
            if (row[i] is not null && Text(row[i]).ToLowerInvariant().Contains(label))
            {
                return i;
            }
        }
        return null;
    }

    private static int? PhotoColumn(object?[] header) => OrPython(FindCol(header, "фото"), () => 1);

    /// <summary>
    /// 1С выгружает товар блоком: «Фото», «Описание» и «pH» объединены на все строки его фасовок,
    /// а название обычно стоит в первой строке блока. Иногда оно оказывается во второй строке, и тогда
    /// первая фасовка выглядит как продолжение предыдущего товара: в прайсе с образцом так было
    /// у «GLASS GLOSS» и «MOTOR SHINE». В Django-версии такая фасовка молча доставалась чужому товару
    /// или терялась вместе с описанием и фото. Поднимаем название в первую строку блока до разбора.
    /// </summary>
    private static void LiftNamesToBlockStart(IXLWorksheet sheet, List<object?[]> rows, int nameCol, int[] blockCols, int firstDataRow)
    {
        foreach (var range in sheet.MergedRanges)
        {
            var first = range.RangeAddress.FirstAddress;
            var last = range.RangeAddress.LastAddress;
            int r1 = first.RowNumber - 1, r2 = last.RowNumber - 1, c1 = first.ColumnNumber - 1, c2 = last.ColumnNumber - 1;
            var isProductBlock = r2 > r1 && r1 >= firstDataRow && r2 < rows.Count
                && blockCols.Any(c => c >= c1 && c <= c2)
                && !(c1 <= nameCol && nameCol <= c2);
            if (!isProductBlock || TextAt(rows[r1], nameCol).Length > 0)
            {
                continue;
            }
            for (var r = r1 + 1; r <= r2; r++)
            {
                if (TextAt(rows[r], nameCol).Length > 0)
                {
                    rows[r1][nameCol] = rows[r][nameCol];
                    rows[r][nameCol] = null;
                    break;
                }
            }
        }
    }

    // В Python «find_col(...) or другое» срабатывает и на номер колонки 0, а не только на None.
    // Повторяем это поведение, чтобы разбор совпадал с Django-версией на тех же файлах.
    private static int? OrPython(int? first, Func<int?> fallback) => first is null or 0 ? fallback() : first;

    private static object? At(object?[] row, int? idx) => idx is { } i && i < row.Length ? row[i] : null;

    private static string TextAt(object?[] row, int? idx) => At(row, idx) is { } value ? Text(value) : "";

    /// <summary>Текст ячейки как str() в Python: целое число без «.0», неразрывные пробелы заменены, края обрезаны.</summary>
    private static string Text(object? value)
    {
        var text = value switch
        {
            null => "",
            double d when d == Math.Floor(d) && Math.Abs(d) < 1e15 => ((long)d).ToString(CultureInfo.InvariantCulture),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "",
        };
        return text.Replace(' ', ' ').Trim();
    }

    private static int? AsInt(object? value) => value is double d ? (int)d : null;

    // Через строковое представление, как Decimal(str(x)) в Python: 1890.5 остаётся 1890.5 без хвоста двоичной дроби.
    private static decimal ToDecimal(double value) =>
        decimal.Parse(value.ToString("R", CultureInfo.InvariantCulture), NumberStyles.Float, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"^(\d+(?:[.,]\d+)?)\s*(.*)$")]
    private static partial Regex PackRegex();
}

/// <summary>Файл не похож на выгрузку 1С: текст ошибки показывается менеджеру как есть.</summary>
public class ImportFormatException(string message) : Exception(message);
