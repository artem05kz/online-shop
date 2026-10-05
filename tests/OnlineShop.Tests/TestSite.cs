using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace OnlineShop.Tests;

/// <summary>
/// Приложение целиком на отдельной временной базе: настоящие запросы, куки, сессия и защита форм.
/// </summary>
public class TempShopFactory : WebApplicationFactory<Program>
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "online-shop-tests", Guid.NewGuid().ToString("N"));

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        builder.UseSetting("ConnectionStrings:Shop", $"Data Source={Path.Combine(_dir, "shop.db")}");
        builder.UseSetting("Media:Root", Path.Combine(_dir, "media"));
    }

    /// <summary>Клиент с куками, но без автоматических переходов: тесты проверяют, куда сайт перенаправляет.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
            // SQLite может ещё держать файл: временная папка не стоит падения тестов.
        }
    }
}

public static class TestSite
{
    /// <summary>Текст страницы так, как его видит покупатель: коды вроде &amp;#xA0; уже раскодированы.</summary>
    public static async Task<string> Page(HttpClient client, string url) =>
        WebUtility.HtmlDecode(await client.GetStringAsync(url));

    /// <summary>Защита от подделки запросов: форму можно отправить только с токеном со страницы сайта.</summary>
    public static async Task<string> TokenFrom(HttpClient client, string url)
    {
        var html = await client.GetStringAsync(url);
        return Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value;
    }

    public static Task<HttpResponseMessage> Post(HttpClient client, string url, string token, params (string, string)[] fields) =>
        client.PostAsync(url, new FormUrlEncodedContent(
            fields.Select(f => new KeyValuePair<string, string>(f.Item1, f.Item2))
                .Append(new KeyValuePair<string, string>("__RequestVerificationToken", token))));

    public static async Task<string> Text(HttpResponseMessage response) =>
        WebUtility.HtmlDecode(await response.Content.ReadAsStringAsync());
}
