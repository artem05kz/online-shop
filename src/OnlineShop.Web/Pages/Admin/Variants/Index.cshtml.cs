using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Variants;

/// <summary>
/// Плоский список всех артикулов для быстрой правки: нашёл по артикулу, поправил цену или остаток,
/// сохранил. Без хождения по карточкам товаров.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public class Row
    {
        public int Id { get; set; }
        public string Article { get; set; } = "";
        public int ProductId { get; set; }
        public string ProductName { get; set; } = "";
        public string? ImagePath { get; set; }
        public string PackLabel { get; set; } = "";
        public string? Price { get; set; }
        public string? StockQty { get; set; }
        public bool IsActive { get; set; }
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public int? Brand { get; set; }
    [BindProperty(SupportsGet = true)] public string? Active { get; set; }
    [BindProperty(SupportsGet = true)] public string? O { get; set; }

    [BindProperty] public List<Row> Rows { get; set; } = [];

    public IReadOnlyList<Brand> Brands { get; private set; } = [];
    public PagerInfo Pager { get; private set; } = new(1, 1, 0);

    public async Task OnGetAsync(string? p)
    {
        Brands = await db.Brands.Ordered().AsNoTracking().ToListAsync();

        var query = db.ProductVariants.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var needle = Q.Trim().ToLowerInvariant();
            query = query.Where(v => SqliteFunctions.Lower(v.Article).Contains(needle)
                || SqliteFunctions.Lower(v.Product.Name).Contains(needle));
        }
        if (Brand is not null)
        {
            query = query.Where(v => v.Product.Category.BrandId == Brand);
        }
        if (Active is "1" or "0")
        {
            var active = Active == "1";
            query = query.Where(v => v.IsActive == active);
        }
        query = O switch
        {
            "article" => query.OrderBy(v => v.Article),
            "-article" => query.OrderByDescending(v => v.Article),
            "-product" => query.OrderByDescending(v => v.Product.Name).ThenBy(v => v.VolumeValue),
            "price" => query.OrderBy(v => v.Price),
            "-price" => query.OrderByDescending(v => v.Price),
            "stock" => query.OrderBy(v => v.StockQty),
            "-stock" => query.OrderByDescending(v => v.StockQty),
            _ => query.OrderBy(v => v.Product.Name).ThenBy(v => v.VolumeValue),
        };

        var list = await PagedList<ProductVariant>.CreateAsync(query.Include(v => v.Product).ThenInclude(p => p.Images).AsNoTracking(), p, AdminList.PageSize);
        Pager = PagerInfo.From(list);
        Rows = list.Items.Select(v => new Row
        {
            Id = v.Id, Article = v.Article, ProductId = v.ProductId, ProductName = v.Product.Name, PackLabel = v.PackLabel,
            ImagePath = v.Product.PrimaryImage?.ImagePath,
            Price = AdminNumbers.Text(v.Price), StockQty = v.StockQty.ToString(), IsActive = v.IsActive,
        }).ToList();
    }

    public async Task<IActionResult> OnPostSaveListAsync()
    {
        var errors = new List<string>();
        var parsed = new Dictionary<int, (decimal? Price, int Stock, bool Active)>();
        foreach (var row in Rows)
        {
            if (!AdminNumbers.TryDecimal(row.Price, out var price))
            {
                errors.Add($"{row.Article}: цена «{row.Price}» не число");
                continue;
            }
            if (!AdminNumbers.TryInt(row.StockQty, out var stock))
            {
                errors.Add($"{row.Article}: остаток «{row.StockQty}» не целое число");
                continue;
            }
            parsed[row.Id] = (price, stock ?? 0, row.IsActive);
        }
        if (errors.Count > 0)
        {
            // Ничего не сохраняем, пока не исправлены все строки: иначе часть правок молча потерялась бы.
            TempData.Flash("Не сохранено, исправьте строки: " + string.Join("; ", errors) + ".", FlashLevel.Error);
            return RedirectToPage(new { q = Q, brand = Brand, active = Active, o = O });
        }

        var variants = await db.ProductVariants.Where(v => parsed.Keys.Contains(v.Id)).ToListAsync();
        foreach (var variant in variants)
        {
            (variant.Price, variant.StockQty, variant.IsActive) = parsed[variant.Id];
        }
        await db.SaveChangesAsync();
        TempData.Flash($"Сохранено строк: {variants.Count}.");
        return RedirectToPage(new { q = Q, brand = Brand, active = Active, o = O, p = Request.Query["p"].ToString() });
    }
}
