using System.Globalization;
using System.Text;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services.Telegram;

/// <summary>Уведомление администратору мойки: когда, что и кому перезвонить — без входа в панель.</summary>
public static class BookingMessage
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    public static string Build(WashBooking booking, string adminUrl, bool cancelled = false)
    {
        var h = OrderMessage.H;
        var text = new StringBuilder();
        text.Append(cancelled ? "<b>Клиент отменил запись на мойку</b>\n" : "<b>Новая запись на мойку</b>\n");
        text.Append(h(booking.WashName)).Append('\n');
        text.Append("<b>").Append(When(booking)).Append("</b>\n");
        text.Append(h(booking.ServiceName)).Append(", около ").Append(booking.Minutes).Append(" мин\n\n");
        text.Append("Клиент: ").Append(h(booking.Name)).Append('\n');
        text.Append("Телефон: ").Append(PhoneNumbers.Display(booking.Phone)).Append('\n');
        if (booking.Car.Length > 0)
        {
            text.Append("Машина: ").Append(h(booking.Car)).Append('\n');
        }
        if (booking.Comment.Length > 0)
        {
            text.Append("Комментарий: ").Append(h(booking.Comment)).Append('\n');
        }
        if (!cancelled)
        {
            text.Append("\nПозвоните клиенту и нажмите «Подтвердить» ниже — запись подтвердится и в панели.");
        }
        text.Append("\nОткрыть запись: ").Append(h(adminUrl));
        return text.ToString();
    }

    /// <summary>
    /// Текст напоминания клиенту накануне (CRM, шаг 2, 25.11). Бот не может написать клиенту сам —
    /// у клиента нет чата с ботом, — поэтому администратор отправляет этот текст в WhatsApp одной кнопкой.
    /// </summary>
    public static string ClientReminder(WashBooking booking)
    {
        var address = CarWashes.Find(booking.WashKey)?.Address;
        return $"Здравствуйте, {booking.Name}! Напоминаем: {booking.StartsAt.ToString("d MMMM", Russian)} в {booking.StartsAt:HH:mm} " +
            $"вы записаны на мойку ({booking.ServiceName.ToLowerInvariant()})" +
            (string.IsNullOrEmpty(address) ? "" : $", адрес: {address}") +
            ". Если планы изменились, напишите нам, пожалуйста.";
    }

    /// <summary>Ссылка WhatsApp с готовым текстом напоминания.</summary>
    public static string WhatsAppReminder(WashBooking booking) =>
        $"https://wa.me/{booking.Phone}?text={Uri.EscapeDataString(ClientReminder(booking))}";

    /// <summary>Сообщение клиенту, когда машину помыли (25.11): администратор отправляет его в WhatsApp одной кнопкой.</summary>
    public static string CarReady(WashBooking booking) =>
        $"Здравствуйте, {booking.Name}! Ваша машина помыта и готова, можете забирать. Спасибо, что выбрали нас!";

    /// <summary>Ссылка WhatsApp с готовым текстом «машина готова».</summary>
    public static string WhatsAppCarReady(WashBooking booking) =>
        $"https://wa.me/{booking.Phone}?text={Uri.EscapeDataString(CarReady(booking))}";

    /// <summary>«чт, 26 ноября, 14:30–15:30».</summary>
    public static string When(WashBooking booking) =>
        booking.StartsAt.ToString("ddd, d MMMM, HH:mm", Russian) + "–" + booking.EndsAt.ToString("HH:mm", Russian);
}
