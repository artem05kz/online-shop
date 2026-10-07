namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Ссылки, которые менеджер вписывает руками (например, «Ссылка при нажатии» у баннера). Разрешены
/// только адрес на своём сайте и обычные http/https-ссылки: ссылка вида javascript:... выполнила бы
/// чужой код у каждого покупателя, нажавшего на баннер.
/// </summary>
public static class SafeLinks
{
    public static bool IsAllowed(string? link)
    {
        if (string.IsNullOrWhiteSpace(link))
        {
            return true;
        }
        link = link.Trim();
        // «//сайт» браузер понимает как ссылку на чужой сайт, а «/\» тоже уводит наружу.
        if (link.StartsWith('/'))
        {
            return !link.StartsWith("//") && !link.StartsWith("/\\");
        }
        return Uri.TryCreate(link, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);
    }

    /// <summary>Ссылка для вывода на страницу: неподходящая заменяется на null, и атрибут href не выводится.</summary>
    public static string? OrNull(string? link) => !string.IsNullOrWhiteSpace(link) && IsAllowed(link) ? link.Trim() : null;
}
