using System.Threading.Channels;
using MailKit.Net.Smtp;
using MailKit.Security;
using MimeKit;

namespace OnlineShop.Web.Services.Email;

/// <summary>
/// Почтовый ящик, с которого сайт отправляет письма. Для Mail.ru: smtp.mail.ru, порт 465,
/// логин это адрес ящика, пароль это «пароль для внешних приложений» из настроек Mail.ru, а не обычный.
/// Без адреса сервера письма не уходят, а складываются файлами в App_Data/mail: так удобно смотреть их при разработке.
/// </summary>
public sealed class EmailOptions
{
    public string Host { get; init; } = "";
    public int Port { get; init; } = 465;
    public string User { get; init; } = "";
    public string Password { get; init; } = "";
    public string From { get; init; } = "GRASS Актау <Grassaktau@mail.ru>";

    public bool IsConfigured => Host.Length > 0;

    public static EmailOptions Load(IConfiguration configuration)
    {
        var section = configuration.GetSection("Email");
        return new EmailOptions
        {
            Host = section["Host"]?.Trim() ?? "",
            Port = section.GetValue("Port", 465),
            User = section["User"]?.Trim() ?? "",
            Password = section["Password"] ?? "",
            From = section["From"] is { Length: > 0 } from ? from.Trim() : "GRASS Актау <Grassaktau@mail.ru>",
        };
    }
}

public record EmailMessage(string To, string Subject, string Html, string Text);

/// <summary>Отправка одного письма. В тестах подменяется заглушкой.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public sealed class SmtpEmailSender(EmailOptions options) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        using var smtp = new SmtpClient();
        // 465: шифрование сразу при подключении (Mail.ru, Яндекс), 587: STARTTLS, прочие порты
        // (например, Mailpit для проверки на своём компьютере) без шифрования.
        var security = options.Port switch
        {
            465 => SecureSocketOptions.SslOnConnect,
            587 => SecureSocketOptions.StartTls,
            _ => SecureSocketOptions.None,
        };
        await smtp.ConnectAsync(options.Host, options.Port, security, ct);
        if (options.User.Length > 0)
        {
            await smtp.AuthenticateAsync(options.User, options.Password, ct);
        }
        await smtp.SendAsync(EmailFiles.ToMime(options.From, message), ct);
        await smtp.DisconnectAsync(quit: true, ct);
    }
}

/// <summary>Почта не настроена: письмо ложится файлом .eml, его можно открыть двойным щелчком.</summary>
public sealed class PickupFolderEmailSender(string folder, EmailOptions options, ILogger<PickupFolderEmailSender> logger) : IEmailSender
{
    public async Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid().ToString("N")[..6]}.eml");
        await EmailFiles.ToMime(options.From, message).WriteToAsync(path, ct);
        logger.LogWarning("Почта не настроена (Email:Host), письмо «{Subject}» сохранено в {Path}", message.Subject, path);
    }
}

public static class EmailFiles
{
    public static MimeMessage ToMime(string from, EmailMessage message)
    {
        var mime = new MimeMessage();
        mime.From.Add(MailboxAddress.Parse(from));
        mime.To.Add(MailboxAddress.Parse(message.To));
        mime.Subject = message.Subject;
        mime.Body = new BodyBuilder { HtmlBody = message.Html, TextBody = message.Text }.ToMessageBody();
        return mime;
    }
}

/// <summary>
/// Очередь писем. Страница кладёт письмо и сразу отвечает: медленный или недоступный почтовый сервер
/// не задерживает оформление заказа и не ломает его.
/// </summary>
public sealed class EmailQueue
{
    private readonly Channel<EmailMessage> _queue = Channel.CreateUnbounded<EmailMessage>();

    public ChannelReader<EmailMessage> Reader => _queue.Reader;

    public void Enqueue(EmailMessage message) => _queue.Writer.TryWrite(message);
}

/// <summary>Отправляет письма из очереди. Сбой отправки повторяется трижды с паузой.</summary>
public sealed class EmailWorker(EmailQueue queue, IEmailSender sender, ILogger<EmailWorker> logger) : BackgroundService
{
    private static readonly TimeSpan[] Retries = [TimeSpan.FromSeconds(10), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5)];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var message in queue.Reader.ReadAllAsync(stoppingToken))
            {
                await SendWithRetriesAsync(message, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }

    private async Task SendWithRetriesAsync(EmailMessage message, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                await sender.SendAsync(message, ct);
                return;
            }
            catch (Exception e) when (!ct.IsCancellationRequested)
            {
                if (attempt >= Retries.Length)
                {
                    // Адрес покупателя в журнал не пишем: журнал не место для персональных данных.
                    logger.LogError(e, "Письмо «{Subject}» не отправлено после {Attempts} попыток", message.Subject, attempt + 1);
                    return;
                }
                logger.LogWarning("Письмо «{Subject}» не ушло ({Error}), повтор через {Delay}", message.Subject, e.Message, Retries[attempt]);
                await Task.Delay(Retries[attempt], ct);
            }
        }
    }
}
