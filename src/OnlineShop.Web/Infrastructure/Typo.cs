using System.Text.RegularExpressions;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Экранная типографика: короткие предлоги, союзы и частицы не остаются висеть в конце строки,
/// число не отрывается от единицы («5 кг», «от 180 ₸»). Пробел после них заменяется неразрывным (U+00A0).
/// Используется для названий разделов и товаров, заголовков и коротких текстов витрины.
/// </summary>
public static partial class Typo
{
    private const char Nbsp = ' ';

    // Слова до трёх букв, которые читаются только вместе со следующим словом.
    [GeneratedRegex(@"(?<=^|[\s(«""])(в|во|к|ко|с|со|у|о|об|обо|от|до|по|на|за|из|изо|без|для|при|про|над|под|меж|и|а|но|не|ни|да|или|как|что|это|то|же)\s+",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ShortWord();

    // Частицы, которые читаются с предыдущим словом: «он же», «будет ли».
    [GeneratedRegex(@"\s+(ли|ль|же|ж|бы|б)(?=$|[\s.,!?;:)»""])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Particle();

    // Число и единица или знак после него: 5 кг, 500 мл, 20 л, 110 ₸, 35 авто.
    [GeneratedRegex(@"(\d)\s+(?=(кг|г|л|мл|шт|м|см|мм|₸|%|авто)(?=$|[\s.,!?;:)»""]))", RegexOptions.CultureInvariant)]
    private static partial Regex NumberUnit();

    // Прямые кавычки из 1С ("TOP WAX") -> ёлочки («TOP WAX»).
    [GeneratedRegex(@"""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex StraightQuotes();

    [GeneratedRegex(@" {2,}", RegexOptions.CultureInvariant)]
    private static partial Regex ManySpaces();

    public static string Text(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var s = ManySpaces().Replace(text.Trim(), " ");
        s = StraightQuotes().Replace(s, m => "«" + m.Groups[1].Value.Trim() + "»");
        s = ShortWord().Replace(s, m => m.Groups[1].Value + Nbsp);
        // второй проход: «и в», «а с» — подряд идущие короткие слова
        s = ShortWord().Replace(s, m => m.Groups[1].Value + Nbsp);
        s = Particle().Replace(s, m => Nbsp + m.Groups[1].Value);
        s = NumberUnit().Replace(s, m => m.Groups[1].Value + Nbsp);
        return s;
    }
}
