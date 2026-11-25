using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using SkiaSharp;

namespace OnlineShop.Web.Pages.Admin.Slides;

/// <summary>
/// Конструктор слайда с товаром (25.11.2026). Менеджер выбирает товар — заголовок, рукописное слово, текст и цена
/// подставляются сами (SlideText); загружает 3D-рендер с прозрачным фоном — цвет фона берётся с этикетки (SlideColors).
/// Всё можно поправить, превью сверху меняется сразу (wwwroot/js/admin_slide_edit.js).
/// </summary>
[RequestSizeLimit(30 * 1024 * 1024)]
[RequestFormLimits(MultipartBodyLengthLimit = 30 * 1024 * 1024)]
public class EditModel(ShopDbContext db, MediaStorage media) : PageModel
{
    private const long MaxImageBytes = 20 * 1024 * 1024;
    /// <summary>Рендер хранится не шире 2000 точек: на сайте он занимает 8 колонок из 12 (около 800 точек).</summary>
    private const int MaxSide = 2000;
    /// <summary>Дольше этого рукописное слово перестаёт помещаться крупно — предупреждаем.</summary>
    public const int ScriptComfortLength = 14;

    public class InputModel
    {
        [Display(Name = "Товар")]
        public int? ProductId { get; set; }

        [Display(Name = "Рукописное слово")]
        [StringLength(24, ErrorMessage = "Не длиннее {1} символов: иначе слово не поместится.")]
        public string? Script { get; set; }

