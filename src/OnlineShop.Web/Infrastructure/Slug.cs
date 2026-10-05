using System.Text;
using System.Text.RegularExpressions;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Ярлыки для адресов страниц. Повторяет django.utils.text.slugify(allow_unicode=True):
/// кириллица сохраняется, поэтому ссылки на категории и товары остаются такими же, как на старом сайте.
/// </summary>
public static partial class Slug
{
    public static string From(string value, int maxLength = int.MaxValue)
    {
        var text = value.Normalize(NormalizationForm.FormKC).ToLowerInvariant();
        text = NotWordRegex().Replace(text, "");
        text = SeparatorRegex().Replace(text, "-").Trim('-', '_');
        return text.Length > maxLength ? text[..maxLength] : text;
    }

    /// <summary>
    /// Первый свободный вариант ярлыка: сам ярлык или ярлык с суффиксом -2, -3 и так далее.
    /// Суффикс помещается в maxLength за счёт обрезки основы.
    /// </summary>
    public static string MakeUnique(string slug, ISet<string> taken, int maxLength)
    {
        if (!taken.Contains(slug))
        {
            return slug;
        }
        for (var n = 2; ; n++)
        {
            var suffix = "-" + n;
            var baseLength = Math.Min(slug.Length, maxLength - suffix.Length);
            var candidate = slug[..baseLength] + suffix;
            if (!taken.Contains(candidate))
            {
                return candidate;
            }
        }
    }

    [GeneratedRegex(@"[^\w\s-]")]
    private static partial Regex NotWordRegex();

    [GeneratedRegex(@"[-\s]+")]
    private static partial Regex SeparatorRegex();
}
