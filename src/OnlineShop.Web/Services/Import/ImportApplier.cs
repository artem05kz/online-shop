using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services.Import;

/// <summary>Итог загрузки для страницы результата и консольной команды.</summary>
public class ImportStats
{
    public int Brands { get; set; }
    public int Categories { get; set; }
    public int ProductsCreated { get; set; }
    public int ProductsUpdated { get; set; }
    public int VariantsCreated { get; set; }
    public int VariantsUpdated { get; set; }
    public int ImagesAdded { get; set; }
    public int StockMatched { get; set; }
    public List<string> StockUnmatched { get; set; } = [];

    public override string ToString() =>
        $"брендов {Brands}, категорий {Categories}, товаров новых {ProductsCreated} и обновлено {ProductsUpdated}, "
        + $"артикулов новых {VariantsCreated} и обновлено {VariantsUpdated}, фото {ImagesAdded}, остатков {StockMatched}";
}

/// <summary>
/// Запись уже разобранной выгрузки 1С (см. <see cref="OneCParser"/>) в базу. Общая для страниц
/// импорта в панели и консольной команды, чтобы они не расходились. Перенесено из
/// catalog/importers/apply.py Django-версии.
/// </summary>
public class ImportApplier(ShopDbContext db, MediaStorage media)
{
    /// <summary>
    /// Прайс-лист одной транзакцией: создаёт и обновляет бренды, категории, товары и артикулы.
    /// Розничная цена не трогается, из 1С приходит только оптовая. Фото добавляется, только если
    /// у товара ещё нет ни одного, чтобы не затирать фото, загруженные менеджером руками.
    /// </summary>
    public async Task ApplyPriceListAsync(PriceListResult result, ImportStats stats)
    {
        var now = DateTime.UtcNow;
        var savedFiles = new List<string>();
        await using var transaction = await db.Database.BeginTransactionAsync();
        try
        {
            foreach (var parsed in result.Products)
            {
                var brand = await db.Brands.FirstOrDefaultAsync(b => b.Name == parsed.Brand);
                if (brand is null)
                {
                    brand = new Brand { Name = parsed.Brand };
                    db.Brands.Add(brand);
                    await db.SaveChangesAsync();
                    stats.Brands++;
                }

                var category = await db.Categories.FirstOrDefaultAsync(c =>
                    c.BrandId == brand.Id && c.ParentId == null && c.Name == parsed.CategoryPath);
                if (category is null)
                {
                    category = new Category { BrandId = brand.Id, Name = parsed.CategoryPath };
                    db.Categories.Add(category);
                    await db.SaveChangesAsync();
                    stats.Categories++;
                }

                var product = await db.Products
                    .Include(p => p.Images)
                    .FirstOrDefaultAsync(p => p.CategoryId == category.Id && p.Name == parsed.Name);
                if (product is null)
                {
                    product = new Product
                    {
                        CategoryId = category.Id,
                        Name = parsed.Name,
                        Description = parsed.Description,
                        Ph = parsed.Ph,
                        SourceGroupPath = parsed.SourceGroupPath,
                    };
                    db.Products.Add(product);
                    stats.ProductsCreated++;
                }
                else
                {
                    // Пустое описание или pH в файле не стирает то, что уже заполнено на сайте.
                    product.Description = parsed.Description.Length > 0 ? parsed.Description : product.Description;
                    product.Ph = parsed.Ph.Length > 0 ? parsed.Ph : product.Ph;
                    product.SourceGroupPath = parsed.SourceGroupPath;
                    product.UpdatedAt = now;
                    db.Entry(product).State = EntityState.Modified;
                    stats.ProductsUpdated++;
                }

                if (parsed.ImageBytes is { Length: > 0 } && product.Images.Count == 0)
                {
                    var ext = parsed.ImageExt.Length > 0 ? parsed.ImageExt : "jpg";
                    var path = await media.SaveAsync(parsed.ImageBytes, "products", $"{parsed.Key}.{ext}");
                    savedFiles.Add(path);
                    product.Images.Add(new ProductImage { ImagePath = path, IsPrimary = true });
                    stats.ImagesAdded++;
                }
                await db.SaveChangesAsync();

                foreach (var v in parsed.Variants.Where(v => v.Article.Length > 0))
                {
                    var variant = await db.ProductVariants.FirstOrDefaultAsync(x => x.Article == v.Article);
                    if (variant is null)
                    {
                        variant = new ProductVariant { Article = v.Article };
                        db.ProductVariants.Add(variant);
                        stats.VariantsCreated++;
                    }
                    else
                    {
                        stats.VariantsUpdated++;
                    }
                    // Артикул мог переехать к другому товару в 1С: переносим его следом.
                    variant.ProductId = product.Id;
                    variant.PackLabel = v.PackLabel;
                    variant.VolumeValue = v.VolumeValue;
                    variant.VolumeUnit = v.VolumeUnit;
                    variant.BoxQty = v.BoxQty;
                    variant.PalletQty = v.PalletQty;
                    variant.WholesalePrice = v.WholesalePrice;
                    variant.LastPriceImportAt = now;
                }
                await db.SaveChangesAsync();
            }
            await transaction.CommitAsync();
        }
        catch
        {
            // База откатилась целиком, значит и сохранённые фото больше никому не нужны.
            savedFiles.ForEach(media.Delete);
            throw;
        }
    }

    /// <summary>Остатки по артикулу. Артикулы, которых нет в каталоге, перечисляются в итоге.</summary>
    public async Task ApplyStockAsync(StockResult result, ImportStats stats)
    {
        var now = DateTime.UtcNow;
        var articles = result.Quantities.Keys.ToList();
        await using var transaction = await db.Database.BeginTransactionAsync();
        var variants = await db.ProductVariants.Where(v => articles.Contains(v.Article)).ToListAsync();
        foreach (var variant in variants)
        {
            variant.StockQty = result.Quantities[variant.Article];
            variant.LastStockImportAt = now;
            stats.StockMatched++;
        }
        await db.SaveChangesAsync();
        await transaction.CommitAsync();

        var matched = variants.Select(v => v.Article).ToHashSet();
        stats.StockUnmatched = result.Quantities.Keys.Where(a => !matched.Contains(a)).Order(StringComparer.Ordinal).ToList();
    }
}
