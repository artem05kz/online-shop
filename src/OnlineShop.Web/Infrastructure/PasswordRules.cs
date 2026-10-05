using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Identity;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Проверки пароля, которые в Django-версии давали стандартные валидаторы: пароль не только
/// из цифр, не из списка распространённых и не похож на логин, имя или почту.
/// Длину (не меньше 8) проверяет сам Identity.
/// </summary>
public partial class PasswordRules : IPasswordValidator<ApplicationUser>
{
    private const double MaxSimilarity = 0.7;

    // В Django список из 20 000 паролей. Здесь самые частые из него плюс русские раскладочные:
    // задача отсечь очевидное, а не заменить хороший пароль.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password", "password1", "password12", "password123", "passw0rd", "qwerty", "qwerty12", "qwerty123",
        "qwertyuiop", "qwerty1234", "1q2w3e4r", "1q2w3e4r5t", "1qaz2wsx", "zaq12wsx", "asdfghjk", "asdfghjkl",
        "zxcvbnm1", "iloveyou", "sunshine", "princess", "football", "baseball", "superman", "trustno1",
        "welcome1", "letmein1", "starwars", "whatever", "computer", "internet", "michael1", "jennifer",
        "abcd1234", "abc12345", "aa123456", "a1234567", "qazwsxedc", "dragon12", "master12", "monkey12",
        "12345678", "123456789", "1234567890", "87654321", "11111111", "00000000", "12341234", "11223344",
        "123123123", "147258369", "123qweasd", "qweasdzxc", "admin123", "administrator",
        "пароль", "пароль123", "йцукен", "йцукенгш", "йцукенгшщз", "фывапролд", "привет123", "любовь123",
    };

    private static readonly (string Label, Func<ApplicationUser, string?> Value)[] Attributes =
    [
        ("логин", u => u.UserName),
        ("имя", u => u.FirstName),
        ("фамилию", u => u.LastName),
        ("адрес электронной почты", u => u.Email),
    ];

    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        var errors = Check(user, password ?? "")
            .Select(message => new IdentityError { Code = "PasswordRules", Description = message })
            .ToArray();
        return Task.FromResult(errors.Length == 0 ? IdentityResult.Success : IdentityResult.Failed(errors));
    }

    public static IEnumerable<string> Check(ApplicationUser user, string password)
    {
        if (password.Length > 0 && password.All(char.IsDigit))
        {
            yield return "Пароль не может состоять только из цифр.";
        }
        if (Common.Contains(password.Trim()))
        {
            yield return "Этот пароль слишком распространён, придумайте другой.";
        }

        var lowered = password.ToLowerInvariant();
        foreach (var (label, value) in Attributes)
        {
            var text = value(user)?.ToLowerInvariant();
            if (string.IsNullOrEmpty(text))
            {
                continue;
            }
            // Как в Django: сравниваем и со значением целиком, и с его частями (почта до и после @).
            var parts = NonWordRegex().Split(text).Append(text).Where(p => p.Length > 0);
            if (parts.Any(part => Similarity(lowered, part) >= MaxSimilarity))
            {
                yield return $"Пароль слишком похож на {label}.";
            }
        }
    }

    /// <summary>
    /// Доля совпадения двух строк от 0 до 1: удвоенная длина общей подпоследовательности,
    /// делённая на сумму длин. Близко к difflib.SequenceMatcher.ratio(), которым пользуется Django.
    /// </summary>
    public static double Similarity(string a, string b)
    {
        if (a.Length + b.Length == 0)
        {
            return 1;
        }
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = a[i - 1] == b[j - 1]
                    ? previous[j - 1] + 1
                    : Math.Max(previous[j], current[j - 1]);
            }
            (previous, current) = (current, previous);
        }
        return 2.0 * previous[b.Length] / (a.Length + b.Length);
    }

    [GeneratedRegex(@"\W+")]
    private static partial Regex NonWordRegex();
}
