using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Banners;

/// <summary>Баннер: картинку и сроки показа менеджер задаёт сам, для новой акции код менять не нужно.</summary>
public class EditModel(ShopDbContext db, MediaStorage media) : PageModel
{
    private const long MaxImageBytes = 10 * 1024 * 1024;

    public class InputModel
    {
        [Display(Name = "Заголовок")]
        [StringLength(200, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Title { get; set; }

        [Display(Name = "Ссылка при нажатии")]
        [StringLength(300, ErrorMessage = "Не длиннее {1} символов.")]
        public string? LinkUrl { get; set; }

        [Display(Name = "Очерёдность в слайдере")]
        [Range(0, int.MaxValue, ErrorMessage = "Число не меньше 0.")]
        public int SortOrder { get; set; }

        [Display(Name = "Показывать на сайте")]
        public bool IsActive { get; set; } = true;

        // Время по Актау из поля datetime-local, в базу уходит в UTC.
        [Display(Name = "Показывать с")] public DateTime? StartsAt { get; set; }
        [Display(Name = "Показывать до")] public DateTime? EndsAt { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public IFormFile? Image { get; set; }

    public int? Id { get; private set; }
    public string? CurrentImage { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is null)
        {
            return Page();
        }
        var banner = await db.Banners.FindAsync(id);
        if (banner is null)
        {
            return NotFound();
        }
        CurrentImage = banner.ImagePath;
        Input = new InputModel
        {
            Title = banner.Title, LinkUrl = banner.LinkUrl, SortOrder = banner.SortOrder, IsActive = banner.IsActive,
            StartsAt = banner.StartsAt is { } s ? Format.ToLocal(s) : null,
            EndsAt = banner.EndsAt is { } e ? Format.ToLocal(e) : null,
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        var banner = id is null ? new Banner() : await db.Banners.FindAsync(id);
        if (banner is null)
        {
            return NotFound();
        }
        CurrentImage = id is null ? null : banner.ImagePath;

        byte[]? upload = null;
        if (Image is { Length: > 0 })
        {
            using var memory = new MemoryStream();
            await Image.CopyToAsync(memory);
            upload = memory.ToArray();
            if (Image.Length > MaxImageBytes)
            {
                ModelState.AddModelError(nameof(Image), "Файл больше 10 МБ.");
            }
            else if (!MediaStorage.LooksLikeImage(Image.FileName, upload))
            {
                ModelState.AddModelError(nameof(Image), "Это не похоже на картинку. Подходят JPG, PNG, GIF и WEBP.");
            }
        }
        else if (banner.Id == 0)
        {
            ModelState.AddModelError(nameof(Image), "Выберите картинку баннера.");
        }
        if (Input.StartsAt is { } from && Input.EndsAt is { } to && to < from)
        {
            ModelState.AddModelError("Input.EndsAt", "Конец показа раньше начала.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var oldImage = banner.ImagePath;
        string? newImage = null;
        if (upload is not null)
        {
            newImage = await media.SaveAsync(upload, "banners", Image!.FileName);
            banner.ImagePath = newImage;
        }
        banner.Title = Input.Title?.Trim() ?? "";
        banner.LinkUrl = Input.LinkUrl?.Trim() ?? "";
        banner.SortOrder = Input.SortOrder;
        banner.IsActive = Input.IsActive;
        banner.StartsAt = Input.StartsAt is { } start ? Format.FromLocal(start) : null;
        banner.EndsAt = Input.EndsAt is { } end ? Format.FromLocal(end) : null;
        if (banner.Id == 0)
        {
            db.Banners.Add(banner);
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
        // Старую картинку удаляем только после того, как новая записана в базу.
        if (newImage is not null && oldImage.Length > 0)
        {
            media.Delete(oldImage);
        }
        TempData.Flash("Баннер сохранён.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var banner = await db.Banners.FindAsync(id);
        if (banner is null)
        {
            return NotFound();
        }
        db.Banners.Remove(banner);
        await db.SaveChangesAsync();
        media.Delete(banner.ImagePath);
        TempData.Flash("Баннер удалён.");
        return RedirectToPage("Index");
    }
}
