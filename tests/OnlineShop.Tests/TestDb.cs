using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Tests;

/// <summary>
/// Настоящая SQLite в памяти, а не InMemory-провайдер EF: проверять нужно именно поведение SQLite
/// (хранение цен, сортировку, уникальные индексы), а InMemory этого не воспроизводит.
/// База создаётся теми же миграциями, что и рабочая.
/// </summary>
public sealed class TestDb : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    public TestDb()
    {
        _connection.Open();
        using var db = Create();
        db.Database.Migrate();
    }

    /// <summary>Новый контекст на ту же базу: так проверяется, что данные действительно легли в таблицы.</summary>
    public ShopDbContext Create() =>
        new(new DbContextOptionsBuilder<ShopDbContext>().UseSqlite(_connection).Options);

    public SqliteConnection Connection => _connection;

    public static Category NewCategory(string brand = "Grass", string name = "Автошампуни") =>
        new() { Brand = new Brand { Name = brand }, Name = name };

    public static ApplicationUser NewUser(string userName = "buyer") =>
        new() { UserName = userName, NormalizedUserName = userName.ToUpperInvariant() };

    public void Dispose() => _connection.Dispose();
}
