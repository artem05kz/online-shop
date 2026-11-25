using System.Net;
using System.Text;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services.Email;

/// <summary>
/// Тексты писем. Каждое письмо в двух видах: HTML для почтовых программ и простой текст для тех,
/// кто картинки и оформление отключил. Стили прямо в тегах: почтовые программы не читают внешние стили.
/// </summary>
public static class MailTemplates
{
    public const string CodeLifetime = "5 минут";

    public static EmailMessage ConfirmCode(string to, string code) => Code(to,
        "Код подтверждения почты",
        "Чтобы подтвердить почту на сайте IFOAM Актау, введите код:",
        code,
        "Если вы не регистрировались на нашем сайте, просто удалите это письмо.");

    public static EmailMessage ResetCode(string to, string code) => Code(to,
        "Код для смены пароля",
        "Вы попросили сменить пароль на сайте IFOAM Актау. Введите код на странице смены пароля:",
        code,
        "Если вы не просили сменить пароль, удалите это письмо: без кода пароль никто не поменяет.");

    public static EmailMessage OrderPlaced(string to, Order order, string orderUrl)
    {
        var html = new StringBuilder();
        var intro = order.IsPickup
            ? $"Спасибо! Ваш заказ №{order.Id} принят. Мы позвоним, когда он будет готов к выдаче."
            : $"Спасибо! Ваш заказ №{order.Id} принят. Менеджер позвонит, чтобы уточнить время доставки.";
        var where = order.IsPickup
            ? $"Самовывоз из магазина: {order.City}, {order.StreetAndDetails}"
            : $"Доставка: {order.City}, {order.StreetAndDetails}";
        var payment = order.IsPickup ? "Оплата наличными или картой в магазине." : "Оплата наличными или картой курьеру при получении.";
        html.Append(P(intro));
        html.Append(ItemsTable(order));
        html.Append(P(where));
        html.Append(P(payment));
        html.Append(Button("Открыть заказ", orderUrl));

        var text = new StringBuilder();
        text.AppendLine(intro);
        text.AppendLine();
        foreach (var item in order.Items)
        {
            text.AppendLine($"{ItemName(item)}: {item.Quantity} × {Format.Money(item.Price)} = {Format.Money(item.Subtotal)}");
        }
        text.AppendLine($"Итого: {Format.Money(order.Total)}");
        text.AppendLine();
        text.AppendLine(where);
        text.AppendLine(payment);
        text.AppendLine($"Заказ на сайте: {orderUrl}");
        return Wrap(to, $"Заказ №{order.Id} принят", html.ToString(), text.ToString());
    }

    /// <summary>Письмо о новом статусе. null для статусов, о которых не пишем (например, «Новый»).</summary>
    public static EmailMessage? StatusChanged(string to, Order order, string orderUrl)
    {
        var line = order.Status switch
        {
            OrderStatus.Confirmed => "Менеджер подтвердил ваш заказ. Скоро он будет передан в доставку.",
            OrderStatus.Delivering when order.IsPickup => $"Заказ собран и ждёт вас в магазине: {order.City}, {order.StreetAndDetails}.",
            OrderStatus.Delivering => "Заказ передан курьеру и едет к вам. Оплатить можно наличными или картой курьеру.",
            OrderStatus.Completed => "Заказ выполнен. Спасибо за покупку!",
            OrderStatus.Canceled => "Заказ отменён. Если это ошибка, позвоните нам, и мы всё исправим.",
            _ => null,
        };
        if (line is null)
        {
            return null;
        }
        var subject = $"Заказ №{order.Id}: {order.StatusText(order.Status).ToLowerInvariant()}";
        var html = P(line) + ItemsTable(order) + Button("Открыть заказ", orderUrl);
        var text = $"{line}\n\nИтого: {Format.Money(order.Total)}\nЗаказ на сайте: {orderUrl}\n";
        return Wrap(to, subject, html, text);
    }

    public static EmailMessage BackInStock(string to, IReadOnlyList<(string Name, string Url)> products)
    {
        var intro = products.Count == 1
            ? "Товар из вашего избранного снова в наличии:"
            : "Товары из вашего избранного снова в наличии:";
        var html = new StringBuilder(P(intro));
        html.Append("<ul style=\"padding-left:20px;margin:0 0 12px;\">");
        foreach (var (name, url) in products)
        {
            html.Append($"<li style=\"margin-bottom:6px;\"><a href=\"{H(url)}\" style=\"color:#4d8f21;\">{H(name)}</a></li>");
        }
        html.Append("</ul>");
        html.Append(P("Остатки меняются быстро, заказывайте, пока есть."));
        var text = intro + "\n" + string.Join("\n", products.Select(p => $"{p.Name}: {p.Url}")) + "\n";
        return Wrap(to, products.Count == 1 ? $"Снова в наличии: {products[0].Name}" : "Товары из избранного снова в наличии", html.ToString(), text);
    }

    private static EmailMessage Code(string to, string subject, string intro, string code, string footer)
    {
        var html = P(intro)
            + $"<p style=\"font-size:32px;font-weight:bold;letter-spacing:6px;margin:16px 0;\">{H(code)}</p>"
            + P($"Код действует {CodeLifetime}.")
            + P(footer);
        var text = $"{intro}\n\n{code}\n\nКод действует {CodeLifetime}.\n{footer}\n";
        return Wrap(to, subject, html, text);
    }

    private static EmailMessage Wrap(string to, string subject, string body, string text) => new(to, subject,
        "<div style=\"font-family:Arial,sans-serif;font-size:16px;line-height:1.5;color:#16181a;max-width:600px;\">"
        + "<p style=\"font-size:20px;font-weight:bold;color:#0f0f0f;margin:0 0 16px;\">IFOAM Актау</p>"
        + body
        + "<p style=\"color:#7b838c;font-size:13px;margin-top:24px;\">Письмо отправлено автоматически, отвечать на него не нужно.</p>"
        + "</div>",
        text + "\nIFOAM Актау\n");

    private static string ItemsTable(Order order)
    {
        var rows = new StringBuilder();
        foreach (var item in order.Items)
        {
            rows.Append("<tr>")
                .Append($"<td style=\"padding:4px 12px 4px 0;\">{H(ItemName(item))}</td>")
                .Append($"<td style=\"padding:4px 12px 4px 0;white-space:nowrap;\">{item.Quantity} шт.</td>")
                .Append($"<td style=\"padding:4px 0;text-align:right;white-space:nowrap;\">{H(Format.Money(item.Subtotal))}</td>")
                .Append("</tr>");
        }
        rows.Append($"<tr><td colspan=\"2\" style=\"padding:8px 0;font-weight:bold;\">Итого</td>"
            + $"<td style=\"padding:8px 0;text-align:right;font-weight:bold;white-space:nowrap;\">{H(Format.Money(order.Total))}</td></tr>");
        return $"<table style=\"border-collapse:collapse;margin:12px 0;\">{rows}</table>";
    }

    private static string ItemName(OrderItem item) =>
        item.PackLabel.Length > 0 ? $"{item.ProductName}, {item.PackLabel}" : item.ProductName;

    private static string P(string text) => $"<p style=\"margin:0 0 12px;\">{H(text)}</p>";

    private static string Button(string label, string url) =>
        $"<p style=\"margin:20px 0;\"><a href=\"{H(url)}\" style=\"background:#62b22c;color:#ffffff;padding:12px 22px;"
        + $"border-radius:8px;text-decoration:none;font-weight:bold;\">{H(label)}</a></p>";

    private static string H(string value) => WebUtility.HtmlEncode(value);
}
