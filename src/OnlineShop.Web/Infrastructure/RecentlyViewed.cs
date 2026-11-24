namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// «Вы недавно смотрели» (24.11, только на странице товара — с главной убрано по решению пользователя): номера последних открытых товаров лежат в куке браузера, без входа и без базы.
/// Свежие — первыми, не больше <see cref="Max"/>. Кука только для удобства: подделанное содержимое просто отбрасывается.
/// </summary>
public static class RecentlyViewed
{
    public const string CookieName = "recent";
    public const int Max = 12;

    public static List<int> Read(HttpRequest request)
    {
        var raw = request.Cookies[CookieName];
        if (string.IsNullOrEmpty(raw)) return [];
        return raw.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => int.TryParse(x, out var id) ? id : 0)
            .Where(id => id > 0)
            .Distinct()
            .Take(Max)
            .ToList();
    }

    /// <summary>Запомнить открытый товар: он встаёт первым, повтор убирается.</summary>
    public static void Remember(HttpContext context, int productId)
    {
        var ids = Read(context.Request);
        ids.Remove(productId);
        ids.Insert(0, productId);
        context.Response.Cookies.Append(CookieName, string.Join('.', ids.Take(Max)), new CookieOptions
        {
            HttpOnly = true,
            IsEssential = false,
            SameSite = SameSiteMode.Lax,
            Secure = context.Request.IsHttps,
            MaxAge = TimeSpan.FromDays(60),
        });
    }

    /// <summary>Товары в порядке просмотра (из уже загруженных), без исключённого.</summary>
    public static List<T> InOrder<T>(IEnumerable<T> items, Func<T, int> id, IReadOnlyList<int> order, int? except = null) =>
        order.Where(x => x != except)
            .Select(x => items.FirstOrDefault(i => id(i) == x))
            .Where(i => i is not null)
            .Select(i => i!)
            .ToList();
}
