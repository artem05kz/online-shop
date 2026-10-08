using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services.Email;

namespace OnlineShop.Web.Services;

/// <summary>
/// «Товар снова в наличии». У каждого товара в избранном запомнено, был ли он в наличии при прошлой
/// проверке. Если не было, а теперь есть (загрузили остатки из 1С, поправили вручную, отменили заказ),
/// покупателю уходит одно письмо со всеми такими товарами. Проверка по расписанию ловит любое
/// изменение остатка, откуда бы оно ни пришло.
/// </summary>
public class RestockNotifier(ShopDbContext db, EmailQueue queue, SiteAddress site, ILogger<RestockNotifier> logger)
{
    public async Task<int> CheckAsync(CancellationToken ct = default)
    {
        var rows = await db.Favorites
            .Select(f => new
            {
                Favorite = f,
                InStock = f.Product.IsActive && f.Product.Variants.Any(v => v.IsActive && v.StockQty > v.ReservedQty),
                f.Product.Name,
                f.Product.Slug,
                Email = f.User.EmailConfirmed ? f.User.Email : null,
            })
            .ToListAsync(ct);

        var letters = new Dictionary<string, List<(string Name, string Slug)>>();
        foreach (var row in rows)
        {
            // Письмо только при переходе «не было в наличии» в «есть». Если состояние ещё не известно
            // (избранное старше этой функции), просто запоминаем его.
            if (row.Favorite.WasInStock is false && row.InStock && row.Email is { } email)
            {
                if (!letters.TryGetValue(email, out var list))
                {
                    letters[email] = list = [];
                }
                list.Add((row.Name, row.Slug));
            }
            row.Favorite.WasInStock = row.InStock;
        }
        if (letters.Count > 0 && site.Url is null)
        {
            // Ничего не сохраняем: при следующей проверке, когда адрес станет известен, письма уйдут.
            logger.LogWarning("Адрес сайта неизвестен (Site:Url), письма о поступлении отложены до первого запроса к сайту");
            return 0;
        }
        await db.SaveChangesAsync(ct);
        foreach (var (email, products) in letters)
        {
            queue.Enqueue(MailTemplates.BackInStock(email, products.Select(p => (p.Name, $"{site.Url}/product/{Uri.EscapeDataString(p.Slug)}")).ToList()));
        }
        return letters.Count;
    }
}

/// <summary>Проверка поступлений раз в несколько минут.</summary>
public sealed class RestockWatcher(IServiceScopeFactory scopes, IConfiguration configuration, ILogger<RestockWatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var minutes = configuration.GetValue("Restock:IntervalMinutes", 5);
        if (minutes <= 0)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(minutes));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<RestockNotifier>().CheckAsync(stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(e, "Не удалось проверить поступления товаров из избранного");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
