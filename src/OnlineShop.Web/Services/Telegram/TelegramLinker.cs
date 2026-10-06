using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Web.Services.Telegram;

/// <summary>
/// Разговор с ботом: человек нажимает «Старт», затем кнопку «Отправить мой номер».
/// Если номер есть в списке панели, чат запоминается и туда приходят заказы.
/// </summary>
public class TelegramLinker(ShopDbContext db, ITelegramApi api)
{
    public const string PanelSection = "«Уведомления в Telegram»";

    public async Task HandleAsync(TelegramUpdate update, CancellationToken ct)
    {
        // Кнопка «отправить номер» есть только в личном чате, в группах бот молчит.
        if (update.Message is not { Chat.Type: "private" } message)
        {
            return;
        }
        var chatId = message.Chat.Id;

        if (message.Contact is { } contact)
        {
            await LinkAsync(chatId, message, contact, ct);
            return;
        }

        if (message.Text?.Trim().StartsWith("/stop", StringComparison.OrdinalIgnoreCase) == true)
        {
            var linked = await db.TelegramRecipients.Where(r => r.ChatId == chatId).ToListAsync(ct);
            foreach (var recipient in linked)
            {
                recipient.ChatId = null;
                recipient.LinkedAt = null;
            }
            await db.SaveChangesAsync(ct);
            await api.SendMessageAsync(chatId,
                "Уведомления о заказах отключены. Чтобы включить снова, отправьте /start.", TelegramKeyboards.Remove, ct);
            return;
        }

        // /start и любой другой текст.
        if (await db.TelegramRecipients.AnyAsync(r => r.ChatId == chatId, ct))
        {
            await api.SendMessageAsync(chatId,
                "Этот чат уже получает уведомления о новых заказах. Отключить: /stop", null, ct);
            return;
        }
        await api.SendMessageAsync(chatId,
            "Бот присылает уведомления о новых заказах магазина GRASS.\n\n" +
            "Нажмите кнопку «Отправить мой номер» внизу. Если номер есть в списке магазина, заказы начнут приходить сюда.",
            TelegramKeyboards.SharePhone, ct);
    }

    private async Task LinkAsync(long chatId, TelegramMessage message, TelegramContact contact, CancellationToken ct)
    {
        // Чужой контакт из записной книжки тоже можно отправить. Принимаем только свой номер,
        // иначе любой подписался бы на заказы, зная телефон менеджера.
        if (contact.UserId is null || contact.UserId != message.From?.Id)
        {
            await api.SendMessageAsync(chatId,
                "Это чужой контакт. Нажмите кнопку «Отправить мой номер», чтобы отправить свой.",
                TelegramKeyboards.SharePhone, ct);
            return;
        }

        var phone = PhoneNumbers.Normalize(contact.PhoneNumber);
        var recipient = phone.Length == 0 ? null : await db.TelegramRecipients.FirstOrDefaultAsync(r => r.Phone == phone, ct);
        if (recipient is null)
        {
            await api.SendMessageAsync(chatId,
                $"Номера {PhoneNumbers.Display(phone)} нет в списке магазина. Попросите менеджера добавить его " +
                $"в панели управления, раздел {PanelSection}, и нажмите кнопку ещё раз.",
                TelegramKeyboards.SharePhone, ct);
            return;
        }

        recipient.ChatId = chatId;
        recipient.LinkedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        var name = recipient.Name.Length > 0 ? $", {OrderMessage.H(recipient.Name)}" : "";
        await api.SendMessageAsync(chatId,
            $"Готово{name}. Сюда будут приходить новые заказы. Отключить: /stop", TelegramKeyboards.Remove, ct);
    }
}
