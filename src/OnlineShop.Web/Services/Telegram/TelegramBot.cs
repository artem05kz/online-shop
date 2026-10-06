namespace OnlineShop.Web.Services.Telegram;

/// <summary>
/// Бот живёт внутри сайта. Сообщения боту забираются запросом getUpdates (long polling): так бот
/// работает и на компьютере разработчика, и на сервере, без публичного адреса для webhook.
/// Параллельно отправляет уведомления из очереди OrderAlerts.
/// </summary>
public sealed class TelegramBot(
    TelegramOptions options, ITelegramApi api, OrderAlerts alerts,
    IServiceScopeFactory scopes, ILogger<TelegramBot> logger) : BackgroundService
{
    private const int PollSeconds = 25;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(15);

    /// <summary>Имя бота для ссылки t.me в панели. null, пока Telegram не ответил.</summary>
    public string? Username { get; private set; }

    /// <summary>Последняя ошибка связи с Telegram понятными словами, null если всё в порядке.</summary>
    public string? Problem { get; private set; }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        options.IsConfigured
            ? Task.WhenAll(PollAsync(stoppingToken), SendAsync(stoppingToken))
            : Task.CompletedTask;

    private async Task PollAsync(CancellationToken ct)
    {
        long offset = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                Username ??= (await api.GetMeAsync(ct)).Username;
                var updates = await api.GetUpdatesAsync(offset, PollSeconds, ct);
                Problem = null;
                foreach (var update in updates)
                {
                    // Сдвиг offset подтверждает Telegram, что сообщение получено: иначе оно придёт снова.
                    offset = update.UpdateId + 1;
                    await HandleAsync(update, ct);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception e)
            {
                Problem = Describe(e);
                logger.LogWarning("Бот Telegram: {Problem} ({Error})", Problem, e.Message);
                await Pause(ct);
            }
        }
    }

    private async Task HandleAsync(TelegramUpdate update, CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            await scope.ServiceProvider.GetRequiredService<TelegramLinker>().HandleAsync(update, ct);
        }
        catch (Exception e) when (!ct.IsCancellationRequested)
        {
            // Одно неудачное сообщение не должно останавливать бота.
            logger.LogWarning(e, "Бот Telegram не смог ответить на сообщение {UpdateId}", update.UpdateId);
        }
    }

    private async Task SendAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var alert in alerts.Reader.ReadAllAsync(ct))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    var report = await scope.ServiceProvider.GetRequiredService<TelegramNotifier>().SendOrderAsync(alert, ct);
                    logger.LogInformation("Заказ №{OrderId} отправлен в Telegram: доставлено {Sent}, ошибок {Failed}",
                        alert.OrderId, report.Sent, report.Failed);
                }
                catch (Exception e) when (!ct.IsCancellationRequested)
                {
                    logger.LogWarning(e, "Не удалось разослать заказ №{OrderId} в Telegram", alert.OrderId);
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
    }

    private static string Describe(Exception e) => e switch
    {
        TelegramException { Code: 401 or 404 } => "Telegram не принял токен бота. Проверьте, что токен скопирован целиком.",
        TelegramException { Code: 409 } => "Этот бот уже запущен в другом месте: вторая копия сайта или webhook.",
        TelegramException t => $"Telegram ответил ошибкой {t.Code}.",
        _ => "Нет связи с Telegram. Бот повторит попытку сам.",
    };

    private static async Task Pause(CancellationToken ct)
    {
        try
        {
            await Task.Delay(RetryDelay, ct);
        }
        catch (OperationCanceledException)
        {
        }
    }
}
