using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;

namespace OnlineShop.Web.Services;

/// <summary>
/// Раз в сутки удаляет корзины гостей, к которым не возвращались два месяца: кука у них уже истекла,
/// и открыть такую корзину больше некому. Корзины покупателей не трогает.
/// </summary>
public sealed class CartCleanup(IServiceScopeFactory scopes, ILogger<CartCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromDays(1));
        do
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
                var before = DateTime.UtcNow - Cart.GuestLifetime;
                var removed = await db.CartItems
                    .Where(i => i.OwnerKey.StartsWith("g:") && i.UpdatedAt < before)
                    .ExecuteDeleteAsync(stoppingToken);
                if (removed > 0)
                {
                    logger.LogInformation("Удалено позиций из старых корзин гостей: {Count}", removed);
                }
            }
            catch (Exception e) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(e, "Не удалось почистить старые корзины гостей");
            }
        }
        while (await WaitAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitAsync(PeriodicTimer timer, CancellationToken ct)
    {
        try
        {
            return await timer.WaitForNextTickAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }
}
