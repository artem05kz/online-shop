using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.ViewModels;

public record CategoryTile(Category Category, int ProductCount);

public record CategorySection(Brand Brand, Category Category, IReadOnlyList<Product> Products);

public class HomeViewModel
{
    public required IReadOnlyList<Banner> Banners { get; init; }
    public required IReadOnlyList<CategoryTile> Tiles { get; init; }
    public required IReadOnlyList<Product> Featured { get; init; }
    public required IReadOnlyList<CategorySection> Sections { get; init; }
}

/// <summary>Список товаров с фильтрами: страница категории или результаты поиска.</summary>
public class ProductListViewModel
{
    public required CatalogFilter Filter { get; init; }
    public required PagedList<Product> Page { get; init; }

    // Для категории бренд и категория уже заданы, и в фильтре их не показываем.
    public Category? Category { get; init; }

    // Для поиска: строка запроса и списки брендов и категорий для фильтра.
    public string? Query { get; init; }
    public IReadOnlyList<Brand> AllBrands { get; init; } = [];
    public IReadOnlyList<Category> AllCategories { get; init; } = [];

    public bool ShowTaxonomyFilters => Category is null;
}

public class ProductPageViewModel
{
    public required Product Product { get; init; }
    public required IReadOnlyList<Product> Related { get; init; }
}

public class StoreListViewModel
{
    public required IReadOnlyList<Store> Stores { get; init; }
    public required IReadOnlyList<string> Cities { get; init; }
    public required string SelectedCity { get; init; }
}
