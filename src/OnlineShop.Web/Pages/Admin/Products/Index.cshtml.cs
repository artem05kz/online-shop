using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Products;

/// <summary>
/// Товары списком с фото, чтобы товар узнавался глазами, а не по названию из 1С.
/// Фильтры-кнопки сверху отвечают на частые вопросы: где нет фото, цены, остатка.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public record Row(Product Product, string Path)
    {
        public IReadOnlyList<ProductVariant> Packs => Product.Variants.OrderBy(v => v.SortVolume).ThenBy(v => v.Id).ToList();
        public decimal? MinPrice => Product.Variants.Where(v => v.IsActive && v.Price != null).Select(v => v.Price).Min();
        public decimal? MaxPrice => Product.Variants.Where(v => v.IsActive && v.Price != null).Select(v => v.Price).Max();
        public bool InStock => Product.Variants.Any(v => v.IsActive && v.InStock);
        public bool HasUnpriced => Product.Variants.Any(v => v.IsActive && v.Price == null);
    }

    public record Show(string Value, string Label, int Count);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Category { get; set; }
    [BindProperty(SupportsGet = true)] public string? Filter { get; set; }
    [BindProperty(SupportsGet = true)] public string? O { get; set; }

    public CatalogTree Tree { get; private set; } = null!;
    public IReadOnlyList<Show> Shows { get; private set; } = [];
    public PagedList<Row> List { get; private set; } = null!;

    public static readonly IReadOnlyList<(string Value, string Label)> SortOptions =
    [
        ("name", "По названию"),
        ("new", "Сначала новые"),
        ("updated", "Недавно изменённые"),
    ];

    public async Task OnGetAsync(string? p)
    {
        Tree = await CatalogTree.LoadAsync(db);

        var scope = db.Products.AsQueryable();
        if (Category is { } categoryId && Tree.Find(categoryId) is { } node)
        {
            // Категория вместе со всеми подкатегориями: «Очистители» показывают и товары из вложенных.
            var ids = Tree.Flatten().Where(n => IsWithin(n, node)).Select(n => n.Category.Id).ToList();
            scope = scope.Where(x => ids.Contains(x.CategoryId));
        }
        if (!string.IsNullOrWhiteSpace(Q))
        {
            scope = scope.Search(Q);
        }

        // Сколько товаров попадёт в каждый фильтр: число на кнопке подсказывает, есть ли там что делать.
        Shows =
        [
            new("", "Все", await scope.CountAsync()),
            new("nophoto", "Без фото", await NoPhoto(scope).CountAsync()),
            new("noprice", "Без цены", await NoPrice(scope).CountAsync()),
            new("nostock", "Нет в наличии", await NoStock(scope).CountAsync()),
            new("hidden", "Скрытые", await scope.CountAsync(x => !x.IsActive)),
        ];

        var query = Filter switch
        {
            "nophoto" => NoPhoto(scope),
            "noprice" => NoPrice(scope),
            "nostock" => NoStock(scope),
            "hidden" => scope.Where(x => !x.IsActive),
            _ => scope,
        };
        query = O switch
        {
            "new" => query.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Name),
            "updated" => query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Name),
            _ => query.OrderBy(x => x.Name),
        };

        var page = await PagedList<Product>.CreateAsync(
            query.Include(x => x.Variants).Include(x => x.Images).AsSplitQuery().AsNoTracking(), p, 50);
        List = new PagedList<Row>
        {
            Items = page.Items.Select(x => new Row(x, Tree.Path(x.CategoryId))).ToList(),
            Page = page.Page, PageCount = page.PageCount, TotalCount = page.TotalCount,
        };
    }

    private static bool IsWithin(CategoryNode candidate, CategoryNode root)
    {
        static IEnumerable<CategoryNode> Walk(CategoryNode n) => new[] { n }.Concat(n.Children.SelectMany(Walk));
        return Walk(root).Contains(candidate);
    }

    private static IQueryable<Product> NoPhoto(IQueryable<Product> q) => q.Where(x => !x.Images.Any());

    private static IQueryable<Product> NoPrice(IQueryable<Product> q) =>
        q.Where(x => x.Variants.Any(v => v.IsActive && v.Price == null));

    private static IQueryable<Product> NoStock(IQueryable<Product> q) =>
        q.Where(x => !x.Variants.Any(v => v.IsActive && v.StockQty > v.ReservedQty));
}
