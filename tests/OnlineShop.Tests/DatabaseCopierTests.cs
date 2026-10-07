using Microsoft.EntityFrameworkCore;
using Npgsql;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Tests;

/// <summary>
/// Перенос базы из SQLite. Если задана SHOP_TEST_POSTGRES, переносим в настоящий PostgreSQL,
/// как перед выкладкой; иначе во вторую базу SQLite.
/// </summary>
public sealed class DatabaseCopierTests : IDisposable
{
    private readonly TestDb _source = new();
    private readonly TestDb? _sqliteTarget;
    private readonly string? _postgres;

    public DatabaseCopierTests()
    {
        var server = Environment.GetEnvironmentVariable(TempShopFactory.PostgresVariable);
        if (string.IsNullOrEmpty(server))
        {
            _sqliteTarget = new TestDb();
            return;
        }
        _postgres = new NpgsqlConnectionStringBuilder(server) { Database = "shop_copy_" + Guid.NewGuid().ToString("N")[..12] }.ConnectionString;
        using var db = Target();
        db.Database.Migrate();
    }

    private ShopDbContext Target() => _postgres is null
        ? _sqliteTarget!.Create()
        : new PostgresShopDbContext(new DbContextOptionsBuilder<PostgresShopDbContext>().UseNpgsql(_postgres).Options);

    public void Dispose()
    {
        _source.Dispose();
        _sqliteTarget?.Dispose();
        if (_postgres is not null)
        {
            using var db = Target();
            NpgsqlConnection.ClearAllPools();
            db.Database.EnsureDeleted();
        }
    }

    [Fact]
    public async Task Everything_moves_with_same_numbers_slugs_and_dates()
    {
        var created = new DateTime(2025, 3, 14, 9, 26, 53, DateTimeKind.Utc);
        int orderId, productId, childId;
        await using (var db = _source.Create())
        {
            var user = TestDb.NewUser("перенос");
            var root = TestDb.NewCategory("Ifoam", "Автошампуни");
            var child = new Category { Brand = root.Brand, Parent = root, Name = "Для моек" };
            var variant = new ProductVariant { Article = "231101", PackLabel = "1 л", Price = 1890.5m, VolumeValue = 0.5m, StockQty = 3 };
            var product = new Product { Category = child, Name = "Шампунь «Тест»", Variants = [variant], Images = [new ProductImage { ImagePath = "products/a.webp", IsPrimary = true }] };
            db.Users.Add(user);
            db.Products.Add(product);
            db.SaveChanges();
            var order = new Order
            {
                User = user, ContactName = "Иван", ContactPhone = "+7 700", City = "Актау", StreetAndDetails = "14 мкр",
                Items = [new OrderItem { VariantId = variant.Id, ProductName = product.Name, Price = 1890.5m, Quantity = 2 }],
            };
            db.Orders.Add(order);
            db.Favorites.Add(new Favorite { UserId = user.Id, ProductId = product.Id, CreatedAt = created });
            db.TelegramRecipients.Add(new TelegramRecipient { Phone = "77010000001", ChatId = 42, CreatedAt = created });
            db.UserRoles.Add(new() { UserId = user.Id, RoleId = 1 });
            db.SaveChanges();
            // Дата из прошлого, как у старого заказа: при переносе она не должна стать сегодняшней.
            db.Database.ExecuteSql($"UPDATE Orders SET CreatedAt = {created}, UpdatedAt = {created}");
            (orderId, productId, childId) = (order.Id, product.Id, child.Id);
        }

        string report;
        await using (var source = _source.Create())
        await using (var target = Target())
        {
            report = await DatabaseCopier.CopyAsync(source, target);
        }

        Assert.Contains("товаров 1", report);
        Assert.Contains("заказов 1", report);
        await using (var db = Target())
        {
            var order = db.Orders.Include(o => o.Items).Include(o => o.User).Single();
            Assert.Equal((orderId, created, created), (order.Id, order.CreatedAt, order.UpdatedAt));
            Assert.Equal((1890.5m, 2, 3781m), (order.Items[0].Price, order.Items[0].Quantity, order.Total));
            var product = db.Products.Include(p => p.Variants).Include(p => p.Category).ThenInclude(c => c.Parent).Single();
            Assert.Equal((productId, "шампунь-тест", childId, "Автошампуни"), (product.Id, product.Slug, product.CategoryId, product.Category.Parent!.Name));
            Assert.Equal(0.5m, product.Variants[0].VolumeValue);
            Assert.True(db.UserRoles.Any(r => r.UserId == order.UserId && r.RoleId == 1), "Роль менеджера не потерялась.");
            Assert.Equal(42, db.TelegramRecipients.Single().ChatId);
            Assert.Single(db.Favorites);
            Assert.Single(db.Roles);

            // Счётчики номеров продолжаются после перенесённых: новая запись не упирается в занятый номер.
            db.Brands.Add(new Brand { Name = "Новый бренд" });
            db.SaveChanges();
            Assert.True(db.Brands.Max(b => b.Id) > 1);
        }

        // Повторный перенос в непустую базу запрещён: иначе получились бы дубли.
        await using (var source = _source.Create())
        await using (var target = Target())
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => DatabaseCopier.CopyAsync(source, target));
            Assert.Contains("уже есть данные", error.Message);
        }
    }
}
