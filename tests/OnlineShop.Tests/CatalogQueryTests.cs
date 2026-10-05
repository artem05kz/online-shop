using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Tests;

/// <summary>Поиск, фильтры, сортировка и страницы каталога на настоящей SQLite.</summary>
public class CatalogQueryTests : IDisposable
{
    private readonly TestDb _testDb = new();

    public CatalogQueryTests()
    {
        using var db = _testDb.Create();
        var shampoo = new Category { Brand = new Brand { Name = "Grass" }, Name = "Автошампуни" };
        var foam = new Category { Brand = new Brand { Name = "Ifoam" }, Name = "Пена" };

        db.Products.AddRange(
            new Product
            {
                Category = shampoo, Name = "Гель Альфа", Description = "Мягкий ГЕЛЬ",
                Variants = [Variant("A1", 500m, 0), Variant("A2", 5000m, 3)],
            },
            new Product
            {
                Category = shampoo, Name = "Шампунь Бета",
                Variants = [Variant("B1", 1500m, 0), Variant("B2", 1200m, 9, active: false)],
            },
            new Product { Category = foam, Name = "Пена Гамма", Variants = [Variant("G-77", null, 4)] },
            new Product { Category = foam, Name = "Пена скрытая", IsActive = false, Variants = [Variant("H1", 100m, 1)] });
        db.SaveChanges();
    }

    public void Dispose() => _testDb.Dispose();

    private static ProductVariant Variant(string article, decimal? price, int stock, bool active = true) =>
        new() { Article = article, Price = price, StockQty = stock, IsActive = active };

    private List<string> Names(Func<IQueryable<Product>, IQueryable<Product>> query)
    {
        using var db = _testDb.Create();
        return query(db.Products.ForListing()).Select(p => p.Name).ToList();
    }

    private static CatalogFilter Filter(params (string Key, string Value)[] pairs) =>
        CatalogFilter.FromQuery(new QueryCollection(pairs
            .GroupBy(p => p.Key)
            .ToDictionary(g => g.Key, g => new StringValues(g.Select(p => p.Value).ToArray()))));

    [Fact]
    public void Listing_hides_inactive_products()
    {
        Assert.DoesNotContain("Пена скрытая", Names(q => q));
    }

    [Theory]
    [InlineData("гель", new[] { "Гель Альфа" })]
    [InlineData("ГЕЛЬ", new[] { "Гель Альфа" })]
    [InlineData("пЕнА", new[] { "Пена Гамма" })]
    [InlineData("g-77", new[] { "Пена Гамма" })]
    [InlineData("мягкий", new[] { "Гель Альфа" })]
    public void Search_ignores_case_including_cyrillic(string text, string[] expected)
    {
        Assert.Equal(expected, Names(q => q.Search(text).ApplySort("name")));
    }

    [Fact]
    public void Price_range_must_match_a_single_visible_pack()
    {
        // У «Гель Альфа» фасовки 500 и 5000: ни одна не попадает в 1000-2000, хотя по отдельности
        // «есть дороже 1000» и «есть дешевле 2000». Скрытая фасовка «Шампунь Бета» за 1200 не считается.
        Assert.Equal(["Шампунь Бета"], Names(q => q.ApplyFilter(Filter(("price_min", "1000"), ("price_max", "2000")))));
        Assert.Equal(["Гель Альфа"], Names(q => q.ApplyFilter(Filter(("price_max", "999,5")))));
    }

    [Fact]
    public void In_stock_counts_only_visible_packs()
    {
        Assert.Equal(["Гель Альфа", "Пена Гамма"],
            Names(q => q.ApplyFilter(Filter(("in_stock", "1"))).ApplySort("name")));
    }

    [Fact]
    public void Brand_and_category_filters_use_slugs()
    {
        Assert.Equal(["Пена Гамма"], Names(q => q.ApplyFilter(Filter(("brand", "ifoam")))));
        Assert.Equal(["Гель Альфа", "Шампунь Бета"],
            Names(q => q.ApplyFilter(Filter(("category", "автошампуни"))).ApplySort("name")));
    }

    [Fact]
    public void Price_sort_uses_cheapest_visible_pack_and_puts_unpriced_last()
    {
        Assert.Equal(["Гель Альфа", "Шампунь Бета", "Пена Гамма"], Names(q => q.ApplySort("price_asc")));
        Assert.Equal(["Шампунь Бета", "Гель Альфа", "Пена Гамма"], Names(q => q.ApplySort("price_desc")));
    }

    [Fact]
    public void Filter_parsing_keeps_raw_input_and_rejects_unknown_sort()
    {
        var filter = Filter(("price_min", "abc"), ("sort", "drop table"), ("brand", ""));
        Assert.Null(filter.PriceMin);
        Assert.Equal("abc", filter.PriceMinRaw);
        Assert.Equal(CatalogFilter.DefaultSort, filter.Sort);
        Assert.Empty(filter.Brands);
        Assert.False(filter.HasActiveFilters);
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("abc", 1)]
    [InlineData("-3", 1)]
    [InlineData("2", 2)]
    [InlineData("99", 2)]
    public async Task Page_number_is_clamped_to_existing_pages(string? raw, int expected)
    {
        using var db = _testDb.Create();
        var page = await PagedList<Product>.CreateAsync(db.Products.ForListing().ApplySort("name"), raw, pageSize: 2);
        Assert.Equal(expected, page.Page);
        Assert.Equal(2, page.PageCount);
        Assert.Equal(3, page.TotalCount);
    }

    [Theory]
    [InlineData(1, "товар")]
    [InlineData(3, "товара")]
    [InlineData(5, "товаров")]
    [InlineData(11, "товаров")]
    [InlineData(21, "товар")]
    [InlineData(112, "товаров")]
    public void Plural_forms(int n, string expected) => Assert.Equal(expected, Plural.Products(n));

    [Fact]
    public void Money_is_rounded_and_grouped()
    {
        Assert.Equal("15 991 ₸", Format.Money(15990.5m));
        Assert.Equal("990 ₸", Format.Money(990m));
    }
}
