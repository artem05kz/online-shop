using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OnlineShop.Web.Services.Telegram;

/// <summary>Токен бота от @BotFather. Без него бот не запускается, а сайт работает как раньше.</summary>
public sealed class TelegramOptions
{
    public const string TokenKey = "Telegram:BotToken";

    public string BotToken { get; init; } = "";

    public bool IsConfigured => BotToken.Length > 0;

    public static TelegramOptions From(IConfiguration configuration) =>
        new() { BotToken = configuration[TokenKey]?.Trim() ?? "" };
}

// Только те поля ответов Telegram, которые нужны сайту.
public record TelegramUser(long Id, string? Username, string? FirstName);
public record TelegramChat(long Id, string Type);
public record TelegramContact(string PhoneNumber, long? UserId);
public record TelegramMessage(long MessageId, TelegramChat Chat, TelegramUser? From, string? Text, TelegramContact? Contact);
/// <summary>Нажатие кнопки под сообщением бота (CRM мойки, 24.11): Data — что за кнопка.</summary>
public record TelegramCallbackQuery(string Id, TelegramUser From, TelegramMessage? Message, string? Data);
public record TelegramUpdate(long UpdateId, TelegramMessage? Message, TelegramCallbackQuery? CallbackQuery = null);

/// <summary>Telegram ответил ошибкой. Code 403: человек заблокировал бота, 401: неверный токен.</summary>
public class TelegramException(int code, string description) : Exception($"Telegram: {code} {description}")
{
    public int Code { get; } = code;
}

/// <summary>Методы Bot API, которыми пользуется сайт. В тестах подменяется заглушкой.</summary>
public interface ITelegramApi
{
    Task<TelegramUser> GetMeAsync(CancellationToken ct);

    /// <summary>Ждёт новых сообщений боту до timeoutSeconds секунд (long polling).</summary>
    Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct);

    /// <summary>Сообщение с разметкой HTML. replyMarkup: клавиатура под полем ввода или её снятие.</summary>
    Task SendMessageAsync(long chatId, string html, object? replyMarkup, CancellationToken ct);

    /// <summary>Ответ на нажатие кнопки: короткая всплывающая подсказка у того, кто нажал.</summary>
    Task AnswerCallbackAsync(string callbackId, string text, CancellationToken ct);

    /// <summary>Заменить кнопки под уже отправленным сообщением (null — убрать кнопки).</summary>
    Task EditReplyMarkupAsync(long chatId, long messageId, object? replyMarkup, CancellationToken ct);
}

/// <summary>
/// Обращения к api.telegram.org. Свой HttpClient, а не из IHttpClientFactory: тот пишет в журнал
/// адрес каждого запроса, а токен бота входит в адрес и попал бы в логи сервера.
/// </summary>
public sealed class TelegramApi(TelegramOptions options) : ITelegramApi, IDisposable
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // Ожидание long polling длится до 25 секунд, запас сверху на медленную сеть.
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public Task<TelegramUser> GetMeAsync(CancellationToken ct) =>
        CallAsync<TelegramUser>("getMe", new { }, ct);

    public async Task<IReadOnlyList<TelegramUpdate>> GetUpdatesAsync(long offset, int timeoutSeconds, CancellationToken ct) =>
        await CallAsync<List<TelegramUpdate>>("getUpdates",
            new { offset, timeout = timeoutSeconds, allowed_updates = new[] { "message", "callback_query" } }, ct);

    public Task SendMessageAsync(long chatId, string html, object? replyMarkup, CancellationToken ct) =>
        CallAsync<JsonElement>("sendMessage", new
        {
            chat_id = chatId,
            text = html,
            parse_mode = "HTML",
            link_preview_options = new { is_disabled = true },
            reply_markup = replyMarkup,
        }, ct);

    public Task AnswerCallbackAsync(string callbackId, string text, CancellationToken ct) =>
        CallAsync<JsonElement>("answerCallbackQuery", new { callback_query_id = callbackId, text }, ct);

    public Task EditReplyMarkupAsync(long chatId, long messageId, object? replyMarkup, CancellationToken ct) =>
        CallAsync<JsonElement>("editMessageReplyMarkup", new
        {
            chat_id = chatId,
            message_id = messageId,
            reply_markup = replyMarkup ?? new { inline_keyboard = Array.Empty<object>() },
        }, ct);

    private async Task<T> CallAsync<T>(string method, object body, CancellationToken ct)
    {
        using var response = await _http.PostAsJsonAsync(
            $"https://api.telegram.org/bot{options.BotToken}/{method}", body, Json, ct);
        // Telegram отвечает JSON и при ошибке (401, 403, 409), в нём описание причины.
        var answer = await response.Content.ReadFromJsonAsync<Answer<T>>(Json, ct);
        if (answer is not { Ok: true, Result: not null })
        {
            throw new TelegramException(answer?.ErrorCode ?? (int)response.StatusCode, answer?.Description ?? response.ReasonPhrase ?? "");
        }
        return answer.Result;
    }

    private sealed record Answer<T>(bool Ok, T? Result, int? ErrorCode, string? Description);

    public void Dispose() => _http.Dispose();
}

/// <summary>Клавиатуры под полем ввода в чате с ботом.</summary>
public static class TelegramKeyboards
{
    /// <summary>Одна кнопка: Telegram сам отправит номер этого человека, вводить его не нужно.</summary>
    public static object SharePhone => new
    {
        keyboard = new[] { new[] { new { text = "Отправить мой номер", request_contact = true } } },
        resize_keyboard = true,
        one_time_keyboard = true,
    };

    public static object Remove => new { remove_keyboard = true };

    /// <summary>Кнопки под новой записью на мойку: подтвердить или отменить, не заходя в панель.</summary>
    public static object Booking(int id) => new
    {
        inline_keyboard = new[]
        {
            new[] { new { text = "✅ Подтвердить", callback_data = $"wash:ok:{id}" }, new { text = "Отменить", callback_data = $"wash:ask:{id}" } },
        },
    };

    /// <summary>Отмена — в два нажатия, чтобы не отменить запись случайно.</summary>
    public static object BookingCancelAsk(int id) => new
    {
        inline_keyboard = new[]
        {
            new[] { new { text = "Да, отменить запись", callback_data = $"wash:cancel:{id}" } },
            new[] { new { text = "Назад", callback_data = $"wash:back:{id}" } },
        },
    };
}
