namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Адрес сайта для ссылок в письмах, которые уходят не в ответ на запрос (например, «товар снова
/// в наличии» из фоновой проверки). Берётся из настройки Site:Url, а если её нет, из первого запроса
/// к сайту: за Caddy это уже правильный https://домен.
/// </summary>
public sealed class SiteAddress(IConfiguration configuration)
{
    private string? _fromRequest;

    public string? Url => configuration["Site:Url"]?.TrimEnd('/') is { Length: > 0 } configured ? configured : _fromRequest;

    public void Remember(HttpRequest request) => _fromRequest ??= $"{request.Scheme}://{request.Host}";
}

public static class SiteAddressExtensions
{
    public static IApplicationBuilder UseSiteAddress(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        context.RequestServices.GetRequiredService<SiteAddress>().Remember(context.Request);
        await next();
    });
}
