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

public record FlashMessage(FlashLevel Level, string Text)
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

    public static void Flash(this ITempDataDictionary tempData, string text, FlashLevel level = FlashLevel.Success)
    {
        var messages = Read(tempData.Peek(Key));
        messages.Add(new FlashMessage(level, text));
        tempData[Key] = JsonSerializer.Serialize(messages);
    }

    /// <summary>Забирает накопленные сообщения: после показа они удаляются.</summary>
    public static IReadOnlyList<FlashMessage> TakeFlash(this ITempDataDictionary tempData) => Read(tempData[Key]);

    private static List<FlashMessage> Read(object? raw) =>
        raw is string json ? JsonSerializer.Deserialize<List<FlashMessage>>(json) ?? [] : [];
}
