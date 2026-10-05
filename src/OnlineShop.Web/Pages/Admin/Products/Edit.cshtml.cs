using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Products;

/// <summary>
/// Карточка товара целиком: основные поля, фасовки и фото на одной странице, как в Django-админке.
/// Поля, которые менеджер чаще всего правит после импорта (название, описание), стоят первыми.
/// </summary>
public class EditModel(ShopDbContext db, MediaStorage media) : PageModel
{
    private const long MaxImageBytes = 10 * 1024 * 1024;

    public class InputModel
    {
        [Display(Name = "Название")]
        [Required(ErrorMessage = "Обязательное поле."), StringLength(300, ErrorMessage = "Не длиннее {1} символов.")]
        public string Name { get; set; } = "";

        [Display(Name = "Ярлык для ссылки")]
        [StringLength(Product.SlugMaxLength, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Slug { get; set; }

        [Display(Name = "Категория")]
        [Required(ErrorMessage = "Выберите категорию.")]
        public int? CategoryId { get; set; }

        [Display(Name = "Показывать на сайте")]
        public bool IsActive { get; set; } = true;

        [Display(Name = "Описание")]
        public string? Description { get; set; }

        [Display(Name = "Кислотность (pH)")]
        [StringLength(50, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Ph { get; set; }
    }

    /// <summary>Строка фасовки. Числа строками: менеджер может написать цену и с запятой, и с точкой.</summary>
    public class VariantRow
    {
        public int Id { get; set; }
        public string? Article { get; set; }
        public string? PackLabel { get; set; }
        public string? WholesalePrice { get; set; }
        public string? Price { get; set; }
        public string? OldPrice { get; set; }
        public string? StockQty { get; set; }
        public bool IsActive { get; set; } = true;
        public bool Delete { get; set; }

        public bool IsBlank => Id == 0 && string.IsNullOrWhiteSpace(Article) && string.IsNullOrWhiteSpace(PackLabel)
            && string.IsNullOrWhiteSpace(Price) && string.IsNullOrWhiteSpace(WholesalePrice)
            && string.IsNullOrWhiteSpace(OldPrice) && string.IsNullOrWhiteSpace(StockQty);
    }

    public class ImageRow
    {
        public int Id { get; set; }
        public string ImagePath { get; set; } = "";
        public bool IsPrimary { get; set; }
        public int SortOrder { get; set; }
        public bool Delete { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public List<VariantRow> Variants { get; set; } = [];
    [BindProperty] public List<ImageRow> Images { get; set; } = [];
    [BindProperty] public List<IFormFile> NewImages { get; set; } = [];

    public int? Id { get; private set; }
    public Product? Existing { get; private set; }
    public IReadOnlyList<Category> Categories { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is not null)
        {
            Existing = await Load(id.Value);
            if (Existing is null)
            {
                return NotFound();
            }
            var p = Existing;
            Input = new InputModel
            {
                Name = p.Name, Slug = p.Slug, CategoryId = p.CategoryId, IsActive = p.IsActive,
                Description = p.Description, Ph = p.Ph,
            };
            Variants = p.Variants.OrderBy(v => v.VolumeValue).ThenBy(v => v.Id).Select(v => new VariantRow
            {
                Id = v.Id, Article = v.Article, PackLabel = v.PackLabel,
                WholesalePrice = AdminNumbers.Text(v.WholesalePrice), Price = AdminNumbers.Text(v.Price),
                OldPrice = AdminNumbers.Text(v.OldPrice), StockQty = v.StockQty.ToString(), IsActive = v.IsActive,
            }).ToList();
            Images = p.Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).Select(i => new ImageRow
            {
                Id = i.Id, ImagePath = i.ImagePath, IsPrimary = i.IsPrimary, SortOrder = i.SortOrder,
            }).ToList();
        }
        Variants.Add(new VariantRow());
        await LoadCategories();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        var product = id is null ? new Product() : await Load(id.Value);
        if (product is null)
        {
            return NotFound();
        }
        Existing = id is null ? null : product;

        var slug = Slug.From(Input.Slug ?? "", Product.SlugMaxLength);
        if (slug.Length > 0 && await db.Products.AnyAsync(p => p.Slug == slug && p.Id != product.Id))
        {
            ModelState.AddModelError("Input.Slug", "Такой ярлык уже занят другим товаром.");
        }
        var parsedVariants = await ValidateVariants(product);
        var uploads = await ValidateUploads();

        if (!ModelState.IsValid)
        {
            // Пустую строку для новой фасовки показываем снова, чтобы форма выглядела так же.
            if (Variants.Count == 0 || !Variants[^1].IsBlank)
            {
                Variants.Add(new VariantRow());
            }
            await LoadCategories();
            return Page();
        }

        product.Name = Input.Name.Trim();
        product.Slug = slug;
        product.CategoryId = Input.CategoryId!.Value;
        product.IsActive = Input.IsActive;
        product.Description = Input.Description?.Trim() ?? "";
        product.Ph = Input.Ph ?? "";
        if (product.Id == 0)
        {
            db.Products.Add(product);
        }

        foreach (var (row, values) in parsedVariants)
        {
            var variant = row.Id == 0 ? new ProductVariant() : product.Variants.First(v => v.Id == row.Id);
            if (row.Delete)
            {
                db.ProductVariants.Remove(variant);
                continue;
            }
            variant.Article = row.Article!.Trim();
            variant.PackLabel = row.PackLabel?.Trim() ?? "";
            variant.WholesalePrice = values.Wholesale;
            variant.Price = values.Price;
            variant.OldPrice = values.Old;
            variant.StockQty = values.Stock ?? 0;
            variant.IsActive = row.IsActive;
            if (row.Id == 0)
            {
                product.Variants.Add(variant);
            }
        }

        var filesToDelete = new List<string>();
        foreach (var row in Images)
        {
            var image = product.Images.FirstOrDefault(i => i.Id == row.Id);
            if (image is null)
            {
                continue;
            }
            if (row.Delete)
            {
                filesToDelete.Add(image.ImagePath);
                db.ProductImages.Remove(image);
                continue;
            }
            image.IsPrimary = row.IsPrimary;
            image.SortOrder = Math.Max(0, row.SortOrder);
        }
        var nextOrder = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.SortOrder) + 1;
        var savedFiles = new List<string>();
        try
        {
            foreach (var (content, fileName) in uploads)
            {
                var path = await media.SaveAsync(content, "products", fileName);
                savedFiles.Add(path);
                product.Images.Add(new ProductImage { ImagePath = path, SortOrder = nextOrder++ });
            }
            await db.SaveChangesAsync();
        }
        catch
        {
            // В базу ничего не попало, значит и новые файлы на диске не нужны.
            savedFiles.ForEach(media.Delete);
            throw;
        }
        // Файлы удаляем после записи в базу: если бы сохранение упало, фото остались бы на месте.
        filesToDelete.ForEach(media.Delete);

        TempData.Flash($"Товар «{product.Name}» сохранён.");
        return RedirectToPage(new { id = product.Id });
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var product = await Load(id);
        if (product is null)
        {
            return NotFound();
        }
        var variantIds = product.Variants.Select(v => v.Id).ToList();
        if (await db.OrderItems.AnyAsync(i => variantIds.Contains(i.VariantId)))
        {
            TempData.Flash($"Нельзя удалить товар «{product.Name}»: его заказывали, и он есть в истории заказов. "
                + "Снимите галочку «Показывать на сайте», чтобы скрыть его.", FlashLevel.Error);
            return RedirectToPage(new { id });
        }
        var files = product.Images.Select(i => i.ImagePath).ToList();
        db.Products.Remove(product);
        await db.SaveChangesAsync();
        files.ForEach(media.Delete);
        TempData.Flash($"Товар «{product.Name}» удалён.");
        return RedirectToPage("Index");
    }

    private Task<Product?> Load(int id) =>
        db.Products.Include(p => p.Variants).Include(p => p.Images).Include(p => p.Category)
            .AsSplitQuery().FirstOrDefaultAsync(p => p.Id == id);

    private async Task LoadCategories() =>
        Categories = await db.Categories.Include(c => c.Brand)
            .OrderBy(c => c.Brand.SortOrder).ThenBy(c => c.Brand.Name).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .AsNoTracking().ToListAsync();

    private record VariantValues(decimal? Wholesale, decimal? Price, decimal? Old, int? Stock);

    private async Task<List<(VariantRow Row, VariantValues Values)>> ValidateVariants(Product product)
    {
        var result = new List<(VariantRow, VariantValues)>();
        var seen = new Dictionary<string, int>();
        for (var i = 0; i < Variants.Count; i++)
        {
            var row = Variants[i];
            if (row.IsBlank)
            {
                continue;
            }
            if (row.Id != 0 && product.Variants.All(v => v.Id != row.Id))
            {
                ModelState.AddModelError("", "Форма устарела: обновите страницу и повторите правку.");
                continue;
            }
            var key = $"Variants[{i}]";
            if (row.Delete)
            {
                if (row.Id != 0 && await db.OrderItems.AnyAsync(o => o.VariantId == row.Id))
                {
                    ModelState.AddModelError($"{key}.Article",
                        "Эту фасовку заказывали, удалить её нельзя. Снимите галочку «На сайте», чтобы скрыть.");
                }
                result.Add((row, new VariantValues(null, null, null, null)));
                continue;
            }

            var article = row.Article?.Trim() ?? "";
            if (article.Length == 0)
            {
                ModelState.AddModelError($"{key}.Article", "Укажите артикул.");
            }
            else if (article.Length > 50)
            {
                ModelState.AddModelError($"{key}.Article", "Не длиннее 50 символов.");
            }
            else if (!seen.TryAdd(article, i))
            {
                ModelState.AddModelError($"{key}.Article", "Этот артикул уже есть выше в списке.");
            }
            else
            {
                var other = await db.ProductVariants.Include(v => v.Product)
                    .FirstOrDefaultAsync(v => v.Article == article && v.Id != row.Id);
                if (other is not null)
                {
                    ModelState.AddModelError($"{key}.Article", $"Артикул уже есть у товара «{other.Product.Name}».");
                }
            }
            if ((row.PackLabel?.Trim().Length ?? 0) > 50)
            {
                ModelState.AddModelError($"{key}.PackLabel", "Не длиннее 50 символов.");
            }

            decimal? Money(string? raw, string field)
            {
                if (!AdminNumbers.TryDecimal(raw, out var value))
                {
                    ModelState.AddModelError($"{key}.{field}", "Введите число, например 1890 или 1890,50.");
                }
                return value;
            }
            var values = new VariantValues(Money(row.WholesalePrice, "WholesalePrice"), Money(row.Price, "Price"),
                Money(row.OldPrice, "OldPrice"), null);
            if (!AdminNumbers.TryInt(row.StockQty, out var stock))
            {
                ModelState.AddModelError($"{key}.StockQty", "Введите целое число.");
            }
            result.Add((row, values with { Stock = stock }));
        }
        return result;
    }

    private async Task<List<(byte[] Content, string FileName)>> ValidateUploads()
    {
        var result = new List<(byte[], string)>();
        foreach (var file in NewImages.Where(f => f.Length > 0))
        {
            if (file.Length > MaxImageBytes)
            {
                ModelState.AddModelError("NewImages", $"Файл «{file.FileName}» больше 10 МБ.");
                continue;
            }
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory);
            var bytes = memory.ToArray();
            if (!MediaStorage.LooksLikeImage(file.FileName, bytes))
            {
                ModelState.AddModelError("NewImages", $"«{file.FileName}» не похож на фотографию. Подходят JPG, PNG, GIF и WEBP.");
                continue;
            }
            result.Add((bytes, file.FileName));
        }
        return result;
    }
}
