using Microsoft.AspNetCore.Http.Extensions;

namespace OnlineShop.Web.Infrastructure;

/// <summary>Ссылки, которые меняют один параметр адреса и сохраняют остальные (фильтры, поиск, сортировку).</summary>
public static class QueryStrings
{
    public static string With(IQueryCollection query, string key, string value)
    {
        var builder = new QueryBuilder();
        foreach (var (name, values) in query)
        {
            if (name != key)
            {
                foreach (var v in values)
                {
                    builder.Add(name, v ?? "");
                }
            }
        }
        builder.Add(key, value);
        return builder.ToString();
    }

    /// <summary>Все параметры адреса, кроме перечисленных: для скрытых полей формы сортировки.</summary>
    public static IEnumerable<KeyValuePair<string, string>> Except(IQueryCollection query, params string[] keys) =>
        query
            .Where(pair => !keys.Contains(pair.Key))
            .SelectMany(pair => pair.Value.Select(v => new KeyValuePair<string, string>(pair.Key, v ?? "")));
}
