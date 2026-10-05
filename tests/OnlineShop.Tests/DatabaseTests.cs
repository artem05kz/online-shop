using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Tests;

/// <summary>Схема базы и правила сохранения на настоящей SQLite.</summary>
public class DatabaseTests : IDisposable
{
    private readonly TestDb _testDb = new();

    public void Dispose() => _testDb.Dispose();

    [Fact]
    public void Migrations_match_the_model()
    {
        using var db = _testDb.Create();
        Assert.False(db.Database.HasPendingModelChanges());
        Assert.Empty(db.Database.GetPendingMigrations());
    }

    [Fact]
    public void Manager_role_is_seeded()
    {
        using var db = _testDb.Create();
        Assert.Equal("MANAGER", db.Roles.Single().NormalizedName);
    }

    [Fact]
    public void Slugs_are_filled_from_names_and_made_unique()
    {
        using (var db = _testDb.Create())
        {
            var category = TestDb.NewCategory();
            db.Products.AddRange(
                new Product { Category = category, Name = "Active Foam" },
                new Product { Category = category, Name = "Active Foam" },
                new Product { Category = TestDb.NewCategory("Ifoam", "Автошампуни"), Name = "Active   foam!" });
            db.SaveChanges();
        }

        using (var db = _testDb.Create())
        {
            Assert.Equal(["active-foam", "active-foam-2", "active-foam-3"],
                db.Products.OrderBy(p => p.Id).Select(p => p.Slug).ToList());
            Assert.Equal(["grass", "ifoam"], db.Brands.OrderBy(b => b.Id).Select(b => b.Slug).ToList());
            // Ярлык категории уникален только внутри бренда: у разных брендов он может совпадать.
            Assert.Equal(["автошампуни", "автошампуни"], db.Categories.OrderBy(c => c.Id).Select(c => c.Slug).ToList());

            // Новый товар с тем же названием после сохранения получает следующий свободный номер.
            db.Products.Add(new Product { CategoryId = db.Categories.First().Id, Name = "Active Foam" });
            db.SaveChanges();
            Assert.Equal("active-foam-4", db.Products.OrderBy(p => p.Id).Last().Slug);
        }
    }

    [Fact]
    public void Manual_slug_is_kept_and_ph_is_trimmed()
    {
        using var db = _testDb.Create();
        var product = new Product { Category = TestDb.NewCategory(), Name = "Gel", Slug = "my-gel", Ph = "  " };
        db.Products.Add(product);
        db.SaveChanges();

        Assert.Equal("my-gel", product.Slug);
        Assert.Equal("", product.Ph);
    }

    [Fact]
    public void Timestamps_are_set_in_utc_and_read_back_as_utc()
    {
        int id;
        DateTime created;
        using (var db = _testDb.Create())
        {
            var product = new Product { Category = TestDb.NewCategory(), Name = "Gel" };
            db.Products.Add(product);
            db.SaveChanges();
            id = product.Id;
            created = product.CreatedAt;
        }

        using (var db = _testDb.Create())
        {
            var product = db.Products.Single(p => p.Id == id);
            Assert.Equal(DateTimeKind.Utc, product.CreatedAt.Kind);
            Assert.Equal(created, product.CreatedAt);

            product.Name = "Gel 2";
            db.SaveChanges();
            Assert.Equal(created, product.CreatedAt);
            Assert.True(product.UpdatedAt >= created);
        }

        using (var db = _testDb.Create())
        {
            var variant = new ProductVariant { ProductId = id, Article = "1", LastStockImportAt = DateTime.UtcNow };
            db.ProductVariants.Add(variant);
            db.SaveChanges();
        }
        using (var db = _testDb.Create())
        {
            Assert.Equal(DateTimeKind.Utc, db.ProductVariants.Single().LastStockImportAt!.Value.Kind);
        }
    }

    [Fact]
    public void Prices_round_trip_exactly_and_support_sorting_and_filtering()
    {
        using (var db = _testDb.Create())
        {
            var category = TestDb.NewCategory();
            db.Products.AddRange(
                new Product
                {
                    Category = category, Name = "Дорогой",
                    Variants = [new ProductVariant { Article = "1", Price = 15990.99m, VolumeValue = 22m }],
                },
                new Product
                {
                    Category = category, Name = "Дешёвый",
                    Variants =
                    [
                        new ProductVariant { Article = "2", Price = 1200.5m, VolumeValue = 5.5m },
                        new ProductVariant { Article = "3", Price = 990m, VolumeValue = 0.75m },
                    ],
                },
                new Product { Category = category, Name = "Без цены", Variants = [new ProductVariant { Article = "4" }] });
            db.SaveChanges();
        }

        using (var db = _testDb.Create())
        {
            Assert.Equal(15990.99m, db.ProductVariants.Single(v => v.Article == "1").Price);
            Assert.Equal(0.75m, db.ProductVariants.Single(v => v.Article == "3").VolumeValue);

            // Сортировка «сначала дешевле» по минимальной цене фасовки, как в каталоге Django-версии.
            var byPrice = db.Products
                .OrderBy(p => p.Variants.Min(v => v.Price))
                .ThenBy(p => p.Name)
                .Select(p => p.Name)
                .ToList();
            Assert.Equal(["Без цены", "Дешёвый", "Дорогой"], byPrice);

            // Фильтр «цена от/до».
            var from1000 = db.Products
                .Where(p => p.Variants.Any(v => v.Price >= 1000m && v.Price <= 2000m))
                .Select(p => p.Name)
                .ToList();
            Assert.Equal(["Дешёвый"], from1000);

            // Фасовки по объёму, как Meta.ordering в Django: без объёма в начале, дальше по возрастанию.
            Assert.Equal(["4", "3", "2", "1"], db.ProductVariants.Ordered().Select(v => v.Article).ToList());
        }

        // В базе цена лежит целым числом тиынов.
        using var command = _testDb.Connection.CreateCommand();
        command.CommandText = "SELECT Price FROM ProductVariants WHERE Article = '1'";
        Assert.Equal(1599099L, command.ExecuteScalar());
    }

