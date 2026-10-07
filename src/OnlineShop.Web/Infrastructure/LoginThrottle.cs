using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Ограничение попыток входа и регистрации с одного адреса. Без него пароль менеджера можно
/// подбирать без остановки, а аккаунты регистрировать роботом тысячами.
/// Блокировку самого аккаунта после нескольких неверных паролей делает Identity (Lockout в Program.cs).
/// </summary>
public static class LoginThrottle
{
    public const string ConfigKey = "Security:AuthAttemptsPerMinute";
    public const int DefaultPerMinute = 10;

    private static readonly string[] GuardedPaths = ["/accounts/login", "/accounts/register"];

    public static IServiceCollection AddLoginThrottle(this IServiceCollection services, IConfiguration configuration)
    {
        var perMinute = configuration.GetValue(ConfigKey, DefaultPerMinute);
        return services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                // Считаем только отправку форм: открывать страницу входа можно сколько угодно.
                var guarded = HttpMethods.IsPost(context.Request.Method)
                    && GuardedPaths.Any(p => context.Request.Path.StartsWithSegments(p, StringComparison.OrdinalIgnoreCase));
                if (!guarded)
                {
                    return RateLimitPartition.GetNoLimiter("free");
                }
                // За Caddy адрес покупателя приходит в заголовке, его подставляет ForwardedHeaders.
                var ip = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                return RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = perMinute,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                });
            });
        });
    }
}
