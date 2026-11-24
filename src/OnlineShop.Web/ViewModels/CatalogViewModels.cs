using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.ViewModels;

/// <param name="Render">3D-рендер товара раздела из wwwroot/img/tiles/ (если есть) — показывается вместо фото.</param>
public record CategoryTile(Category Category, int ProductCount, string? ImagePath, string? Render = null);

/// <summary>Слайд первого экрана вместе с товаром из базы (цена, ссылка, наличие).</summary>
public record HeroSlideView(OnlineShop.Web.Infrastructure.HeroSlide Slide, Product Product);

public class HomeViewModel
{
    public IReadOnlyList<HeroSlideView> Hero { get; init; } = [];
    public required IReadOnlyList<Banner> Banners { get; init; }
    public required IReadOnlyList<CategoryTile> Tiles { get; init; }
    public required IReadOnlyList<Product> Featured { get; init; }

    /// <summary>«Хиты продаж» на главной (список заказчицы, <see cref="Infrastructure.HitProducts"/>).</summary>
    public IReadOnlyList<Product> Hits { get; init; } = [];
    public IReadOnlyList<Store> Stores { get; init; } = [];
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

    /// <summary>Раздел: все категории бренда со счётчиками (кнопки в шапке). Поиск: категории, в которых нашлись товары.</summary>
    public IReadOnlyList<CategoryTile> SideCategories { get; init; } = [];

    /// <summary>Кнопки фасовок раздела («Триггер 500 мл», «1 кг», «5–6 кг»…): только те, что есть в разделе.</summary>
    public IReadOnlyList<PackChip> PackChips { get; init; } = [];
}

public record PackChip(string Key, string Label, int Count);

public class ProductPageViewModel
{
    public required Product Product { get; init; }
    public required IReadOnlyList<Product> Related { get; init; }

    /// <summary>«С этим покупают»: из тех же заказов, остаток — хиты других разделов.</summary>
    public IReadOnlyList<Product> BoughtWith { get; init; } = [];

    /// <summary>«Вы недавно смотрели», без этого товара.</summary>
    public IReadOnlyList<Product> Recent { get; init; } = [];

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
