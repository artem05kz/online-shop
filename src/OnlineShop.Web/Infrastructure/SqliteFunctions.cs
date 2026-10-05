using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Функции, которых нет во встроенной SQLite. Встроенные lower() и LIKE переводят в нижний регистр
/// только латиницу, поэтому поиск «гель» не находил бы «Гель». В Django-версии так и было.
/// </summary>
public static class SqliteFunctions
{
    public const string LowerName = "ulower";

    /// <summary>Вызов в LINQ-запросе превращается в SQL-функцию: ulower в SQLite, lower в других базах.</summary>
    public static string Lower(string value) => throw new InvalidOperationException("Вызывается только внутри запроса к базе.");

    public static void Register(SqliteConnection connection) =>
        connection.CreateFunction(LowerName, (string? value) => value?.ToLowerInvariant(), isDeterministic: true);

    /// <summary>Регистрирует функции при каждом открытии соединения: у SQLite они живут в пределах соединения.</summary>
    public sealed class Interceptor : DbConnectionInterceptor
    {
        public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
        {
            if (connection is SqliteConnection sqlite)
            {
                Register(sqlite);
            }
        }

        public override Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData,
            CancellationToken cancellationToken = default)
        {
            ConnectionOpened(connection, eventData);
            return Task.CompletedTask;
        }
    }
}
