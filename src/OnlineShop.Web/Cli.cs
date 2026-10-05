using Microsoft.AspNetCore.Identity;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using OnlineShop.Web.Services.Import;

namespace OnlineShop.Web;

/// <summary>
/// Команды для консоли вместо запуска сайта. Вызов: dotnet run --project src/OnlineShop.Web -- &lt;команда&gt;
/// <list type="bullet">
/// <item>--seed-demo: выдуманный каталог в пустую базу;</item>
/// <item>--make-manager &lt;логин&gt;: дать покупателю доступ в панель управления (аналог createsuperuser);</item>
/// <item>--import-1c --price-list &lt;файл&gt; --stock &lt;файл&gt; [--dry-run]: загрузка из 1С, как команда import_1c в Django.</item>
/// </list>
/// </summary>
public static class Cli
{
    /// <summary>Возвращает true, если в аргументах была команда и сайт запускать не нужно.</summary>
    public static async Task<bool> RunAsync(string[] args, IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        if (args.Contains("--seed-demo"))
        {
            Console.WriteLine(await DemoData.SeedAsync(provider.GetRequiredService<ShopDbContext>(), provider.GetRequiredService<MediaStorage>().Root));
            return true;
        }

        if (Value(args, "--make-manager") is { } login)
        {
            var users = provider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByNameAsync(login);
            if (user is null)
            {
                Console.WriteLine($"Покупатель «{login}» не найден. Сначала зарегистрируйтесь на сайте под этим логином.");
                Environment.ExitCode = 1;
            }
            else if (await users.IsInRoleAsync(user, RoleNames.Manager))
            {
                Console.WriteLine($"У «{login}» уже есть доступ в панель управления.");
            }
            else
            {
                await users.AddToRoleAsync(user, RoleNames.Manager);
                Console.WriteLine($"«{login}» теперь менеджер: панель управления по адресу /admin. Перезайдите на сайт, чтобы доступ появился.");
            }
            return true;
        }

        if (args.Contains("--import-1c"))
        {
            await ImportAsync(args, provider);
            return true;
        }
        return false;
    }

    private static async Task ImportAsync(string[] args, IServiceProvider provider)
    {
        var priceList = Value(args, "--price-list");
        var stock = Value(args, "--stock");
        var dryRun = args.Contains("--dry-run");
        if (priceList is null && stock is null)
        {
            Console.WriteLine("Укажите хотя бы один файл: --price-list и/или --stock");
            Environment.ExitCode = 1;
            return;
        }

        try
        {
            StockResult? stockResult = null;
            if (stock is not null)
            {
                stockResult = OneCParser.ParseStock(stock);
                Console.WriteLine($"Остатки: разобрано {stockResult.Quantities.Count} артикулов");
                PrintWarnings(stockResult.Warnings);
            }

            PriceListResult? priceResult = null;
            if (priceList is not null)
            {
                priceResult = OneCParser.ParsePriceList(priceList);
                var variants = priceResult.Products.Sum(p => p.Variants.Count);
                var photos = priceResult.Products.Count(p => p.ImageBytes is not null);
                Console.WriteLine($"Прайс-лист: разобрано {priceResult.Products.Count} товаров, {variants} артикулов, {photos} фото");
                Console.WriteLine("  колонки: " + string.Join(", ", priceResult.ColumnMap.Select(c => $"{c.Key}={c.Value}")));
                Console.WriteLine("  бренды: " + string.Join(", ", priceResult.Products.Select(p => p.Brand).Distinct().Order()));
                PrintWarnings(priceResult.Warnings);
                Console.WriteLine("  Первые 5 товаров:");
                foreach (var p in priceResult.Products.Take(5))
                {
                    Console.WriteLine($"    [{p.Brand}] {p.CategoryPath} :: {p.Name} -> "
                        + string.Join(", ", p.Variants.Select(v => $"{v.Article}({v.PackLabel})")));
                }
            }

            if (dryRun)
            {
                Console.WriteLine("Пробный запуск: в базу ничего не записано.");
                return;
            }

            var stats = new ImportStats();
            var applier = provider.GetRequiredService<ImportApplier>();
            if (priceResult is not null)
            {
                await applier.ApplyPriceListAsync(priceResult, stats);
            }
            if (stockResult is not null)
            {
                await applier.ApplyStockAsync(stockResult, stats);
            }
            Console.WriteLine($"Готово: {stats}.");
            if (stats.StockUnmatched.Count > 0)
            {
                Console.WriteLine($"Остатки без пары в каталоге ({stats.StockUnmatched.Count}): " + string.Join(", ", stats.StockUnmatched.Take(30)));
            }
        }
        catch (Exception e) when (e is ImportFormatException or FileNotFoundException or IOException)
        {
            Console.WriteLine("Не смог разобрать файл: " + e.Message);
            Environment.ExitCode = 1;
        }
    }

    private static void PrintWarnings(List<string> warnings)
    {
        foreach (var w in warnings.Take(20))
        {
            Console.WriteLine("  ! " + w);
        }
        if (warnings.Count > 20)
        {
            Console.WriteLine($"  и ещё {warnings.Count - 20} предупреждений");
        }
    }

    private static string? Value(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length && !args[i + 1].StartsWith("--") ? args[i + 1] : null;
    }
}
