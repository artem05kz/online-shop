using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.ViewModels;

public record CategoryTile(Category Category, int ProductCount, string? ImagePath);

public class HomeViewModel
{
    public required IReadOnlyList<Banner> Banners { get; init; }
    public required IReadOnlyList<CategoryTile> Tiles { get; init; }
    public required IReadOnlyList<Product> Featured { get; init; }
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

    /// <summary>Запрос, исправленный из-за опечатки или раскладки, если по исходному ничего не нашлось.</summary>
    public string? CorrectedQuery { get; init; }
    public IReadOnlyList<Brand> AllBrands { get; init; } = [];
    public IReadOnlyList<Category> AllCategories { get; init; } = [];

    public bool ShowTaxonomyFilters => Category is null;

    /// <summary>Все категории бренда со счётчиками для списка слева: перейти в соседнюю, не возвращаясь на главную.</summary>
    public IReadOnlyList<CategoryTile> SideCategories { get; init; } = [];
}

public class ProductPageViewModel
{
    public required Product Product { get; init; }
    public required IReadOnlyList<Product> Related { get; init; }

    /// <summary>Опубликованные отзывы, новые сверху.</summary>
    public IReadOnlyList<Review> Reviews { get; init; } = [];

    /// <summary>Получал ли товар вошедший покупатель: тогда ему показывается форма отзыва.</summary>
    public bool CanReview { get; init; }

    /// <summary>Отзыв этого покупателя, если он уже писал (в том числе ещё не проверенный).</summary>
    public Review? MyReview { get; init; }

    public double? AverageRating => Reviews.Count == 0 ? null : Reviews.Average(r => r.Rating);
}

public class StoreListViewModel
{
    public required IReadOnlyList<Store> Stores { get; init; }
    public required IReadOnlyList<string> Cities { get; init; }
    public required string SelectedCity { get; init; }
    public string MapKey { get; init; } = "";
}
