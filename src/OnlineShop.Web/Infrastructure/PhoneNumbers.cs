namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Номера телефонов для сравнения. Менеджер пишет номер как привык («8 701 123 45 67»,
/// «+7 (701) 123-45-67»), а Telegram присылает «77011234567» или «+77011234567».
/// </summary>
public static class PhoneNumbers
{
    /// <summary>Только цифры с кодом страны. Пустая строка, если это не похоже на номер.</summary>
    public static string Normalize(string? raw)
    {
        var digits = new string((raw ?? "").Where(char.IsAsciiDigit).ToArray());
        if (digits.Length == 11 && digits[0] == '8')
        {
            // Привычная запись через восьмёрку: в Казахстане и России это тот же +7.
            digits = "7" + digits[1..];
        }
        else if (digits.Length == 10)
        {
            digits = "7" + digits;
        }
        return digits.Length is >= 11 and <= 15 ? digits : "";
    }

    /// <summary>Как показать номер человеку: +7 701 123-45-67.</summary>
    public static string Display(string digits) =>
        digits.Length == 11 && digits[0] == '7'
            ? $"+7 {digits[1..4]} {digits[4..7]}-{digits[7..9]}-{digits[9..]}"
            : "+" + digits;
}
