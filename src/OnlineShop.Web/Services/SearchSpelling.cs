using System.Text;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;

namespace OnlineShop.Web.Services;

/// <summary>
/// Исправление запроса, по которому ничего не нашлось: набран не в той раскладке («ifvgeym»),
/// латиницей вместо кириллицы или с опечаткой («шампнь»). Слова сверяются со словами из названий
/// товаров, брендов и категорий: так исправление ведёт только к тому, что в магазине есть.
/// </summary>
public sealed class SearchSpelling(IServiceScopeFactory scopes)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);
    private const string Latin = "qwertyuiop[]asdfghjkl;'zxcvbnm,.`";
    private const string Cyrillic = "йцукенгшщзхъфывапролджэячсмитьбюё";

    private readonly SemaphoreSlim _lock = new(1, 1);
    private Dictionary<string, int>? _words;
    private DateTime _loadedAt;

    /// <summary>Исправленный запрос или null, если исправлять нечего или не на что.</summary>
    public async Task<string?> CorrectAsync(string query, CancellationToken ct = default) =>
        Correct(query, await WordsAsync(ct));

    /// <summary>Сердце исправления, отдельно от базы: слова каталога с частотой употребления.</summary>
    public static string? Correct(string query, IReadOnlyDictionary<string, int> words)
    {
        var parts = Tokenize(query);
        if (parts.Count == 0 || words.Count == 0)
        {
            return null;
        }
        var changed = false;
        var result = new List<string>();
        foreach (var part in parts)
        {
            var fixedWord = Known(part, words) ? part : Fix(part, words);
            changed |= fixedWord != part;
            result.Add(fixedWord);
        }
        return changed ? string.Join(' ', result) : null;
    }

    /// <summary>Слова запроса: буквы и цифры в нижнем регистре.</summary>
    public static List<string> Tokenize(string text)
    {
        var cleaned = new string(text.ToLowerInvariant().Replace('ё', 'е')
            .Select(c => char.IsLetterOrDigit(c) || c == '-' ? c : ' ').ToArray());
        return cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
    }

    // Слово уже есть в каталоге целиком или как начало слова: «шамп» не надо исправлять.
    private static bool Known(string word, IReadOnlyDictionary<string, int> words) =>
        word.Length < 3 || word.All(char.IsDigit) || words.Keys.Any(w => w.Contains(word, StringComparison.Ordinal));

    private static string Fix(string word, IReadOnlyDictionary<string, int> words)
    {
        // Сначала самые надёжные догадки: другая раскладка и транслит дают слово из каталога как есть.
        foreach (var candidate in new[] { SwitchLayout(word), Transliterate(word) })
        {
            if (candidate != word && candidate.Length >= 3 && Known(candidate, words))
            {
                return candidate;
            }
        }
        // Затем опечатки: ближайшее слово каталога или его начало (покупатель мог не допечатать).
        string? best = null;
        var bestScore = (Distance: int.MaxValue, Prefix: 1, Frequency: 0);
        var limit = word.Length <= 4 ? 1 : 2;
        foreach (var source in new[] { word, SwitchLayout(word), Transliterate(word) }.Distinct())
        {
            // Слово на том же алфавите, что набрано, вероятнее: «шампнь» это «шампунь», а не SHAMPOO,
            // хотя в транслите оба одинаково близки. Поэтому догадка через другой алфавит на шаг дальше.
            var penalty = source == word ? 0 : 1;
            foreach (var (candidate, frequency) in words)
            {
                if (Math.Abs(candidate.Length - source.Length) > limit && candidate.Length < source.Length)
                {
                    continue;
                }
                var (distance, isPrefix, value) = (Distance(source, candidate), 0, candidate);
                if (distance > 0 && candidate.Length > source.Length)
                {
                    // Слово могло быть частью длинного: «шампунь» в «автошампунь», «шампунь-воск».
                    // Сравниваем с каждым куском той же длины, плюс-минус буква.
                    var part = BestPart(source, candidate, limit);
                    if (part.Distance < distance)
                    {
                        (distance, isPrefix, value) = (part.Distance, 1, part.Value);
                    }
                }
                if (distance > limit)
                {
                    continue;
                }
                // Меньше правок, затем целое слово раньше его части, затем более частое в каталоге.
                var score = (Distance: distance + penalty, Prefix: isPrefix, Frequency: frequency);
                if (score.Distance < bestScore.Distance
                    || score.Distance == bestScore.Distance && (score.Prefix < bestScore.Prefix
                        || score.Prefix == bestScore.Prefix && score.Frequency > bestScore.Frequency))
                {
                    (best, bestScore) = (value, score);
                }
            }
        }
        return best ?? word;
    }

    private static (int Distance, string Value) BestPart(string source, string candidate, int limit)
    {
        var best = (Distance: int.MaxValue, Value: "");
        for (var length = Math.Max(3, source.Length - 1); length <= Math.Min(candidate.Length, source.Length + 1); length++)
        {
            for (var start = 0; start + length <= candidate.Length; start++)
            {
                var part = candidate.Substring(start, length);
                var distance = Distance(source, part);
                if (distance < best.Distance && distance <= limit)
                {
                    best = (distance, part);
                }
            }
        }
        return best;
    }

    /// <summary>Набрано не в той раскладке: «ifvgeym» ↔ «шампунь».</summary>
    public static string SwitchLayout(string word)
    {
        var result = new StringBuilder(word.Length);
        foreach (var c in word)
        {
            var latin = Latin.IndexOf(c);
            var cyrillic = Cyrillic.IndexOf(c);
            result.Append(latin >= 0 ? Cyrillic[latin] : cyrillic >= 0 ? Latin[cyrillic] : c);
        }
        return result.ToString();
    }

    private static readonly (string Cyr, string Lat)[] Translit =
    [
        ("щ", "sch"), ("ш", "sh"), ("ч", "ch"), ("ж", "zh"), ("ю", "yu"), ("я", "ya"), ("х", "h"), ("ц", "c"),
        ("а", "a"), ("б", "b"), ("в", "v"), ("г", "g"), ("д", "d"), ("е", "e"), ("з", "z"), ("и", "i"), ("й", "y"),
        ("к", "k"), ("л", "l"), ("м", "m"), ("н", "n"), ("о", "o"), ("п", "p"), ("р", "r"), ("с", "s"), ("т", "t"),
        ("у", "u"), ("ф", "f"), ("ы", "y"), ("э", "e"), ("ь", ""), ("ъ", ""),
    ];

    /// <summary>Латиница в кириллицу и наоборот: «polirol» ↔ «полироль», «тайр» ↔ «tayr».</summary>
    public static string Transliterate(string word)
    {
        var cyrillic = word.Any(c => c is >= 'а' and <= 'я');
        var result = word;
        foreach (var (cyr, lat) in Translit)
        {
            if (cyrillic)
            {
                result = result.Replace(cyr, lat);
            }
            else if (lat.Length > 0)
            {
                result = result.Replace(lat, cyr);
            }
        }
        return result;
    }

    /// <summary>Расстояние Дамерау-Левенштейна: вставка, удаление, замена и перестановка соседних букв.</summary>
    public static int Distance(string a, string b)
    {
        var d = new int[a.Length + 1, b.Length + 1];
        for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
        for (var j = 0; j <= b.Length; j++) d[0, j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = a[i - 1] == b[j - 1] ? 0 : 1;
                d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                {
                    d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
        }
        return d[a.Length, b.Length];
    }

    // Словарь каталога в памяти на 10 минут: после загрузки из 1С новые слова появятся сами.
    private async Task<Dictionary<string, int>> WordsAsync(CancellationToken ct)
    {
        if (_words is not null && DateTime.UtcNow - _loadedAt < Lifetime)
        {
            return _words;
        }
        await _lock.WaitAsync(ct);
        try
        {
            if (_words is null || DateTime.UtcNow - _loadedAt >= Lifetime)
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
                var texts = await db.Products.Where(p => p.IsActive).Select(p => p.Name).ToListAsync(ct);
                texts.AddRange(await db.Categories.Where(c => c.IsActive).Select(c => c.Name).ToListAsync(ct));
                texts.AddRange(await db.Brands.Where(b => b.IsActive).Select(b => b.Name).ToListAsync(ct));
                _words = texts.SelectMany(Tokenize).Where(w => w.Length >= 3 && !w.All(char.IsDigit))
                    .GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
                _loadedAt = DateTime.UtcNow;
            }
            return _words;
        }
        finally
        {
            _lock.Release();
        }
    }
}
