using System.Text;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services.Telegram;

/// <summary>Текст уведомления о новом заказе: всё, чтобы перезвонить покупателю, не открывая панель.</summary>
public static class OrderMessage
{
    // Telegram режет сообщения длиннее 4096 знаков. 40 строк товаров с запасом в него помещаются.
    private const int MaxItems = 40;

    public static string Build(Order order, string adminOrderUrl)
    {
        var text = new StringBuilder();
        text.Append("<b>Новый заказ №").Append(order.Id).Append("</b>\n");
        text.Append(Format.DateTimeText(order.CreatedAt)).Append("\n\n");

        foreach (var item in order.Items.Take(MaxItems))
        {
            var name = item.PackLabel.Length > 0 ? $"{item.ProductName}, {item.PackLabel}" : item.ProductName;
            text.Append(H(name)).Append(": ")
                .Append(item.Quantity).Append(" × ").Append(Format.Money(item.Price))
                .Append(" = ").Append(Format.Money(item.Subtotal)).Append('\n');
        }
        var rest = order.Items.Count - MaxItems;
        if (rest > 0)
        {
            text.Append("и ещё ").Append(rest).Append(' ').Append(Plural.Choose(rest, "позиция", "позиции", "позиций")).Append('\n');
        }
        text.Append("<b>Итого: ").Append(Format.Money(order.Total)).Append("</b>\n\n");

        text.Append("Получатель: ").Append(H(order.ContactName)).Append('\n');
        text.Append("Телефон: ").Append(H(order.ContactPhone)).Append('\n');
        text.Append("Адрес: ").Append(H($"{order.City}, {order.StreetAndDetails}")).Append('\n');
        if (order.Comment.Length > 0)
        {
            text.Append("Комментарий: ").Append(H(order.Comment)).Append('\n');
        }

        // Адрес текстом, а не ссылкой-тегом: адрес вида localhost Telegram в теге не принимает.
        text.Append("\nОткрыть в панели: ").Append(H(adminOrderUrl));
        return text.ToString();
    }

    /// <summary>
    /// Текст покупателя в разметке HTML. Экранируем только три знака, которые Telegram требует:
    /// WebUtility.HtmlEncode превратил бы и часть букв в коды.
    /// </summary>
    public static string H(string value) => value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
