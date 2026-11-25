using System.Globalization;
using System.Text.RegularExpressions;
using SkiaSharp;

namespace OnlineShop.Web.Services;

/// <summary>
/// Цвета слайда с товаром (25.11.2026). Фон берём с этикетки на рендере: самый заметный яркий цвет
/// (белый пластик, чёрные надписи и прозрачный фон не считаются). Синяя этикетка Glass Gloss — синий фон,
/// зелёная Nano Soft — салатовый. Рукописное слово того же оттенка: светлее на тёмном фоне, темнее на светлом.
/// Тот же расчёт повторён в wwwroot/js/admin_slide_edit.js для живого превью.
/// </summary>
public static partial class SlideColors
{
    public const string Fallback = "#2c6bd3";

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex HexPattern();

    public static bool IsHex(string? value) => value is not null && HexPattern().IsMatch(value);

    /// <summary>Фон и цвет рукописного слова по картинке. Если ярких цветов нет — запасной синий.</summary>
    public static (string Bg, string Script) FromImage(byte[] content)
    {
        using var bitmap = SKBitmap.Decode(content);
        if (bitmap is null)
        {
            return (Fallback, ScriptFor(Fallback));
        }
        var step = Math.Max(1, Math.Max(bitmap.Width, bitmap.Height) / 160);
        const int bins = 24;
        var weight = new double[bins];
        var sums = new double[bins, 3];
        for (var y = 0; y < bitmap.Height; y += step)
        {
            for (var x = 0; x < bitmap.Width; x += step)
            {
                var c = bitmap.GetPixel(x, y);
                if (c.Alpha < 200)
                {
                    continue;
                }
                var (h, s, l) = ToHsl(c.Red, c.Green, c.Blue);
                if (s < 0.35 || l < 0.15 || l > 0.85)
                {
                    continue;
                }
                var bin = (int)(h / 360 * bins) % bins;
                weight[bin] += s;
                sums[bin, 0] += c.Red * s;
                sums[bin, 1] += c.Green * s;
                sums[bin, 2] += c.Blue * s;
            }
        }
        var best = Enumerable.Range(0, bins).MaxBy(i => weight[i]);
        if (weight[best] <= 0)
        {
            return (Fallback, ScriptFor(Fallback));
        }
        var (hue, sat, light) = ToHsl(
            (byte)(sums[best, 0] / weight[best]), (byte)(sums[best, 1] / weight[best]), (byte)(sums[best, 2] / weight[best]));
        // Жёлто-зелёные этикетки дают светлый «салатовый» фон, как у Nano Soft; остальные — насыщенный средний.
        var warmGreen = hue is >= 45 and <= 160;
        var bg = FromHsl(hue, Math.Clamp(sat, 0.55, 0.9), warmGreen ? Math.Clamp(light, 0.6, 0.7) : Math.Clamp(light, 0.45, 0.62));
        return (bg, ScriptFor(bg));
    }

    /// <summary>Рукописное слово: тот же оттенок, на тёмном фоне светлее, на светлом темнее.</summary>
    public static string ScriptFor(string bg)
    {
        var (r, g, b) = Parse(bg);
        var (h, s, l) = ToHsl(r, g, b);
        return FromHsl(h, Math.Min(1, s + 0.1), Math.Clamp(IsDark(bg) ? l + 0.15 : l - 0.15, 0.08, 0.92));
    }

    /// <summary>Тёмный фон — текст на слайде белый.</summary>
    public static bool IsDark(string bg)
    {
        var (r, g, b) = Parse(bg);
        return (0.299 * r + 0.587 * g + 0.114 * b) / 255 < 0.6;
    }

    public static string Ink(string bg) => IsDark(bg) ? "#ffffff" : "#0f0f0f";

    /// <summary>Блик, который пробегает по рукописному слову: его цвет, смешанный с белым.</summary>
    public static string Highlight(string script)
    {
        var (r, g, b) = Parse(script);
        static int Mix(int v) => (int)Math.Round(v + (255 - v) * 0.55);
        return $"#{Mix(r):x2}{Mix(g):x2}{Mix(b):x2}";
    }

    private static (int R, int G, int B) Parse(string hex)
    {
        if (!IsHex(hex))
        {
            hex = Fallback;
        }
        var value = int.Parse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return ((value >> 16) & 255, (value >> 8) & 255, value & 255);
    }

    private static (double H, double S, double L) ToHsl(int r8, int g8, int b8)
    {
        double r = r8 / 255.0, g = g8 / 255.0, b = b8 / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-6)
        {
            return (0, 0, l);
        }
        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    private static string FromHsl(double h, double s, double l)
    {
        var c = (1 - Math.Abs(2 * l - 1)) * s;
        var x = c * (1 - Math.Abs(h / 60 % 2 - 1));
        var m = l - c / 2;
        var (r, g, b) = h switch
        {
            < 60 => (c, x, 0.0),
            < 120 => (x, c, 0.0),
            < 180 => (0.0, c, x),
            < 240 => (0.0, x, c),
            < 300 => (x, 0.0, c),
            _ => (c, 0.0, x),
        };
        static int To8(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
        return $"#{To8(r + m):x2}{To8(g + m):x2}{To8(b + m):x2}";
    }
}
