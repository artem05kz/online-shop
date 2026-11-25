using System.Text.Json;
using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace OnlineShop.Web.Infrastructure;

/// <summary>Уровень сообщения, он же CSS-класс плашки: success, info или error.</summary>
public enum FlashLevel
{
    Success,
    Info,
    Error,
}

/// <summary>Сообщение; Link и LinkText — необязательная ссылка-действие («Перейти в корзину»).</summary>
public record FlashMessage(FlashLevel Level, string Text, string? Link = null, string? LinkText = null)
{
    public string CssClass => Level.ToString().ToLowerInvariant();
}

/// <summary>
/// Сообщения вида «Товар добавлен в корзину», которые показываются один раз на следующей странице
/// после перенаправления. Аналог django.contrib.messages, хранится в TempData.
/// </summary>
public static class FlashMessages
{
    private const string Key = "flash";

    public static void Flash(this ITempDataDictionary tempData, string text, FlashLevel level = FlashLevel.Success) =>
        tempData.Flash(new FlashMessage(level, text));

    /// <summary>Сообщение со ссылкой-действием, например ««Шампунь» в корзине» + «Перейти в корзину».</summary>
    public static void Flash(this ITempDataDictionary tempData, string text, string link, string linkText, FlashLevel level = FlashLevel.Success) =>
        tempData.Flash(new FlashMessage(level, text, link, linkText));

    private static void Flash(this ITempDataDictionary tempData, FlashMessage message)
    {
        var messages = Read(tempData.Peek(Key));
        messages.Add(message);
        tempData[Key] = JsonSerializer.Serialize(messages);
    }

    /// <summary>Забирает накопленные сообщения: после показа они удаляются.</summary>
    public static IReadOnlyList<FlashMessage> TakeFlash(this ITempDataDictionary tempData) => Read(tempData[Key]);

    private static List<FlashMessage> Read(object? raw) =>
        raw is string json ? JsonSerializer.Deserialize<List<FlashMessage>>(json) ?? [] : [];
}
