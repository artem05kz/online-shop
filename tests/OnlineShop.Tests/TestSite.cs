using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using OnlineShop.Web.Services.Telegram;

namespace OnlineShop.Tests;

/// <summary>
/// Приложение целиком на отдельной временной базе: настоящие запросы, куки и защита форм.
/// Обычно база SQLite во временной папке. Если задана переменная SHOP_TEST_POSTGRES (строка
/// подключения к серверу PostgreSQL), каждая фабрика создаёт на нём свою базу и удаляет её после тестов:
/// так те же тесты проверяют и PostgreSQL, на котором сайт работает на сервере.
/// </summary>
public class TempShopFactory : WebApplicationFactory<Program>
{
    public const string PostgresVariable = "SHOP_TEST_POSTGRES";

    private readonly string _dir = Path.Combine(Path.GetTempPath(), "online-shop-tests", Guid.NewGuid().ToString("N"));
    private readonly string? _postgresServer = Environment.GetEnvironmentVariable(PostgresVariable);
    private readonly string _postgresDatabase = "shop_test_" + Guid.NewGuid().ToString("N")[..12];

    /// <summary>Заглушка Telegram: тесты никогда не ходят в настоящий Telegram, даже если на машине задан токен.</summary>
    public FakeTelegramApi Telegram { get; } = new();

    /// <summary>Пустая строка: бот не подключён.</summary>
    public string BotToken { get; init; } = "test-token";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        Directory.CreateDirectory(_dir);
        if (string.IsNullOrEmpty(_postgresServer))
        {
            builder.UseSetting("ConnectionStrings:Shop", $"Data Source={Path.Combine(_dir, "shop.db")}");
        }
        else
        {
            builder.UseSetting("Database:Provider", "Postgres");
            builder.UseSetting("Storage:AppData", Path.Combine(_dir, "App_Data"));
            builder.UseSetting("ConnectionStrings:Shop",
                new NpgsqlConnectionStringBuilder(_postgresServer) { Database = _postgresDatabase }.ConnectionString);
        }
        builder.UseSetting("Media:Root", Path.Combine(_dir, "media"));
        builder.UseSetting(TelegramOptions.TokenKey, BotToken);
        builder.ConfigureTestServices(services => services.Replace(ServiceDescriptor.Singleton<ITelegramApi>(Telegram)));
    }

    /// <summary>Клиент с куками, но без автоматических переходов: тесты проверяют, куда сайт перенаправляет.</summary>
    public HttpClient Browser() => CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!string.IsNullOrEmpty(_postgresServer))
        {
            NpgsqlConnection.ClearAllPools();
            using var connection = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(_postgresServer) { Database = "postgres" }.ConnectionString);
            connection.Open();
            using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS {_postgresDatabase} WITH (FORCE)", connection);
            drop.ExecuteNonQuery();
        }
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
