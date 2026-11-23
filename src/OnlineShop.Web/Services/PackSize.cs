using System.Globalization;
using System.Text.RegularExpressions;

namespace OnlineShop.Web.Services;

/// <summary>
/// Группы фасовок для быстрых кнопок на странице раздела: «Триггер 500 мл», «1 кг», «5–6 кг», «20–26 кг».
/// Фасовки приходят из 1С строкой («5,5 кг», «500мл триггер», «23»), поэтому разбираются здесь, а не в базе.
/// </summary>
public static partial class PackSize
{
    public const string Spray = "spray";
    public const string Small = "1";
    public const string Medium = "5";
    public const string Large = "20";

    /// <summary>Порядок кнопок: от маленькой упаковки к большой.</summary>
    public static readonly IReadOnlyList<string> Order = [Spray, Small, Medium, Large];

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(мл|кг|г|л)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Amount();

    public readonly record struct Parsed(string Group, decimal Number, string Unit);

    /// <summary>Группа фасовки или null, если строку не разобрать.</summary>
    public static Parsed? Parse(string? label)
    {
        var m = Amount().Match(label ?? "");
        if (!m.Success)
        {
            return null;
        }
        var number = decimal.Parse(m.Groups[1].Value.Replace(',', '.'), CultureInfo.InvariantCulture);
        var unit = m.Groups[2].Value.ToLowerInvariant();
        // «23» без единицы в 1С — это канистра в килограммах.
        if (unit.Length == 0) unit = "кг";
        if (unit == "мл") return new Parsed(Spray, number, unit);
        if (unit == "г") { number /= 1000; unit = "кг"; }
        var group = number < 2 ? Small : number < 10 ? Medium : Large;
        return new Parsed(group, number, unit);
    }

    /// <summary>Подпись кнопки по фасовкам, которые реально есть в разделе: «5–6 кг», «1 кг, 1 л», «Триггер 500 мл».</summary>
    public static string Label(string group, IEnumerable<Parsed> packs)
    {
        var list = packs.Where(p => p.Group == group).ToList();
        if (list.Count == 0) return "";
        if (group == Spray)
        {
            return $"Триггер {Num(list.Min(p => p.Number))} мл";
        }
        var min = list.Min(p => p.Number);
        var max = list.Max(p => p.Number);
        var units = string.Join("/", list.Select(p => p.Unit).Distinct().OrderBy(u => u == "кг" ? 0 : 1));
        var range = min == max ? Num(min) : $"{Num(Math.Floor(min))}–{Num(Math.Ceiling(max))}";
        return $"{range} {units}";
    }

    private static string Num(decimal n) => n.ToString("0.#", CultureInfo.GetCultureInfo("ru-RU"));

    /// <summary>Цена за 1 кг или 1 л: чтобы было видно, что канистра выгоднее. Триггер 500 мл считается в литрах.</summary>
    public readonly record struct UnitPrice(decimal Value, string Unit);

    public static UnitPrice? PerUnit(string? label, decimal? price)
    {
        if (price is not { } p || Parse(label) is not { } pack || pack.Number <= 0)
        {
            return null;
        }
        var (amount, unit) = pack.Unit == "мл" ? (pack.Number / 1000m, "л") : (pack.Number, pack.Unit);
        return new UnitPrice(Math.Round(p / amount), unit);
    }
}
