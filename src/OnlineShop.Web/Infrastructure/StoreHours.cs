using System.Text.RegularExpressions;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Часы работы магазина, разобранные из текста, который менеджер пишет в «Адресах»:
/// «Пн–Пт 9:00–16:00, Сб–Вс выходной», «Ежедневно 11:00–18:00», «Пн-Сб, 9:00-18:00».
/// Нужны, чтобы на сайте писать «Открыто до 16:00» или «Закрыто, откроется в понедельник в 9:00».
/// Если текст не удалось понять, статус не показываем (лучше ничего, чем неправда).
/// </summary>
public sealed partial class StoreHours
{
    private static readonly string[] DayCodes = ["пн", "вт", "ср", "чт", "пт", "сб", "вс"];

    // По номеру дня (0 — понедельник): часы работы или null, если выходной.
    private readonly (TimeOnly Open, TimeOnly Close)?[] _days;
    private readonly (TimeOnly From, TimeOnly To)? _lunch;

    private StoreHours((TimeOnly, TimeOnly)?[] days, (TimeOnly, TimeOnly)? lunch)
    {
        _days = days;
        _lunch = lunch;
    }

    [GeneratedRegex(@"(?<all>ежедневно|каждый день|без выходных)|(?<d1>пн|вт|ср|чт|пт|сб|вс)[а-я]*\.?(?:\s*-\s*(?<d2>пн|вт|ср|чт|пт|сб|вс)[а-я]*\.?)?|(?<h1>\d{1,2})(?:[:.](?<m1>\d{2}))?\s*-\s*(?<h2>\d{1,2})(?:[:.](?<m2>\d{2}))?|(?<off>выходн)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex TokenRegex();

    [GeneratedRegex(@"[–—−‑]")]
    private static partial Regex DashRegex();

    /// <summary>Разобрать часы; null, если в тексте нет ни одного промежутка времени.</summary>
    public static StoreHours? Parse(string? workingHours, string? lunchHours = null)
    {
        if (string.IsNullOrWhiteSpace(workingHours))
        {
            return null;
        }
        var text = DashRegex().Replace(workingHours.ToLowerInvariant(), "-");
        var days = new (TimeOnly, TimeOnly)?[7];
        var pending = new List<int>();
        var anyTime = false;
        foreach (Match m in TokenRegex().Matches(text))
        {
            if (m.Groups["all"].Success)
            {
                pending = [0, 1, 2, 3, 4, 5, 6];
            }
            else if (m.Groups["d1"].Success)
            {
                var from = Array.IndexOf(DayCodes, m.Groups["d1"].Value);
                var to = m.Groups["d2"].Success ? Array.IndexOf(DayCodes, m.Groups["d2"].Value) : from;
                // Дни идут подряд после предыдущих: «Пн–Пт, Сб» — это один список.
                for (var d = from; ; d = (d + 1) % 7)
                {
                    if (!pending.Contains(d)) pending.Add(d);
                    if (d == to) break;
                }
            }
            else if (m.Groups["h1"].Success)
            {
                if (Time(m.Groups["h1"].Value, m.Groups["m1"].Value) is not { } open
                    || Time(m.Groups["h2"].Value, m.Groups["m2"].Value) is not { } close || close <= open)
                {
                    return null;
                }
                // Время без дней перед ним — значит, каждый день («9:00–18:00»).
                foreach (var d in pending.Count > 0 ? pending : [0, 1, 2, 3, 4, 5, 6])
                {
                    days[d] = (open, close);
                }
                pending = [];
                anyTime = true;
            }
            else if (m.Groups["off"].Success)
            {
                foreach (var d in pending)
                {
                    days[d] = null;
                }
                pending = [];
            }
        }
        if (!anyTime)
        {
            return null;
        }

        (TimeOnly, TimeOnly)? lunch = null;
        if (!string.IsNullOrWhiteSpace(lunchHours))
        {
            var lm = TokenRegex().Matches(DashRegex().Replace(lunchHours.ToLowerInvariant(), "-")).FirstOrDefault(x => x.Groups["h1"].Success);
            if (lm is not null && Time(lm.Groups["h1"].Value, lm.Groups["m1"].Value) is { } lf
                && Time(lm.Groups["h2"].Value, lm.Groups["m2"].Value) is { } lt && lt > lf)
            {
                lunch = (lf, lt);
            }
        }
        return new StoreHours(days, lunch);
    }

    private static TimeOnly? Time(string hours, string minutes)
    {
        var h = int.Parse(hours);
        var min = minutes.Length > 0 ? int.Parse(minutes) : 0;
        return h is >= 0 and <= 24 && min is >= 0 and < 60 ? (h == 24 ? TimeOnly.MaxValue : new TimeOnly(h, min)) : null;
    }

    private static int Index(DayOfWeek day) => ((int)day + 6) % 7;

    /// <summary>Работает без выходных: все семь дней есть часы.</summary>
    public bool EveryDay => _days.All(d => d is not null);

    /// <summary>Открыто ли в этот момент (местное время магазина) и короткая подпись для карточки.</summary>
    public (bool IsOpen, string Text) StatusAt(DateTime local)
    {
        var now = TimeOnly.FromDateTime(local);
        if (_days[Index(local.DayOfWeek)] is { } today && now >= today.Open && now < today.Close)
        {
            if (_lunch is { } lunch && now >= lunch.From && now < lunch.To)
            {
                return (false, $"Обед до {Hm(lunch.To)}");
            }
            return (true, $"Открыто до {Hm(today.Close)}");
        }

        // Ищем ближайшее открытие: сегодня позже, завтра или в другой день недели.
        for (var shift = 0; shift < 8; shift++)
        {
            var date = local.Date.AddDays(shift);
            if (_days[Index(date.DayOfWeek)] is not { } day || (shift == 0 && now >= day.Open))
            {
                continue;
            }
            var when = shift switch
            {
                0 => $"в {Hm(day.Open)}",
                1 => $"завтра в {Hm(day.Open)}",
                _ => $"{DayAccusative(date.DayOfWeek)} в {Hm(day.Open)}",
            };
            return (false, $"Закрыто, откроется {when}");
        }
        return (false, "Закрыто");
    }

    private static string Hm(TimeOnly t) => t == TimeOnly.MaxValue ? "24:00" : $"{t.Hour}:{t.Minute:00}";

    private static string DayAccusative(DayOfWeek day) => day switch
    {
        DayOfWeek.Monday => "в понедельник",
        DayOfWeek.Tuesday => "во вторник",
        DayOfWeek.Wednesday => "в среду",
        DayOfWeek.Thursday => "в четверг",
        DayOfWeek.Friday => "в пятницу",
        DayOfWeek.Saturday => "в субботу",
        _ => "в воскресенье",
    };

    /// <summary>
    /// Текст часов построчно для карточки: «Пн–Пт 9:00–16:00» и «Сб–Вс выходной» отдельными строками.
    /// </summary>
    public static IReadOnlyList<string> Lines(string? workingHours)
    {
        if (string.IsNullOrWhiteSpace(workingHours))
        {
            return [];
        }
        var text = Regex.Replace(workingHours.Trim(), @"(?<=\d|[а-яА-Я])\s*[-−‑]\s*(?=\d|[а-яА-Я])", "–");
        return Regex.Split(text, @",\s*(?=(?:пн|вт|ср|чт|пт|сб|вс|ежедн|без)\b)", RegexOptions.IgnoreCase)
            .Select(l => l.Length > 0 ? char.ToUpper(l[0]) + l[1..] : l)
            .ToList();
    }
}
