using System.Text.Json;
using System.Threading.Channels;
using OnlineShop.Web.Services.Telegram;

namespace OnlineShop.Tests;

/// <summary>Telegram в памяти: тест пишет боту от имени человека и читает, что бот ответил.</summary>
public sealed class FakeTelegramApi : ITelegramApi
{
    public record Sent(long ChatId, string Text, string Markup);

    private readonly Channel<TelegramUpdate> _incoming = Channel.CreateUnbounded<TelegramUpdate>();
    private readonly List<Sent> _sent = [];
    private long _updateId;

    /// <summary>Чаты, отправка в которые падает с этим кодом ошибки (403: бот заблокирован).</summary>
    public Dictionary<long, int> FailingChats { get; } = [];

    public Task<TelegramUser> GetMeAsync(CancellationToken ct) => Task.FromResult(new TelegramUser(1, "grass_test_bot", "GRASS"));

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct)
    {
        var updates = new List<TelegramUpdate> { await _incoming.Reader.ReadAsync(ct) };
        while (_incoming.Reader.TryRead(out var more))
        {
            updates.Add(more);
        }
        return updates;
    }

    public Task SendMessageAsync(long chatId, string html, object? replyMarkup, CancellationToken ct)
    {
        lock (FailingChats)
        {
            if (FailingChats.TryGetValue(chatId, out var code))
            {
                throw new TelegramException(code, "Forbidden: bot was blocked by the user");
            }
        }
        lock (_sent)
        {
            _sent.Add(new Sent(chatId, html, replyMarkup is null ? "" : JsonSerializer.Serialize(replyMarkup)));
        }
        return Task.CompletedTask;
    }

    /// <summary>Человек пишет боту в личный чат. userId по умолчанию совпадает с номером чата, как в Telegram.</summary>
    public void Write(long chatId, string? text = null, TelegramContact? contact = null, string chatType = "private") =>
        _incoming.Writer.TryWrite(new TelegramUpdate(Interlocked.Increment(ref _updateId),
            new TelegramMessage(1, new TelegramChat(chatId, chatType), new TelegramUser(chatId, null, "Тест"), text, contact)));

    public void Fail(long chatId, int code)
    {
        lock (FailingChats)
        {
            FailingChats[chatId] = code;
        }
    }

    public IReadOnlyList<Sent> SentTo(long chatId)
    {
        lock (_sent)
        {
            return _sent.Where(s => s.ChatId == chatId).ToList();
        }
    }

    /// <summary>Ждёт, пока в чат придёт сообщение номер count (бот отвечает из фоновой службы).</summary>
    public async Task<Sent> WaitForMessage(long chatId, int count = 1)
    {
        for (var i = 0; i < 250; i++)
        {
            var sent = SentTo(chatId);
            if (sent.Count >= count)
            {
                return sent[count - 1];
            }
            await Task.Delay(20);
        }
        throw new TimeoutException($"В чат {chatId} не пришло сообщение №{count}.");
    }

    /// <summary>Ждёт нужное сообщение, когда в тот же чат могут прийти сообщения других тестов.</summary>
    public async Task<Sent> WaitForMessage(long chatId, Func<Sent, bool> match)
    {
        for (var i = 0; i < 250; i++)
        {
            if (SentTo(chatId).FirstOrDefault(match) is { } sent)
            {
                return sent;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException($"В чат {chatId} не пришло нужное сообщение.");
    }
}
