using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using OnlineShop.Web.Services.Import;

namespace OnlineShop.Tests;

/// <summary>Разбор выгрузок 1С и запись в базу.</summary>
public class ImportTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("online-shop-import-").FullName;
    private readonly TestDb _testDb = new();

    public void Dispose()
    {
        _testDb.Dispose();
        Directory.Delete(_dir, recursive: true);
    }

    private MediaStorage Media => new(Path.Combine(_dir, "media"));

    [Theory]
    [InlineData(131122d, "131122")]
    [InlineData("  131122 ", "131122")]
    [InlineData(12.5d, "12.5")]
    [InlineData(null, "")]
    public void Articles_are_normalized(object? raw, string expected) =>
        Assert.Equal(expected, OneCParser.NormalizeArticle(raw));

    [Theory]
    [InlineData("22 кг", 22, "кг")]
    [InlineData("5,5 кг", 5.5, "кг")]
    [InlineData("600мл", 600, "мл")]
    public void Pack_labels_are_split_into_number_and_unit(string raw, double value, string unit)
    {
        var (v, u) = OneCParser.ParsePackLabel(raw);
        Assert.Equal((decimal)value, v);
        Assert.Equal(unit, u);
    }

    [Fact]
    public void Pack_label_without_number_keeps_text_as_unit() =>
        Assert.Equal((null, "шт"), OneCParser.ParsePackLabel("шт"));

    [Theory]
    [InlineData(" Автошампуни для ручной мойки ifoam", "Ifoam", "Автошампуни для ручной мойки")]
    [InlineData("Очистители ГРАСС GRASS", "Grass", "Очистители ГРАСС")]
    [InlineData("Средства без бренда", OneCParser.UnknownBrand, "Средства без бренда")]
    public void Brand_is_taken_from_end_of_group_name(string header, string brand, string category) =>
        Assert.Equal((brand, category), OneCParser.GuessBrandAndCategory(header));

    [Fact]
    public void Price_list_is_parsed_like_the_django_version()
    {
        var result = OneCParser.ParsePriceList(OneCFiles.PriceList(_dir));

        Assert.Equal(["Пробный товар без группы", "Active Foam Pink", "Очиститель дисков", "Губка"],
            result.Products.Select(p => p.Name));
        var warning = Assert.Single(result.Warnings);
        Assert.Contains("без заголовка категории", warning);

        var foam = result.Products[1];
        Assert.Equal(("Ifoam", "Автошампуни для ручной мойки"), (foam.Brand, foam.CategoryPath));
        // Как в Django: строка группы сохраняется уже без пробелов по краям.
        Assert.Equal("Автошампуни для ручной мойки ifoam", foam.SourceGroupPath);
        Assert.Equal(("Пена для бесконтактной мойки", "12,5"), (foam.Description, foam.Ph));
        Assert.Equal("131122", foam.Key);
        Assert.NotNull(foam.ImageBytes);
        Assert.Equal("png", foam.ImageExt);

        Assert.Equal(
            [
                new ParsedVariant("131122", "22 кг", 22m, "кг", 1, 32, 15990.5m),
                new ParsedVariant("131123", "5,5 кг", 5.5m, "кг", 4, 128, 4990m),
                new ParsedVariant("131124", "1 л", 1m, "л", 12, 720, null),
            ],
            foam.Variants);

        Assert.Equal(("Grass", "Очистители ГРАСС"), (result.Products[2].Brand, result.Products[2].CategoryPath));
        Assert.Null(result.Products[2].ImageBytes);
        Assert.Equal((OneCParser.UnknownBrand, "Без категории"), (result.Products[0].Brand, result.Products[0].CategoryPath));
        Assert.Equal(new ParsedVariant("777", "шт", null, "шт", null, null, 150m), result.Products[3].Variants.Single());
        Assert.Equal(5, result.ColumnMap["article"]);
        Assert.Equal(9, result.ColumnMap["price"]);
    }

    [Fact]
    public void Name_in_second_row_of_product_block_still_starts_the_product()
    {
        var result = OneCParser.ParsePriceList(OneCFiles.PriceListWithShiftedName(_dir));

        Assert.Empty(result.Warnings);
        Assert.Equal(["GLASS GLOSS", "MOTOR SHINE"], result.Products.Select(p => p.Name));
        Assert.Equal(["202105", "202101"], result.Products[0].Variants.Select(v => v.Article));
        var motor = result.Products[1];
        Assert.Equal(["206122", "206105", "206101"], motor.Variants.Select(v => v.Article));
        Assert.Equal(("Для двигателя", "pH = 12"), (motor.Description, motor.Ph));
        Assert.NotNull(motor.ImageBytes);
    }

    [Fact]
    public void Same_product_name_twice_is_warned_about()
    {
        var path = OneCFiles.PriceListWithShiftedName(_dir);
        using (var wb = new ClosedXML.Excel.XLWorkbook(path))
        {
            var ws = wb.Worksheets.First();
            ws.Cell(9, 1).Value = "MOTOR SHINE";
            ws.Cell(9, 3).Value = "Готовый к применению";
            ws.Cell(9, 5).Value = 206250;
            ws.Cell(9, 6).Value = "500 мл";
            wb.Save();
        }
        var warning = Assert.Single(OneCParser.ParsePriceList(path).Warnings);
        Assert.Contains("«MOTOR SHINE» встречается в файле 2 раза (артикулы 206122, 206105, 206101; 206250)", warning);
    }

    [Fact]
    public void Stock_sums_duplicates_and_warns_about_non_numbers()
    {
        var result = OneCParser.ParseStock(OneCFiles.Stock(_dir));

        Assert.Equal(new Dictionary<string, int> { ["131122"] = 7, ["131123"] = 9, ["X-1"] = 0, ["555555"] = 3 },
            result.Quantities);
        Assert.Equal(2, result.Warnings.Count);
        Assert.Contains(result.Warnings, w => w.Contains("дважды"));
        Assert.Contains(result.Warnings, w => w.Contains("не число"));
    }

    [Fact]
    public void Wrong_file_gives_readable_error()
    {
        var error = Assert.Throws<ImportFormatException>(() => OneCParser.ParsePriceList(OneCFiles.Stock(_dir)));
        Assert.Contains("название", error.Message);
    }

    [Fact]
    public async Task Price_list_creates_catalog_and_second_run_updates_it()
    {
        var parsed = OneCParser.ParsePriceList(OneCFiles.PriceList(_dir));
        var stats = new ImportStats();
        using (var db = _testDb.Create())
        {
            await new ImportApplier(db, Media).ApplyPriceListAsync(parsed, stats);
        }

        Assert.Equal((3, 4, 4, 0, 6, 0, 1), (stats.Brands, stats.Categories, stats.ProductsCreated, stats.ProductsUpdated,
            stats.VariantsCreated, stats.VariantsUpdated, stats.ImagesAdded));

        using (var db = _testDb.Create())
        {
            var foam = await db.Products.Include(p => p.Variants).Include(p => p.Images).Include(p => p.Category.Brand)
                .SingleAsync(p => p.Name == "Active Foam Pink");
            Assert.Equal(("Ifoam", "Автошампуни для ручной мойки"), (foam.Category.Brand.Name, foam.Category.Name));
            var variant = foam.Variants.Single(v => v.Article == "131122");
            Assert.Equal(15990.5m, variant.WholesalePrice);
            Assert.Null(variant.Price);
            Assert.NotNull(variant.LastPriceImportAt);
            var image = Assert.Single(foam.Images);
            Assert.True(image.IsPrimary);
            Assert.True(File.Exists(Path.Combine(Media.Root, image.ImagePath)));

            // Менеджер поставил розничную цену и описание руками.
            variant.Price = 18990m;
            foam.Description = "Описание менеджера";
            await db.SaveChangesAsync();
        }

        // Повторная загрузка того же файла: ничего не дублирует, розничную цену не трогает.
        var again = new ImportStats();
        using (var db = _testDb.Create())
        {
            await new ImportApplier(db, Media).ApplyPriceListAsync(OneCParser.ParsePriceList(OneCFiles.PriceList(_dir)), again);
        }
        Assert.Equal((0, 0, 0, 4, 0, 6, 0), (again.Brands, again.Categories, again.ProductsCreated, again.ProductsUpdated,
            again.VariantsCreated, again.VariantsUpdated, again.ImagesAdded));
        using (var db = _testDb.Create())
        {
            var foam = await db.Products.Include(p => p.Variants).Include(p => p.Images).SingleAsync(p => p.Name == "Active Foam Pink");
            Assert.Equal(18990m, foam.Variants.Single(v => v.Article == "131122").Price);
            // В файле описание есть, поэтому оно снова приходит из 1С, как в Django-версии.
            Assert.Equal("Пена для бесконтактной мойки", foam.Description);
            Assert.Single(foam.Images);
            Assert.Equal(4, await db.Products.CountAsync());
        }
    }

    [Fact]
    public async Task Stock_updates_known_articles_and_lists_unknown()
    {
        using (var db = _testDb.Create())
        {
            await new ImportApplier(db, Media).ApplyPriceListAsync(OneCParser.ParsePriceList(OneCFiles.PriceList(_dir)), new ImportStats());
        }

        var stats = new ImportStats();
        using (var db = _testDb.Create())
        {
            await new ImportApplier(db, Media).ApplyStockAsync(OneCParser.ParseStock(OneCFiles.Stock(_dir)), stats);
        }

        Assert.Equal(2, stats.StockMatched);
        Assert.Equal(["555555", "X-1"], stats.StockUnmatched);
        using (var db = _testDb.Create())
        {
            var stock = await db.ProductVariants.Where(v => v.Article.StartsWith("1311"))
                .ToDictionaryAsync(v => v.Article, v => (v.StockQty, v.LastStockImportAt != null));
            Assert.Equal((7, true), stock["131122"]);
            Assert.Equal((9, true), stock["131123"]);
            Assert.Equal((0, false), stock["131124"]);
        }
    }

    [Fact]
    public async Task Article_moved_to_another_product_in_1c_follows_it()
    {
        using (var db = _testDb.Create())
        {
            db.Products.Add(new Product
            {
                Category = TestDb.NewCategory("Старый бренд", "Старая категория"), Name = "Старое название",
                Variants = [new ProductVariant { Article = "125333", Price = 1500m }],
            });
            await db.SaveChangesAsync();
        }
        using (var db = _testDb.Create())
        {
            await new ImportApplier(db, Media).ApplyPriceListAsync(OneCParser.ParsePriceList(OneCFiles.PriceList(_dir)), new ImportStats());
        }
        using (var db = _testDb.Create())
        {
            var variant = await db.ProductVariants.Include(v => v.Product).SingleAsync(v => v.Article == "125333");
            Assert.Equal("Очиститель дисков", variant.Product.Name);
            Assert.Equal(1500m, variant.Price);
        }
    }
}
