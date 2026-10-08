using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Data;

/// <summary>
/// Перенос всей базы магазина в другую, например из файла SQLite в PostgreSQL перед выкладкой.
/// Записи переезжают с прежними номерами, ярлыками и датами: ссылки на товары и номера заказов не меняются.
/// </summary>
public static class DatabaseCopier
{
    public static async Task<string> CopyAsync(ShopDbContext source, ShopDbContext target)
    {
        if (await target.Users.AnyAsync() || await target.Products.AnyAsync() || await target.Brands.AnyAsync())
        {
            throw new InvalidOperationException("В базе, куда переносим, уже есть данные. Перенос делается только в пустую базу.");
        }

        target.KeepValuesAsIs = true;
        var counts = new List<string>();
        var copied = new HashSet<Type>();
        await using var transaction = await target.Database.BeginTransactionAsync();

        async Task Copy<T>(string label, Func<List<T>, IEnumerable<T>>? order = null, Func<T, bool>? skip = null) where T : class
        {
            var rows = await source.Set<T>().AsNoTracking().ToListAsync();
            var toAdd = (order is null ? rows : order(rows)).Where(r => skip is null || !skip(r)).ToList();
            // Категории ссылаются друг на друга, поэтому сохраняем по одной группе за раз в заданном порядке.
            foreach (var chunk in toAdd.Chunk(500))
            {
                target.Set<T>().AddRange(chunk);
                await target.SaveChangesAsync();
                target.ChangeTracker.Clear();
            }
            copied.Add(typeof(T));
            if (label.Length > 0)
            {
                counts.Add($"{label} {toAdd.Count}");
            }
        }

        // Роль «Менеджер» новая база получает из миграции, её не дублируем.
        var existingRoles = await target.Roles.Select(r => r.Id).ToListAsync();
        await Copy<IdentityRole<int>>("", skip: r => existingRoles.Contains(r.Id));
        await Copy<IdentityRoleClaim<int>>("");
        await Copy<ApplicationUser>("покупателей");
        await Copy<IdentityUserClaim<int>>("");
        await Copy<IdentityUserLogin<int>>("");
        await Copy<IdentityUserToken<int>>("");
        await Copy<IdentityUserRole<int>>("");
        await Copy<Brand>("брендов");
        await Copy<Category>("категорий", ParentsFirst);
        await Copy<Product>("товаров");
        await Copy<ProductVariant>("фасовок");
        await Copy<ProductImage>("фото");
        await Copy<Banner>("баннеров");
        await Copy<Store>("магазинов");
        await Copy<SiteContact>("");
        await Copy<Address>("адресов");
        await Copy<Order>("заказов");
        await Copy<OrderItem>("");
        await Copy<TelegramRecipient>("номеров Telegram");
        await Copy<Favorite>("");
        await Copy<CartItem>("позиций в корзинах");
        await Copy<Review>("отзывов");
        // Ключи входа: после переноса покупатели и менеджеры остаются в аккаунтах.
        await Copy<DataProtectionKey>("");

        // Таблица, которую здесь забыли, означала бы молча потерянные данные.
        var missed = target.Model.GetEntityTypes().Select(e => e.ClrType).Where(t => !copied.Contains(t)).ToList();
        if (missed.Count > 0)
        {
            throw new InvalidOperationException("Перенос не знает таблиц: " + string.Join(", ", missed.Select(t => t.Name)));
        }

        if (target.Database.IsNpgsql())
        {
            await ResetSequencesAsync(target);
        }
        await transaction.CommitAsync();
        return "Перенесено: " + string.Join(", ", counts) + ".";
    }

    private static IEnumerable<Category> ParentsFirst(List<Category> categories)
    {
        var byId = categories.ToDictionary(c => c.Id);
        int Depth(Category c) => c.ParentId is { } parent && byId.TryGetValue(parent, out var p) ? Depth(p) + 1 : 0;
        return categories.OrderBy(Depth).ThenBy(c => c.Id);
    }

    /// <summary>
    /// Номера переехали готовыми, а счётчик номеров в PostgreSQL об этом не знает и начал бы с 1:
    /// первый же новый заказ упёрся бы в занятый номер.
    /// </summary>
    private static async Task ResetSequencesAsync(ShopDbContext db)
    {
        foreach (var entity in db.Model.GetEntityTypes())
        {
            var key = entity.FindPrimaryKey();
            if (key is not { Properties: [{ ClrType: var type } id] } || type != typeof(int) || id.ValueGenerated == Microsoft.EntityFrameworkCore.Metadata.ValueGenerated.Never)
            {
                continue;
            }
            var table = entity.GetTableName();
            var column = id.GetColumnName();
#pragma warning disable EF1002 // Имена таблиц и столбцов берутся из модели, а не из ввода пользователя.
            await db.Database.ExecuteSqlRawAsync(
                $"SELECT setval(pg_get_serial_sequence('\"{table}\"', '{column}'), COALESCE(MAX(\"{column}\"), 1), MAX(\"{column}\") IS NOT NULL) FROM \"{table}\"");
#pragma warning restore EF1002
        }
    }
}
