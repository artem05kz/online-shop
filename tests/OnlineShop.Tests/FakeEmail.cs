using System.Text.RegularExpressions;
using OnlineShop.Web.Services.Email;

namespace OnlineShop.Tests;

/// <summary>Почта в памяти: тесты читают, какие письма сайт отправил, и достают из них коды.</summary>
public sealed partial class FakeEmailSender : IEmailSender
{
    private readonly List<EmailMessage> _sent = [];

    public Task SendAsync(EmailMessage message, CancellationToken ct)
    {
        lock (_sent)
        {
            _sent.Add(message);
        }
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> SentTo(string to)
    {
        lock (_sent)
        {
            return _sent.Where(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    /// <summary>Ждёт письмо номер count на этот адрес: письма уходят из фоновой очереди.</summary>
    public async Task<EmailMessage> WaitFor(string to, int count = 1)
    {
        for (var i = 0; i < 250; i++)
        {
            var sent = SentTo(to);
            if (sent.Count >= count)
            {
                return sent[count - 1];
            }
            await Task.Delay(20);
        }
        throw new TimeoutException($"На {to} не пришло письмо №{count}.");
    }

    /// <summary>Шестизначный код из последнего письма на адрес.</summary>
    public async Task<string> CodeFor(string to, int count = 1) =>
        Code().Match((await WaitFor(to, count)).Text).Value;

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex Code();
}
