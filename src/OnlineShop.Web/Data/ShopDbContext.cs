using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Data;

public class ShopDbContext(DbContextOptions<ShopDbContext> options)
    : IdentityDbContext<ApplicationUser, IdentityRole<int>, int>(options)
{
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<ProductVariant> ProductVariants => Set<ProductVariant>();
    public DbSet<ProductImage> ProductImages => Set<ProductImage>();
    public DbSet<Banner> Banners => Set<Banner>();
    public DbSet<Store> Stores => Set<Store>();
    public DbSet<SiteContact> SiteContacts => Set<SiteContact>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<TelegramRecipient> TelegramRecipients => Set<TelegramRecipient>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // SQLite не хранит признак часового пояса, и после чтения DateTime приходит с Kind=Unspecified.
        // Все даты в базе в UTC, поэтому при чтении помечаем их так явно, а местное время Актау
        // получаем уже при выводе на страницу.
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Brand>(e =>
        {
            e.HasIndex(b => b.Name).IsUnique();
            e.HasIndex(b => b.Slug).IsUnique();
        });

        builder.Entity<Category>(e =>
        {
            e.HasOne(c => c.Brand).WithMany(b => b.Categories).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(c => c.Parent).WithMany(c => c.Children).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(c => new { c.BrandId, c.ParentId, c.Name }).IsUnique();
            e.HasIndex(c => new { c.BrandId, c.Slug }).IsUnique();
        });

        builder.Entity<Product>(e =>
        {
            // Товар с заказами или артикулами нельзя потерять вместе с категорией: удаление категории блокируется.
            e.HasOne(p => p.Category).WithMany(c => c.Products).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(p => p.Slug).IsUnique();
            e.HasIndex(p => p.Name);
        });

        builder.Entity<ProductVariant>(e =>
        {
            e.HasOne(v => v.Product).WithMany(p => p.Variants).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(v => v.Article).IsUnique();
        });

        builder.Entity<ProductImage>(e =>
        {
            e.HasOne(i => i.Product).WithMany(p => p.Images).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Store>(e =>
        {
            e.Property(s => s.Kind)
                .HasConversion(k => k.ToCode(), c => StoreKindInfo.FromCode(c))
                .HasMaxLength(20);
        });

        builder.Entity<Address>(e =>
        {
            e.HasOne(a => a.User).WithMany(u => u.Addresses).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<Order>(e =>
        {
            // Покупателя с заказами удалить нельзя, иначе пропадёт история продаж.
            e.HasOne(o => o.User).WithMany(u => u.Orders).OnDelete(DeleteBehavior.Restrict);
            e.Property(o => o.Status)
                .HasConversion(s => s.ToCode(), c => OrderStatusInfo.FromCode(c))
                .HasMaxLength(20);
            e.HasIndex(o => o.CreatedAt);
        });

        builder.Entity<OrderItem>(e =>
        {
            e.HasOne(i => i.Order).WithMany(o => o.Items).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(i => i.Variant).WithMany(v => v.OrderItems).OnDelete(DeleteBehavior.Restrict);
        });

        builder.Entity<TelegramRecipient>(e =>
        {
            e.HasIndex(r => r.Phone).IsUnique();
            e.HasIndex(r => r.ChatId);
        });

        builder.Entity<IdentityRole<int>>().HasData(new IdentityRole<int>
        {
            Id = 1,
            Name = RoleNames.Manager,
            NormalizedName = RoleNames.Manager.ToUpperInvariant(),
            ConcurrencyStamp = "5f0c7a52-3c1e-4d55-9a7b-1f6b2d1e8c01",
        });

        var lower = builder.HasDbFunction(typeof(SqliteFunctions).GetMethod(nameof(SqliteFunctions.Lower))!);
        if (Database.IsSqlite())
        {
            lower.HasName(SqliteFunctions.LowerName);
        }
        else
        {
            lower.HasName("lower").IsBuiltIn();
        }

        ConfigureDecimals(builder);
    }

    private void ConfigureDecimals(ModelBuilder builder)
    {
        if (!Database.IsSqlite())
        {
            builder.Entity<ProductVariant>(e =>
            {
                e.Property(v => v.VolumeValue).HasPrecision(10, 3);
                e.Property(v => v.WholesalePrice).HasPrecision(12, 2);
                e.Property(v => v.Price).HasPrecision(12, 2);
                e.Property(v => v.OldPrice).HasPrecision(12, 2);
            });
            builder.Entity<OrderItem>().Property(i => i.Price).HasPrecision(12, 2);
            return;
        }

        // SQLite хранит decimal строкой и не умеет по ней сортировать и сравнивать, а каталогу
        // нужны сортировка по цене и фильтр «цена от/до». Поэтому цены лежат целым числом тиынов
        // (сотых тенге), а объём целым числом тысячных: так сравнение идёт по числам и без потери точности.
        var money = new ValueConverter<decimal, long>(
            v => (long)decimal.Round(v * 100m, MidpointRounding.AwayFromZero),
            v => v / 100m);
        var volume = new ValueConverter<decimal, long>(
            v => (long)decimal.Round(v * 1000m, MidpointRounding.AwayFromZero),
            v => v / 1000m);

        builder.Entity<ProductVariant>(e =>
        {
            e.Property(v => v.VolumeValue).HasConversion(volume);
            e.Property(v => v.WholesalePrice).HasConversion(money);
            e.Property(v => v.Price).HasConversion(money);
            e.Property(v => v.OldPrice).HasConversion(money);
        });
        builder.Entity<OrderItem>().Property(i => i.Price).HasConversion(money);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplySaveRules();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        ApplySaveRules();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// То, что в Django-версии делали методы save() моделей: ярлыки из названий,
    /// даты создания и изменения, очистка pH от случайных пробелов.
    /// </summary>
    private void ApplySaveRules()
    {
        ChangeTracker.DetectChanges();
        var now = DateTime.UtcNow;
        var entries = ChangeTracker.Entries()
            .Where(e => e.State is EntityState.Added or EntityState.Modified)
            .ToList();

        // Ярлыки, уже занятые в этом сохранении, но ещё не записанные в базу.
        // Ключ задаёт область уникальности: все бренды, категории одного бренда, все товары.
        var pending = new Dictionary<string, HashSet<string>>();
        foreach (var entry in entries)
        {
            var (scope, slug) = entry.Entity switch
            {
                Brand b => ("brand", b.Slug),
                Category c => (CategoryScope(entry), c.Slug),
                Product p => ("product", p.Slug),
                _ => ("", ""),
            };
            if (slug.Length > 0)
            {
                PendingIn(pending, scope).Add(slug);
            }
        }

        foreach (var entry in entries)
        {
            switch (entry.Entity)
            {
                case Brand brand when brand.Slug.Length == 0:
                    brand.Slug = AssignSlug(pending, "brand", brand.Name, "brand", 110,
                        Brands.Where(b => b.Id != brand.Id).Select(b => b.Slug));
                    break;

                case Category category when category.Slug.Length == 0:
                    var brandId = (int)entry.Property(nameof(Category.BrandId)).CurrentValue!;
                    category.Slug = AssignSlug(pending, CategoryScope(entry), category.Name, "category", 220,
                        Categories.Where(c => c.BrandId == brandId && c.Id != category.Id).Select(c => c.Slug));
                    break;

                case Product product:
                    if (product.Slug.Length == 0)
                    {
                        product.Slug = AssignSlug(pending, "product", product.Name, "product", Product.SlugMaxLength,
                            Products.Where(p => p.Id != product.Id).Select(p => p.Slug));
                    }
                    // Случайный пробел в поле pH не должен считаться значением, иначе на сайте
                    // появится пустая строка «pH:» вместо того, чтобы её скрыть.
                    product.Ph = product.Ph.Trim();
                    if (entry.State == EntityState.Added)
                    {
                        product.CreatedAt = now;
                    }
                    product.UpdatedAt = now;
                    break;

                case Order order:
                    if (entry.State == EntityState.Added)
                    {
                        order.CreatedAt = now;
                    }
                    order.UpdatedAt = now;
                    break;
            }
        }
    }

    // У нового, ещё не сохранённого бренда ключ временный и в сам объект не записывается,
    // поэтому берём его из трекера изменений: иначе категории разных новых брендов попали бы в одну область.
    private static string CategoryScope(EntityEntry entry) =>
        $"category:{entry.Property(nameof(Category.BrandId)).CurrentValue}";

    private static HashSet<string> PendingIn(Dictionary<string, HashSet<string>> pending, string scope)
    {
        if (!pending.TryGetValue(scope, out var set))
        {
            pending[scope] = set = [];
        }
        return set;
    }

    private static string AssignSlug(
        Dictionary<string, HashSet<string>> pending, string scope, string name, string fallback,
        int maxLength, IQueryable<string> existingInScope)
    {
        var baseSlug = Slug.From(name, maxLength);
        if (baseSlug.Length == 0)
        {
            // Название целиком из знаков препинания: ярлык всё равно нужен, иначе не будет адреса страницы.
            baseSlug = fallback;
        }
        var prefix = baseSlug + "-";
        var taken = existingInScope
            .Where(s => s == baseSlug || s.StartsWith(prefix))
            .ToHashSet();
        var used = PendingIn(pending, scope);
        taken.UnionWith(used);

        var slug = Slug.MakeUnique(baseSlug, taken, maxLength);
        used.Add(slug);
        return slug;
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Local ? v.ToUniversalTime() : v,
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));
}
