using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;

namespace OnlineShop.Web.Services.Telegram;

/// <summary>
/// Что отправить в Telegram: новый заказ или запись на мойку (<see cref="BookingId"/>, тогда OrderId = 0).
/// Адрес сайта берётся из запроса покупателя.
/// </summary>
public record OrderAlert(int OrderId, string SiteUrl, int BookingId = 0, bool BookingCancelled = false);

/// <summary>
/// Очередь уведомлений. Оформление заказа только кладёт в неё номер и сразу отвечает покупателю:
/// медленный или недоступный Telegram не должен задерживать или ломать оформление.
/// </summary>
public sealed class OrderAlerts(TelegramOptions options)
{
    private readonly Channel<OrderAlert> _queue = Channel.CreateUnbounded<OrderAlert>();

    public ChannelReader<OrderAlert> Reader => _queue.Reader;

    public void Enqueue(int orderId, string siteUrl)
    {
        // Без токена бот не запущен, и очередь только росла бы.
        if (options.IsConfigured)
        {
            _queue.Writer.TryWrite(new OrderAlert(orderId, siteUrl.TrimEnd('/')));
        }
    }

    /// <summary>Новая запись на мойку или её отмена клиентом.</summary>
    public void EnqueueBooking(int bookingId, string siteUrl, bool cancelled = false)
    {
        if (options.IsConfigured)
        {
            _queue.Writer.TryWrite(new OrderAlert(0, siteUrl.TrimEnd('/'), bookingId, cancelled));
        }
    }
}

public record SendReport(int Sent, int Failed, int Unlinked);

/// <summary>Рассылка во все подключённые чаты из списка.</summary>
public class TelegramNotifier(ShopDbContext db, ITelegramApi api, ILogger<TelegramNotifier> logger)
{
    public async Task<SendReport> SendOrderAsync(OrderAlert alert, CancellationToken ct)
    {
        var order = await db.Orders.Include(o => o.Items).AsNoTracking().FirstOrDefaultAsync(o => o.Id == alert.OrderId, ct);
        if (order is null)
        {
            return new SendReport(0, 0, 0);
        }
        return await SendToAllAsync(OrderMessage.Build(order, $"{alert.SiteUrl}/admin/orders/{order.Id}"), ct);
    }

    public async Task<SendReport> SendBookingAsync(OrderAlert alert, CancellationToken ct)
    {
        var booking = await db.WashBookings.AsNoTracking().FirstOrDefaultAsync(b => b.Id == alert.BookingId, ct);
        if (booking is null)
        {
            return new SendReport(0, 0, 0);
        }
        // Под новой записью — кнопки «Подтвердить / Отменить» (CRM, 24.11). Ссылка ведёт прямо в карточку записи.
        var markup = alert.BookingCancelled || booking.Status != Models.WashBookingStatus.New ? null : TelegramKeyboards.Booking(booking.Id);
        return await SendToAllAsync(BookingMessage.Build(booking, $"{alert.SiteUrl}/admin/wash/{booking.Id}", alert.BookingCancelled), ct, markup);
    }

    public async Task<SendReport> SendToAllAsync(string html, CancellationToken ct, object? markup = null)
    {
        int sent = 0, failed = 0, unlinked = 0;
        var recipients = await db.TelegramRecipients.Where(r => r.ChatId != null).ToListAsync(ct);
        foreach (var recipient in recipients)
        {
            try
            {
                await api.SendMessageAsync(recipient.ChatId!.Value, html, markup, ct);
                sent++;
            }
            catch (TelegramException e) when (e.Code is 400 or 403)
            {
                // 403: человек заблокировал бота, 400: чат удалён. Писать туда бесполезно, номер
                // остаётся в списке и снова подключится, когда человек нажмёт «Старт».
                logger.LogWarning("Чат получателя {Id} больше недоступен: {Error}", recipient.Id, e.Message);
                recipient.ChatId = null;
                recipient.LinkedAt = null;
                unlinked++;
            }
            catch (Exception e) when (e is not OperationCanceledException || !ct.IsCancellationRequested)
            {
                logger.LogWarning(e, "Не удалось отправить уведомление получателю {Id}", recipient.Id);
                failed++;
            }
        }
        if (unlinked > 0)
        {
            await db.SaveChangesAsync(ct);
        }
        return new SendReport(sent, failed, unlinked);
    }
}
