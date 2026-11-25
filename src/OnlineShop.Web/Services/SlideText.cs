using System.Globalization;
using System.Text.RegularExpressions;

namespace OnlineShop.Web.Services;

/// <summary>
/// Надписи слайда с товаром по карточке товара (25.11.2026): менеджер выбирает товар, а заголовок,
/// рукописное слово и короткий текст подставляются сами (их можно поправить).
/// Тот же расчёт делает wwwroot/js/admin_slide_edit.js, когда товар выбирают в панели.
/// </summary>
public static partial class SlideText
{
    [GeneratedRegex("[«\"“„]([^»\"”]+)[»\"”]")]
    private static partial Regex Quoted();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();

    /// <summary>«Очиститель стёкол "GLASS GLOSS"» → «Очиститель стёкол GLASS GLOSS».</summary>
    public static string Title(string name) =>
        Spaces().Replace(name.Replace("«", "").Replace("»", "").Replace("\"", "").Replace("“", "").Replace("”", "").Replace("„", ""), " ").Trim();

    /// <summary>Название линейки из кавычек, как пишут от руки: «GLASS GLOSS» → «Glass Gloss». Без кавычек — пусто.</summary>
    public static string Script(string name)
    {
        var match = Quoted().Match(name);
        if (!match.Success)
        {
            return "";
        }
        var words = match.Groups[1].Value.Trim().ToLower(CultureInfo.GetCultureInfo("ru-RU"));
        return CultureInfo.GetCultureInfo("ru-RU").TextInfo.ToTitleCase(words);
    }

    /// <summary>Первая фраза описания, не длиннее 120 знаков.</summary>
    public static string Text(string description)
    {
        var text = Spaces().Replace(description, " ").Trim();
        if (text.Length == 0)
        {
            return "";
        }
        var end = text.IndexOfAny(['.', '!', '?']);
        var sentence = end > 0 ? text[..(end + 1)] : text;
        return sentence.Length <= 120 ? sentence : sentence[..117].TrimEnd(' ', ',', ';') + "…";
    }
}