        [Display(Name = "Заголовок")]
        [StringLength(120, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Title { get; set; }

        [Display(Name = "Короткий текст")]
        [StringLength(200, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Text { get; set; }

        [Display(Name = "Фон")]
        public string? BgColor { get; set; }

        [Display(Name = "Рукописное слово")]
        public string? ScriptColor { get; set; }

        [Display(Name = "Показывать на сайте")]
        public bool IsActive { get; set; } = true;
    }

    /// <summary>Товар в списке выбора: подсказки для надписей и цена для превью.</summary>
    public record ProductOption(int Id, string Name, string Title, string Script, string Text, string Price);

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Image { get; set; }

    /// <summary>Цвета выбраны в панели (вручную или по этикетке в браузере). Иначе при новом рендере берём их с этикетки здесь.</summary>
    [BindProperty] public bool ColorsTouched { get; set; }

    public int? Id { get; private set; }
    public string? CurrentImageUrl { get; private set; }
    public List<ProductOption> Products { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is not null)
        {
            var slide = await db.ProductSlides.FindAsync(id);
            if (slide is null)
            {
                return NotFound();
            }
            CurrentImageUrl = ImageUrl(slide);
            Input = new InputModel
            {
                ProductId = slide.ProductId, Script = slide.Script, Title = slide.Title, Text = slide.Text,
                BgColor = slide.BgColor, ScriptColor = slide.ScriptColor, IsActive = slide.IsActive,
            };
        }
        await LoadProductsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        var slide = id is null ? new ProductSlide() : await db.ProductSlides.FindAsync(id);
        if (slide is null)
        {
            return NotFound();
        }
        CurrentImageUrl = id is null ? null : ImageUrl(slide);

        var product = Input.ProductId is { } productId ? await db.Products.FindAsync(productId) : null;
        if (product is null)
        {
            ModelState.AddModelError("Input.ProductId", "Выберите товар.");
        }
        byte[]? upload = null;
        if (Image is { Length: > 0 })
        {
            using var memory = new MemoryStream();
            await Image.CopyToAsync(memory);
            upload = memory.ToArray();
            if (Image.Length > MaxImageBytes)
            {
                ModelState.AddModelError(nameof(Image), "Файл больше 20 МБ.");
            }
            else if (!MediaStorage.LooksLikeImage(Image.FileName, upload))
            {
                ModelState.AddModelError(nameof(Image), "Это не похоже на картинку. Подходят PNG и WEBP с прозрачным фоном.");
            }
        }
        else if (slide.Id == 0)
        {
            ModelState.AddModelError(nameof(Image), "Загрузите 3D-рендер товара.");
        }
        if (!ModelState.IsValid)
        {
            await LoadProductsAsync();
            return Page();
        }

        var warnings = new List<string>();
        var oldImage = slide.ImagePath;
        var oldIsSite = slide.IsSiteImage;
        string? newImage = null;
        var colors = (Bg: Input.BgColor, Script: Input.ScriptColor);
        if (upload is not null)
        {
            if (!HasTransparentEdges(upload))
            {
                warnings.Add("У рендера, похоже, непрозрачный фон: на слайде будет виден прямоугольник. Лучше PNG или WEBP с прозрачным фоном.");
            }
            // Цвета с этикетки, если менеджер не выбрал свои (без скрипта поля приходят пустыми).
            if (!ColorsTouched || !SlideColors.IsHex(colors.Bg))
            {
                colors = SlideColors.FromImage(upload);
            }
            var shrunk = ImageShrinker.Shrink(upload, Path.GetExtension(Image!.FileName), MaxSide, quality: 90);
            newImage = await media.SaveAsync(shrunk.Content, "slides", Path.ChangeExtension(Image.FileName, shrunk.Extension));
            slide.ImagePath = newImage;
        }
        slide.BgColor = SlideColors.IsHex(colors.Bg) ? colors.Bg!.ToLowerInvariant() : slide.Id == 0 ? SlideColors.Fallback : slide.BgColor;
        slide.ScriptColor = SlideColors.IsHex(colors.Script) ? colors.Script!.ToLowerInvariant() : SlideColors.ScriptFor(slide.BgColor);

        slide.ProductId = product!.Id;
        slide.Title = string.IsNullOrWhiteSpace(Input.Title) ? SlideText.Title(product.Name) : Input.Title.Trim();
        slide.Script = Input.Script?.Trim() ?? "";
        slide.Text = string.IsNullOrWhiteSpace(Input.Text) ? SlideText.Text(product.Description) : Input.Text.Trim();
        slide.IsActive = Input.IsActive;
        if (slide.Script.Length > ScriptComfortLength)
        {
            warnings.Add($"Рукописное слово длинное ({slide.Script.Length} букв): на сайте оно станет мельче. Лучше до {ScriptComfortLength}.");
        }
        if (slide.Id == 0)
        {
            slide.SortOrder = (await db.ProductSlides.MaxAsync(s => (int?)s.SortOrder) ?? 0) + 1;
            db.ProductSlides.Add(slide);
        }
        try
        {
            await db.SaveChangesAsync();
        }
        catch
        {
            media.Delete(newImage);
            throw;
        }
        if (newImage is not null && oldImage.Length > 0 && !oldIsSite)
        {
            media.Delete(oldImage);
        }
        TempData.Flash(product.IsActive ? "Слайд сохранён." : "Слайд сохранён, но товар скрыт в каталоге — на сайте слайда не будет, пока товар не покажут.",
            product.IsActive ? FlashLevel.Success : FlashLevel.Info);
        foreach (var warning in warnings)
        {
            TempData.Flash(warning, FlashLevel.Info);
        }
        return RedirectToPage("/Admin/Banners/Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var slide = await db.ProductSlides.FindAsync(id);
        if (slide is null)
        {
            return NotFound();
        }
        db.ProductSlides.Remove(slide);
        await db.SaveChangesAsync();
        if (!slide.IsSiteImage)
        {
            media.Delete(slide.ImagePath);
        }
        TempData.Flash($"Слайд «{slide}» удалён.");
        return RedirectToPage("/Admin/Banners/Index");
    }

    private string ImageUrl(ProductSlide slide) => slide.IsSiteImage ? Url.Content("~" + slide.ImagePath)! : Media.Url(slide.ImagePath);

    private async Task LoadProductsAsync()
    {
        var products = await db.Products.Include(p => p.Variants).OrderBy(p => p.Name).AsNoTracking().ToListAsync();
        Products = products
            .Where(p => p.IsActive || p.Id == Input.ProductId)
            .Select(p => new ProductOption(p.Id, p.Name, SlideText.Title(p.Name), SlideText.Script(p.Name), SlideText.Text(p.Description),
                p.MinPrice is { } price ? (p.HasSeveralPacks ? "от " : "") + Format.Money(price) : ""))
            .ToList();
    }

    /// <summary>Прозрачные ли края картинки (углы и середины сторон): у рендера с фоном они непрозрачные.</summary>
    private static bool HasTransparentEdges(byte[] content)
    {
        using var bitmap = SKBitmap.Decode(content);
        if (bitmap is null || bitmap.AlphaType == SKAlphaType.Opaque)
        {
            return false;
        }
        int w = bitmap.Width - 1, h = bitmap.Height - 1;
        (int X, int Y)[] points = [(0, 0), (w, 0), (0, h), (w, h), (w / 2, 0), (w / 2, h), (0, h / 2), (w, h / 2)];
        return points.Count(p => bitmap.GetPixel(p.X, p.Y).Alpha < 128) >= 6;
    }
}
