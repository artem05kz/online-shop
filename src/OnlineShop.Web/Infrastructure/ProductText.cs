using System.Text.RegularExpressions;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Описание товара из 1С обычно устроено одинаково: абзац о средстве, затем «Способ применения:» и шаги «1. …», «2. …».
/// Разбираем его, чтобы на странице товара показать шаги нумерованным списком. Если описание устроено иначе,
/// всё остаётся одним текстом.
/// </summary>
public static partial class ProductText
{
    public record Parts(string Intro, IReadOnlyList<string> Steps, string Outro);

    [GeneratedRegex(@"^\s*\d{1,2}\s*[.)]\s*(.+)$")]
    private static partial Regex Step();

    [GeneratedRegex(@"способ\s+применения\s*:?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex HowTo();

    public static Parts Split(string? description)
    {
        var text = (description ?? "").Replace("\r", "").Trim();
        var m = HowTo().Match(text);
        if (!m.Success)
        {
            return new Parts(text, [], "");
        }
        var intro = text[..m.Index].Trim();
        var steps = new List<string>();
        var outro = new List<string>();
        // Строка без номера после шагов — продолжение предыдущего шага: в 1С длинный шаг часто перенесён
        // на новую строку, и продолжение может начинаться с цифры или скобки («(10-15 г/л) для…»).
        // Текстом после шагов считается только то, что отделено пустой строкой.
        var blankAfterSteps = false;
        foreach (var line in text[(m.Index + m.Length)..].Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                if (steps.Count > 0) blankAfterSteps = true;
                continue;
            }
            var step = Step().Match(line);
            if (step.Success && outro.Count == 0)
            {
                steps.Add(step.Groups[1].Value.Trim());
                blankAfterSteps = false;
            }
            else if (steps.Count > 0 && outro.Count == 0 && !blankAfterSteps)
            {
                steps[^1] += " " + line.Trim();
            }
            else
            {
                outro.Add(line.Trim());
            }
        }
        // Без шагов разбирать нечего: показываем как было.
        return steps.Count == 0 ? new Parts(text, [], "") : new Parts(intro, steps, string.Join("\n", outro));
    }
}
