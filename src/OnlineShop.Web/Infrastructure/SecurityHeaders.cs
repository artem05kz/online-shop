using System.Security.Cryptography;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Заголовки, которые просят браузер дополнительно защищать покупателя. Главный из них
/// Content-Security-Policy: даже если на страницу как-то попадёт чужой скрипт, браузер
/// его не выполнит. Разрешены только скрипты с нашего сайта, карт (unpkg для Leaflet, 2ГИС)
/// и встроенные блоки с одноразовой меткой (nonce) этого запроса.
/// </summary>
public static class SecurityHeaders
{
    private const string NonceKey = "csp-nonce";

    /// <summary>Метка для встроенного &lt;script&gt;: &lt;script nonce="@Context.CspNonce()"&gt;.</summary>
    public static string CspNonce(this HttpContext context) => (string)context.Items[NonceKey]!;

    public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        context.Items[NonceKey] = nonce;

        var headers = context.Response.Headers;
        headers.ContentSecurityPolicy = string.Join("; ",
            "default-src 'self'",
            $"script-src 'self' 'nonce-{nonce}' https://unpkg.com https://mapgl.2gis.com https://*.2gis.com",
            // Атрибуты style в разметке панели и стили, которые ставят карты: без unsafe-inline они сломаются.
            // Стили не выполняют код, поэтому для защиты от чужих скриптов это не важно.
            "style-src 'self' 'unsafe-inline' https://unpkg.com https://*.2gis.com",
            // Фото и плитки карт с любых https-адресов: картинка скрипт не запустит.
            "img-src 'self' data: blob: https:",
            "font-src 'self' data: https://*.2gis.com",
            "connect-src 'self' https://*.2gis.com https://*.2gis.ru https://nominatim.openstreetmap.org",
            "worker-src 'self' blob:",
            "child-src 'self' blob:",
            "object-src 'none'",
            "base-uri 'self'",
            "form-action 'self'",
            // Не встраивать сайт в чужие страницы: иначе покупателя можно обманом заставить нажать кнопку.
            "frame-ancestors 'self'");
        headers.XContentTypeOptions = "nosniff";
        headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        // Геолокация нужна карте адреса («где я»), камера и микрофон сайту не нужны.
        headers["Permissions-Policy"] = "geolocation=(self), camera=(), microphone=(), payment=()";
        await next();
    });
}
