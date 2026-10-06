using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

public class CategoryNode(Category category, int depth)
{
    public Category Category { get; } = category;
    public int Depth { get; } = depth;
    public List<CategoryNode> Children { get; } = [];
    public int ProductCount { get; set; }

    /// <summary>Товаров в этой категории вместе со всеми вложенными.</summary>
    public int TotalProducts => ProductCount + Children.Sum(c => c.TotalProducts);
}

public class BrandNode(Brand brand)
{
    public Brand Brand { get; } = brand;
    public List<CategoryNode> Roots { get; } = [];
    public int TotalProducts => Roots.Sum(r => r.TotalProducts);
}

/// <summary>
/// Каталог деревом: бренд, его категории, их подкатегории. Нужен панели, чтобы было видно,
/// что во что вложено, и выпадающим спискам, где категория показана полным путём.
/// </summary>
public class CatalogTree
{
    public const string Arrow = " → ";

    public required IReadOnlyList<BrandNode> Brands { get; init; }
    private Dictionary<int, CategoryNode> ById { get; init; } = [];

    public static async Task<CatalogTree> LoadAsync(ShopDbContext db)
    {
        var brands = await db.Brands.Ordered().AsNoTracking().ToListAsync();
        var categories = await db.Categories.Ordered().AsNoTracking().ToListAsync();
        var counts = await db.Products.GroupBy(p => p.CategoryId)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);

        var brandNodes = brands.Select(b => new BrandNode(b)).ToList();
        var brandById = brandNodes.ToDictionary(b => b.Brand.Id);
        var byId = new Dictionary<int, CategoryNode>();

        void Add(Category category, int depth, List<CategoryNode> into)
        {
            var node = new CategoryNode(category, depth) { ProductCount = counts.GetValueOrDefault(category.Id) };
            byId[category.Id] = node;
            into.Add(node);
            foreach (var child in categories.Where(c => c.ParentId == category.Id))
            {
                Add(child, depth + 1, node.Children);
            }
        }
        foreach (var root in categories.Where(c => c.ParentId == null))
        {
            if (brandById.TryGetValue(root.BrandId, out var brand))
            {
                root.Brand = brand.Brand;
                Add(root, 0, brand.Roots);
            }
        }
        foreach (var node in byId.Values)
        {
            node.Category.Brand = brandById[node.Category.BrandId].Brand;
        }
        return new CatalogTree { Brands = brandNodes, ById = byId };
    }

    public CategoryNode? Find(int categoryId) => ById.GetValueOrDefault(categoryId);

    /// <summary>Цепочка от верхней категории до этой включительно.</summary>
    public IReadOnlyList<Category> Ancestry(int categoryId)
    {
        var chain = new List<Category>();
        var current = Find(categoryId)?.Category;
        while (current is not null)
        {
            chain.Insert(0, current);
            current = current.ParentId is { } parent ? Find(parent)?.Category : null;
        }
        return chain;
    }

    /// <summary>«Ifoam → Очистители → Для стёкол»: так категорию видно в списках, а не одно её название.</summary>
    public string Path(int categoryId)
    {
        var chain = Ancestry(categoryId);
        return chain.Count == 0 ? "" : chain[0].Brand.Name + Arrow + string.Join(Arrow, chain.Select(c => c.Name));
    }

    /// <summary>Все категории по порядку дерева, для выпадающих списков с отступами.</summary>
    public IEnumerable<CategoryNode> Flatten(int? brandId = null)
    {
        IEnumerable<CategoryNode> Walk(CategoryNode node) => new[] { node }.Concat(node.Children.SelectMany(Walk));
        return Brands.Where(b => brandId is null || b.Brand.Id == brandId).SelectMany(b => b.Roots.SelectMany(Walk));
    }
}
