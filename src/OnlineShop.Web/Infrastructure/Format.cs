using System.Globalization;

namespace OnlineShop.Web.Infrastructure;

/// <summary>Как показывать покупателю цены и даты.</summary>
public static class Format
{
    private static readonly CultureInfo Russian = CultureInfo.GetCultureInfo("ru-RU");

    // Магазин в Актау, а даты в базе в UTC.
    private static readonly TimeZoneInfo Aqtau = TimeZoneInfo.FindSystemTimeZoneById("Asia/Aqtau");

    /// <summary>Цена в тенге без тиынов, с разделителем разрядов: «15 991 ₸».</summary>
    public static string Money(decimal value) =>
        decimal.Round(value, 0, MidpointRounding.AwayFromZero).ToString("#,0", Russian) + " ₸";

    public static DateTime ToLocal(DateTime utc) => TimeZoneInfo.ConvertTimeFromUtc(
        DateTime.SpecifyKind(utc, DateTimeKind.Utc), Aqtau);

    /// <summary>Дата и время по Актау: «05.10.2026, 19:02».</summary>
    public static string DateTimeText(DateTime utc) => ToLocal(utc).ToString("dd.MM.yyyy, HH:mm", Russian);
}
