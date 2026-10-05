using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Variants;

public class EditModel(ShopDbContext db) : PageModel
{
    public class InputModel
    {
        [Display(Name = "Товар")]
        [Required(ErrorMessage = "Выберите товар.")]
        public int? ProductId { get; set; }

        [Display(Name = "Артикул")]
        [Required(ErrorMessage = "Обязательное поле."), StringLength(50, ErrorMessage = "Не длиннее {1} символов.")]
        public string Article { get; set; } = "";

        [Display(Name = "Фасовка")]
        [StringLength(50, ErrorMessage = "Не длиннее {1} символов.")]
        public string? PackLabel { get; set; }

        [Display(Name = "Объём, число")] public string? VolumeValue { get; set; }

        [Display(Name = "Единица измерения")]
        [StringLength(20, ErrorMessage = "Не длиннее {1} символов.")]
        public string? VolumeUnit { get; set; }

        [Display(Name = "Количество в коробке")] public string? BoxQty { get; set; }
        [Display(Name = "Количество на паллете")] public string? PalletQty { get; set; }
        [Display(Name = "Оптовая цена")] public string? WholesalePrice { get; set; }
        [Display(Name = "Розничная цена")] public string? Price { get; set; }
        [Display(Name = "Цена до скидки")] public string? OldPrice { get; set; }
        [Display(Name = "Остаток на складе")] public string? StockQty { get; set; }
        [Display(Name = "Показывать на сайте")] public bool IsActive { get; set; } = true;
    }

    [BindProperty] public InputModel Input { get; set; } = new();

    public int? Id { get; private set; }
    public ProductVariant? Existing { get; private set; }
    public IReadOnlyList<Product> Products { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id, int? product)
    {
        Id = id;
        if (id is not null)
        {
            Existing = await db.ProductVariants.FindAsync(id);
            if (Existing is null)
            {
                return NotFound();
            }
            var v = Existing;
            Input = new InputModel
            {
                ProductId = v.ProductId, Article = v.Article, PackLabel = v.PackLabel,
                VolumeValue = AdminNumbers.Text(v.VolumeValue), VolumeUnit = v.VolumeUnit,
                BoxQty = v.BoxQty?.ToString(), PalletQty = v.PalletQty?.ToString(),
                WholesalePrice = AdminNumbers.Text(v.WholesalePrice), Price = AdminNumbers.Text(v.Price),
                OldPrice = AdminNumbers.Text(v.OldPrice), StockQty = v.StockQty.ToString(), IsActive = v.IsActive,
            };
        }
        else
        {
            Input.ProductId = product;
        }
        await LoadProducts();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        var variant = id is null ? new ProductVariant() : await db.ProductVariants.FindAsync(id);
        if (variant is null)
        {
            return NotFound();
        }
        Existing = id is null ? null : variant;

        var article = Input.Article.Trim();
        if (article.Length > 0 && await db.ProductVariants.AnyAsync(v => v.Article == article && v.Id != variant.Id))
        {
            ModelState.AddModelError("Input.Article", "Такой артикул уже есть.");
        }
        decimal? Dec(string? raw, string field)
        {
            if (!AdminNumbers.TryDecimal(raw, out var value))
            {
                ModelState.AddModelError($"Input.{field}", "Введите число, например 1890 или 1890,50.");
            }
            return value;
        }
        int? Int(string? raw, string field)
        {
            if (!AdminNumbers.TryInt(raw, out var value) || value < 0 && field != nameof(InputModel.StockQty))
            {
                ModelState.AddModelError($"Input.{field}", "Введите целое число.");
            }
            return value;
        }
        var volume = Dec(Input.VolumeValue, nameof(InputModel.VolumeValue));
        var box = Int(Input.BoxQty, nameof(InputModel.BoxQty));
        var pallet = Int(Input.PalletQty, nameof(InputModel.PalletQty));
        var wholesale = Dec(Input.WholesalePrice, nameof(InputModel.WholesalePrice));
        var price = Dec(Input.Price, nameof(InputModel.Price));
        var oldPrice = Dec(Input.OldPrice, nameof(InputModel.OldPrice));
        var stock = Int(Input.StockQty, nameof(InputModel.StockQty));
        if (!ModelState.IsValid)
        {
            await LoadProducts();
            return Page();
        }

        variant.ProductId = Input.ProductId!.Value;
        variant.Article = article;
        variant.PackLabel = Input.PackLabel?.Trim() ?? "";
        variant.VolumeValue = volume;
        variant.VolumeUnit = Input.VolumeUnit?.Trim() ?? "";
        variant.BoxQty = box;
        variant.PalletQty = pallet;
        variant.WholesalePrice = wholesale;
        variant.Price = price;
        variant.OldPrice = oldPrice;
        variant.StockQty = stock ?? 0;
        variant.IsActive = Input.IsActive;
        if (variant.Id == 0)
        {
            db.ProductVariants.Add(variant);
        }
        await db.SaveChangesAsync();
        TempData.Flash($"Артикул {variant.Article} сохранён.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var variant = await db.ProductVariants.FindAsync(id);
        if (variant is null)
        {
            return NotFound();
        }
        if (await db.OrderItems.AnyAsync(i => i.VariantId == id))
        {
            TempData.Flash($"Артикул {variant.Article} заказывали, удалить его нельзя. Снимите галочку «Показывать на сайте».",
                FlashLevel.Error);
            return RedirectToPage(new { id });
        }
        db.ProductVariants.Remove(variant);
        await db.SaveChangesAsync();
        TempData.Flash($"Артикул {variant.Article} удалён.");
        return RedirectToPage("Index");
    }

    private async Task LoadProducts() =>
        Products = await db.Products.OrderBy(p => p.Name).AsNoTracking().ToListAsync();
}
