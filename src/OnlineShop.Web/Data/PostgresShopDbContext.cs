using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OnlineShop.Web.Data;

/// <summary>
/// Та же база магазина на PostgreSQL. Отдельный класс нужен только миграциям: у SQLite и PostgreSQL
/// разные типы столбцов, поэтому и наборы миграций разные (Data/Migrations и Data/PostgresMigrations).
/// </summary>
public sealed class PostgresShopDbContext(DbContextOptions<PostgresShopDbContext> options) : ShopDbContext(options);

/// <summary>Для команды dotnet ef: создать миграцию можно и без запущенного PostgreSQL.</summary>
public sealed class PostgresDesignTimeFactory : IDesignTimeDbContextFactory<PostgresShopDbContext>
{
    public PostgresShopDbContext CreateDbContext(string[] args) => new(
        new DbContextOptionsBuilder<PostgresShopDbContext>().UseNpgsql("Host=localhost;Database=shop").Options);
}
