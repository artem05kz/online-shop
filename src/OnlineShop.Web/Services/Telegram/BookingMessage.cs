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
            text.Append("\nПодтвердите запись клиенту звонком или в WhatsApp.");
        }
        text.Append("\nВсе записи: ").Append(h(adminUrl));
        return text.ToString();
    }

    /// <summary>«чт, 26 ноября, 14:30–15:30».</summary>
    public static string When(WashBooking booking) =>
        booking.StartsAt.ToString("ddd, d MMMM, HH:mm", Russian) + "–" + booking.EndsAt.ToString("HH:mm", Russian);
}
