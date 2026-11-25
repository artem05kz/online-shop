using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services.Telegram;

/// <summary>
/// Кнопки под уведомлением о записи на мойку (CRM, 24.11): «Подтвердить» и «Отменить» прямо в Telegram.
/// Нажимать могут только подключённые получатели уведомлений (их чаты есть в списке «Уведомления в Telegram»).
/// Отмена — в два нажатия. О результате бот пишет всем получателям, чтобы два человека не звонили одному клиенту.
/// </summary>
public class WashTelegramActions(ShopDbContext db, ITelegramApi api, TelegramNotifier notifier)
{
    public async Task HandleAsync(TelegramCallbackQuery query, CancellationToken ct)
    {
        var parts = (query.Data ?? "").Split(':');
        if (parts is not ["wash", var action, var idText] || !int.TryParse(idText, out var id) || query.Message is not { } message)
        {
            await api.AnswerCallbackAsync(query.Id, "", ct);
            return;
        }
        var chatId = message.Chat.Id;
        var who = await db.TelegramRecipients.Where(r => r.ChatId == chatId).Select(r => r.Name).FirstOrDefaultAsync(ct);
        if (who is null)
        {
            await api.AnswerCallbackAsync(query.Id, "Эта кнопка только для администраторов мойки.", ct);
            return;
        }
        if (who.Length == 0)
        {
            who = query.From.FirstName ?? "администратор";
        }

        var booking = await db.WashBookings.FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null)
        {
            await api.AnswerCallbackAsync(query.Id, "Запись не найдена.", ct);
            await api.EditReplyMarkupAsync(chatId, message.MessageId, null, ct);
            return;
        }
        var label = $"{booking.Name}, {BookingMessage.When(booking)}";

        switch (action)
        {
            case "ask":
                await api.EditReplyMarkupAsync(chatId, message.MessageId, TelegramKeyboards.BookingCancelAsk(id), ct);
                await api.AnswerCallbackAsync(query.Id, "Точно отменить? Нажмите ещё раз.", ct);
                return;
            case "back":
                await api.EditReplyMarkupAsync(chatId, message.MessageId,
                    booking.Status == WashBookingStatus.New ? TelegramKeyboards.Booking(id) : null, ct);
                await api.AnswerCallbackAsync(query.Id, "", ct);
                return;
            case "ok" when booking.Status == WashBookingStatus.New:
                booking.Status = WashBookingStatus.Confirmed;
                booking.ConfirmedBy = who;
                await db.SaveChangesAsync(ct);
                await api.EditReplyMarkupAsync(chatId, message.MessageId, null, ct);
                await api.AnswerCallbackAsync(query.Id, "Запись подтверждена.", ct);
                await notifier.SendToAllAsync($"✅ <b>Подтверждена</b>: {OrderMessage.H(label)}\nПодтвердил(а): {OrderMessage.H(who)}", ct);
                return;
            case "cancel" when booking.Status is WashBookingStatus.New or WashBookingStatus.Confirmed:
                booking.Status = WashBookingStatus.Cancelled;
                await db.SaveChangesAsync(ct);
                await api.EditReplyMarkupAsync(chatId, message.MessageId, null, ct);
                await api.AnswerCallbackAsync(query.Id, "Запись отменена, время освободилось.", ct);
                await notifier.SendToAllAsync($"❌ <b>Отменена</b>: {OrderMessage.H(label)}\nОтменил(а): {OrderMessage.H(who)}", ct);
                return;
            default:
                // Уже обработали — здесь или другой администратор.
                await api.EditReplyMarkupAsync(chatId, message.MessageId, null, ct);
                await api.AnswerCallbackAsync(query.Id, $"Уже сделано: «{booking.Status.Text()}».", ct);
                return;
        }
    }
}
