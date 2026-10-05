using System.Globalization;
using Microsoft.AspNetCore.Html;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin;

/// <summary>Числа в формах панели. Менеджер может написать цену и с запятой, и с точкой, и с пробелами.</summary>
public static class AdminNumbers
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    /// <summary>Пустая строка даёт null. Возвращает false, если написано что-то, что не число.</summary>
    public static bool TryDecimal(string? raw, out decimal? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }
        var text = raw.Replace(" ", "").Replace(" ", "").Replace(',', '.');
        if (decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
        {
            value = parsed;
            return true;
        }
        return false;
    }

    public static bool TryInt(string? raw, out int? value)
    {
        value = null;
        if (string.IsNullOrWhiteSpace(raw))
        {
            return true;
        }
        if (int.TryParse(raw.Replace(" ", "").Replace(" ", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
        {
            value = parsed;
            return true;
        }
        return false;
    }

    /// <summary>Как показать число в поле формы: 1890,5 без лишних нулей.</summary>
    public static string Text(decimal? value) => value?.ToString("0.###", Russian) ?? "";
}

public record PagerInfo(int Page, int PageCount, int TotalCount)
{
    public static PagerInfo From<T>(PagedList<T> list) => new(list.Page, list.PageCount, list.TotalCount);
}

public static class AdminList
{
    // Как в Django admin: 100 строк на страницу. Номер страницы в адресе называется «p»:
    // имя «page» в Razor Pages занято под адрес самой страницы и ломает перенаправления.
    public const int PageSize = 100;

    /// <summary>
    /// Заголовок столбца, по которому можно отсортировать: ссылка ставит ?o=ключ, повторный клик
    /// меняет направление (?o=-ключ). Остальные параметры (поиск, фильтры) сохраняются, страница сбрасывается.
    /// </summary>
    public static IHtmlContent SortHeader(HttpRequest request, string key, string label)
    {
        var current = request.Query["o"].ToString();
        var next = current == key ? "-" + key : key;
        var arrow = current == key ? " ▲" : current == "-" + key ? " ▼" : "";
        var pairs = QueryStrings.Except(request.Query, "o", "p").ToList();
        pairs.Add(new("o", next));
        var href = "?" + string.Join("&", pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));
        var css = arrow.Length > 0 ? " class=\"sorted\"" : "";
        return new HtmlString($"<a href=\"{System.Net.WebUtility.HtmlEncode(href)}\"{css}>{System.Net.WebUtility.HtmlEncode(label)}{arrow}</a>");
    }
}
