using System.Globalization;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

public record SortOption(string Value, string Label);

/// <summary>
/// Условия фильтра из адреса страницы. Фильтр работает через GET, поэтому выбранное остаётся
/// в адресе: его можно сохранить в закладки или переслать.
/// </summary>
public class CatalogFilter
{
    public const string DefaultSort = "name";

    public static readonly IReadOnlyList<SortOption> SortOptions =
    [
        new("name", "По названию"),
        new("price_asc", "Сначала дешевле"),
        new("price_desc", "Сначала дороже"),
        new("new", "Сначала новинки"),
    ];

    public IReadOnlyList<string> Brands { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public bool InStockOnly { get; init; }

    // Как ввёл покупатель: чтобы показать в поле обратно ровно это, даже если число не разобралось.
    public string PriceMinRaw { get; init; } = "";
    public string PriceMaxRaw { get; init; } = "";

    public string Sort { get; init; } = DefaultSort;

    public decimal? PriceMin => ParsePrice(PriceMinRaw);
    public decimal? PriceMax => ParsePrice(PriceMaxRaw);

    public bool HasActiveFilters =>
        Brands.Count > 0 || Categories.Count > 0 || InStockOnly || PriceMin is not null || PriceMax is not null;

    public static CatalogFilter FromQuery(IQueryCollection query)
    {
        var sort = query["sort"].ToString();
        return new CatalogFilter
        {
            Brands = NonEmpty(query["brand"]),
            Categories = NonEmpty(query["category"]),
            InStockOnly = query["in_stock"] == "1",
            PriceMinRaw = query["price_min"].ToString().Trim(),
            PriceMaxRaw = query["price_max"].ToString().Trim(),
            Sort = SortOptions.Any(o => o.Value == sort) ? sort : DefaultSort,
        };
    }

    private static List<string> NonEmpty(Microsoft.Extensions.Primitives.StringValues values) =>
        values.Where(v => !string.IsNullOrWhiteSpace(v)).Select(v => v!).ToList();

    // Покупатель может написать цену с запятой, как принято в русской раскладке.
    private static decimal? ParsePrice(string raw) =>
        decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
}

/// <summary>Общие для категории и поиска фильтры, поиск и сортировка товаров.</summary>
public static class CatalogQuery
{
    public const int PageSize = 24;

    /// <summary>Активные товары со всем, что нужно карточке в списке.</summary>
    public static IQueryable<Product> ForListing(this IQueryable<Product> products) =>
        products
            .Where(p => p.IsActive)
            .Include(p => p.Category).ThenInclude(c => c.Brand)
            .Include(p => p.Variants)
            .Include(p => p.Images)
            .AsSplitQuery();

    /// <summary>Поиск по названию, описанию и артикулу без учёта регистра, в том числе для кириллицы.</summary>
    public static IQueryable<Product> Search(this IQueryable<Product> products, string query)
    {
        var needle = query.Trim().ToLowerInvariant();
        return products.Where(p =>
            SqliteFunctions.Lower(p.Name).Contains(needle)
            || SqliteFunctions.Lower(p.Description).Contains(needle)
            || p.Variants.Any(v => SqliteFunctions.Lower(v.Article).Contains(needle)));
    }

    public static IQueryable<Product> ApplyFilter(this IQueryable<Product> products, CatalogFilter filter)
    {
        if (filter.Brands.Count > 0)
        {
            var brands = filter.Brands.ToList();
            products = products.Where(p => brands.Contains(p.Category.Brand.Slug));
        }
        if (filter.Categories.Count > 0)
        {
            var categories = filter.Categories.ToList();
            products = products.Where(p => categories.Contains(p.Category.Slug));
        }
        if (filter.InStockOnly)
        {
            products = products.Where(p => p.Variants.Any(v => v.IsActive && v.StockQty > 0));
        }

        // Обе границы цены проверяются на одной и той же видимой фасовке. В Django-версии они
        // проверялись по отдельности, и товар с фасовками за 500 и 5000 ₸ проходил фильтр «от 1000 до 2000».
        var min = filter.PriceMin;
        var max = filter.PriceMax;
        if (min is not null || max is not null)
        {
            products = products.Where(p => p.Variants.Any(v =>
                v.IsActive && v.Price != null
                && (min == null || v.Price >= min)
                && (max == null || v.Price <= max)));
        }
        return products;
    }

    public static IQueryable<Product> ApplySort(this IQueryable<Product> products, string sort)
    {
        // Товары без цены при сортировке по цене всегда в конце: иначе «сначала дешевле»
        // начиналось бы с позиций «цена уточняется».
        return sort switch
        {
            "price_asc" => products
                .OrderBy(p => p.Variants.Where(v => v.IsActive && v.Price != null).Min(v => v.Price) == null)
                .ThenBy(p => p.Variants.Where(v => v.IsActive && v.Price != null).Min(v => v.Price))
                .ThenBy(p => p.Name),
            "price_desc" => products
                .OrderBy(p => p.Variants.Where(v => v.IsActive && v.Price != null).Min(v => v.Price) == null)
                .ThenByDescending(p => p.Variants.Where(v => v.IsActive && v.Price != null).Min(v => v.Price))
                .ThenBy(p => p.Name),
            "new" => products.OrderByDescending(p => p.CreatedAt).ThenBy(p => p.Name),
            _ => products.OrderBy(p => p.Name).ThenBy(p => p.Id),
        };
    }
}

/// <summary>Одна страница списка. Номер страницы вне диапазона приводится к ближайшему существующему.</summary>
public class PagedList<T>
{
    public required IReadOnlyList<T> Items { get; init; }
    public required int Page { get; init; }
    public required int PageCount { get; init; }
    public required int TotalCount { get; init; }

    public bool HasPrevious => Page > 1;
    public bool HasNext => Page < PageCount;

    public static async Task<PagedList<T>> CreateAsync(IQueryable<T> query, string? rawPage, int pageSize)
    {
        var total = await query.CountAsync();
        var pageCount = Math.Max(1, (total + pageSize - 1) / pageSize);
        var page = int.TryParse(rawPage, out var parsed) ? Math.Clamp(parsed, 1, pageCount) : 1;
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
        return new PagedList<T> { Items = items, Page = page, PageCount = pageCount, TotalCount = total };
    }
}
