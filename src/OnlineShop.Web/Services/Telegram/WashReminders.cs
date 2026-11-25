using System.Text;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services.Telegram;

/// <summary>
/// Напоминания администраторам мойки в Telegram (CRM, шаг 2, 25.11):
/// 1) запись с сайта ждёт подтверждения уже 30 минут — бот пишет ещё раз, с теми же кнопками;
/// 2) вечером, начиная с 18:00, — список записей на завтра со ссылками WhatsApp с готовым текстом,
///    чтобы напомнить клиентам. Каждая запись попадает в напоминание один раз (отметки в базе).
/// </summary>
public class WashReminders(ShopDbContext db, TelegramNotifier notifier)
{
    public static readonly TimeSpan WaitLimit = TimeSpan.FromMinutes(30);
    public static readonly TimeOnly EveningAt = new(18, 0);

    public record Result(int Waiting, int Tomorrow);

    /// <param name="utcNow">Сейчас, UTC.</param>
    /// <param name="siteUrl">Адрес сайта для ссылок на записи; null — без ссылок.</param>
    public async Task<Result> RunAsync(DateTime utcNow, string? siteUrl, CancellationToken ct)
    {
        var now = Format.ToLocal(utcNow);
        var waiting = await RemindWaitingAsync(utcNow, now, siteUrl, ct);
        var tomorrow = TimeOnly.FromDateTime(now) >= EveningAt ? await RemindTomorrowAsync(now, siteUrl, ct) : 0;
        return new Result(waiting, tomorrow);
    }

    private async Task<int> RemindWaitingAsync(DateTime utcNow, DateTime now, string? siteUrl, CancellationToken ct)
    {
        var createdBefore = utcNow - WaitLimit;
        var list = await db.WashBookings
            .Where(b => b.Status == WashBookingStatus.New && !b.WaitReminderSent && b.CreatedAt <= createdBefore && b.StartsAt > now)
            .OrderBy(b => b.StartsAt).ToListAsync(ct);
        foreach (var b in list)
        {
            var text = new StringBuilder();
            text.Append("⏰ <b>Запись ждёт подтверждения уже 30 минут</b>\n");
            text.Append("<b>").Append(BookingMessage.When(b)).Append("</b>\n");
            text.Append(OrderMessage.H(b.ServiceName)).Append('\n');
            text.Append("Клиент: ").Append(OrderMessage.H(b.Name)).Append(", ").Append(PhoneNumbers.Display(b.Phone)).Append('\n');
            text.Append("\nПозвоните клиенту и нажмите «Подтвердить».");
            if (siteUrl is not null)
            {
                text.Append("\nОткрыть запись: ").Append(OrderMessage.H($"{siteUrl}/admin/wash/{b.Id}"));
            }
            await notifier.SendToAllAsync(text.ToString(), ct, TelegramKeyboards.Booking(b.Id));
            b.WaitReminderSent = true;
            await db.SaveChangesAsync(ct);
        }
        return list.Count;
    }

    private async Task<int> RemindTomorrowAsync(DateTime now, string? siteUrl, CancellationToken ct)
    {
        var from = now.Date.AddDays(1);
        var to = from.AddDays(1);
        var list = await db.WashBookings
            .Where(b => (b.Status == WashBookingStatus.New || b.Status == WashBookingStatus.Confirmed) && !b.ClientReminderSent
                && b.StartsAt >= from && b.StartsAt < to)
            .OrderBy(b => b.StartsAt).ToListAsync(ct);
        if (list.Count == 0)
        {
            return 0;
        }
        var text = new StringBuilder();
        text.Append("📅 <b>Завтра на мойке: ").Append(list.Count).Append(' ')
            .Append(Plural.Choose(list.Count, "запись", "записи", "записей")).Append("</b>\n");
        text.Append("Напомните клиентам: нажмите «WhatsApp» — текст напоминания уже готов.\n");
        foreach (var b in list)
        {
            text.Append('\n').Append("<b>").Append(b.StartsAt.ToString("HH:mm")).Append("</b> ")
                .Append(OrderMessage.H(b.Name)).Append(", ").Append(PhoneNumbers.Display(b.Phone))
                .Append(" · ").Append(OrderMessage.H(b.ServiceName));
            if (b.Status == WashBookingStatus.New)
            {
                text.Append(" · <i>ещё не подтверждена</i>");
            }
            text.Append("\n<a href=\"").Append(OrderMessage.H(BookingMessage.WhatsAppReminder(b))).Append("\">WhatsApp</a>");
            if (siteUrl is not null)
            {
                text.Append(" · <a href=\"").Append(OrderMessage.H($"{siteUrl}/admin/wash/{b.Id}")).Append("\">запись</a>");
            }
            text.Append('\n');
            b.ClientReminderSent = true;
        }
        await notifier.SendToAllAsync(text.ToString(), ct);
        await db.SaveChangesAsync(ct);
        return list.Count;
    }
}

/// <summary>Раз в минуту проверяет, не пора ли напомнить. Без токена Telegram не работает.</summary>
public sealed class WashReminderWorker(
    TelegramOptions options, IServiceScopeFactory scopes, SiteAddress site, ILogger<WashReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.IsConfigured)
        {
            return;
        }
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<WashReminders>().RunAsync(DateTime.UtcNow, site.Url, stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    logger.LogWarning(e, "Не удалось отправить напоминания о мойке");
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }
}