    [Fact]
    public void Article_must_be_unique()
    {
        using var db = _testDb.Create();
        var category = TestDb.NewCategory();
        db.Products.Add(new Product { Category = category, Name = "A", Variants = [new ProductVariant { Article = "131122" }] });
        db.Products.Add(new Product { Category = category, Name = "B", Variants = [new ProductVariant { Article = "131122" }] });

        var error = Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        Assert.Contains("UNIQUE", error.InnerException!.Message);
    }

    [Fact]
    public void Category_with_products_cannot_be_deleted()
    {
        using (var db = _testDb.Create())
        {
            db.Products.Add(new Product { Category = TestDb.NewCategory(), Name = "A" });
            db.SaveChanges();
        }

        using (var db = _testDb.Create())
        {
            db.Categories.Remove(db.Categories.Single());
            var error = Assert.Throws<DbUpdateException>(() => db.SaveChanges());
            Assert.IsType<SqliteException>(error.InnerException);
        }
    }

    [Fact]
    public void Order_status_and_store_kind_are_stored_as_django_codes()
    {
        using (var db = _testDb.Create())
        {
            var user = TestDb.NewUser();
            var variant = new ProductVariant
            {
                Article = "1", Price = 100m,
                Product = new Product { Category = TestDb.NewCategory(), Name = "A" },
            };
            db.Orders.Add(new Order
            {
                User = user, Status = OrderStatus.Delivering, ContactName = "Иван", ContactPhone = "+7",
                City = "Актау", StreetAndDetails = "14 мкр",
                Items = [new OrderItem { Variant = variant, ProductName = "A", Price = 100m, Quantity = 2 }],
            });
            db.Stores.Add(new Store { Address = "21 мкр", Kind = StoreKind.Warehouse });
            db.SaveChanges();
        }

        using var command = _testDb.Connection.CreateCommand();
        command.CommandText = "SELECT (SELECT Status FROM Orders) || ',' || (SELECT Kind FROM Stores)";
        Assert.Equal("delivering,warehouse", command.ExecuteScalar());

        using var db2 = _testDb.Create();
        var order = db2.Orders.Include(o => o.Items).Single();
        Assert.Equal(OrderStatus.Delivering, order.Status);
        Assert.Equal(200m, order.Total);
    }

    [Fact]
    public void Visible_banners_respect_active_flag_and_dates()
    {
        var now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        using (var db = _testDb.Create())
        {
            db.Banners.AddRange(
                new Banner { Title = "всегда", ImagePath = "a.jpg", SortOrder = 2 },
                new Banner { Title = "выключен", ImagePath = "b.jpg", IsActive = false },
                new Banner { Title = "ещё не начался", ImagePath = "c.jpg", StartsAt = now.AddDays(1) },
                new Banner { Title = "закончился", ImagePath = "d.jpg", EndsAt = now.AddDays(-1) },
                new Banner { Title = "идёт сейчас", ImagePath = "e.jpg", SortOrder = 1, StartsAt = now.AddDays(-1), EndsAt = now.AddDays(1) });
            db.SaveChanges();
        }

        using (var db = _testDb.Create())
        {
            Assert.Equal(["идёт сейчас", "всегда"],
                db.Banners.VisibleAt(now).Ordered().Select(b => b.Title).ToList());
        }
    }

    [Fact]
    public void Deleting_user_removes_addresses_but_is_blocked_by_orders()
    {
        using (var db = _testDb.Create())
        {
            var user = TestDb.NewUser();
            user.Addresses.Add(new Address { FullName = "Иван", Phone = "+7", Street = "14 мкр" });
            db.Users.Add(user);
            db.SaveChanges();

            db.Users.Remove(user);
            db.SaveChanges();
            Assert.Empty(db.Addresses);
        }

        using (var db = _testDb.Create())
        {
            var user = TestDb.NewUser("with-order");
            db.Orders.Add(new Order { User = user, ContactName = "Иван", ContactPhone = "+7", City = "Актау", StreetAndDetails = "1" });
            db.SaveChanges();
        }

        // Заказы в контекст не загружены, поэтому удаление останавливает сама база, а не EF.
        using (var db = _testDb.Create())
        {
            db.Users.Remove(db.Users.Single());
            Assert.Throws<DbUpdateException>(() => db.SaveChanges());
        }
    }
}
