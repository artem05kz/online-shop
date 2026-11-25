using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Banners;

/// <summary>
/// Баннеры (25.11.2026): карточки с картинкой и состоянием («на сайте», «выключен», «ждёт начала», «закончился»),
/// вкладки по состоянию. Включить и выключить, сдвинуть раньше или позже, удалить — сразу, без отдельного «Сохранить».
/// </summary>
public class IndexModel(ShopDbContext db, MediaStorage media) : PageModel
{
    public enum State { Live, Off, Planned, Ended }

    public record Card(Banner Banner, State State, int? Position);

    public static readonly IReadOnlyList<(string Value, string Label)> TabList =
    [
        ("", "Все"), ("live", "На сайте"), ("off", "Выключены"), ("planned", "Ждут начала"), ("ended", "Закончились"),
    ];

    [BindProperty(SupportsGet = true)] public string? Show { get; set; }

    public List<Card> Cards { get; private set; } = [];

    /// <summary>Все баннеры по порядку (для вкладок, ленты «Сейчас на главной» и стрелок).</summary>
    public List<Card> All { get; private set; } = [];

    public Dictionary<string, int> Counts { get; private set; } = [];

    /// <summary>
    /// Слайд с товаром и его товар для витрины (с ценой). Product пусто — товар скрыт, слайд не показывается.
    /// Position — какой по счёту слайд на главной (после включённых баннеров), пусто — не показывается.
    /// </summary>
    public record SlideCard(ProductSlide Slide, Product? Product, int? Position);

    public List<SlideCard> Slides { get; private set; } = [];

    public static State StateOf(Banner b, DateTime nowUtc) =>
        !b.IsActive ? State.Off
        : b.StartsAt is { } from && from > nowUtc ? State.Planned
        : b.EndsAt is { } to && to < nowUtc ? State.Ended
        : State.Live;

    public static string Code(State state) => state switch
    {
        State.Live => "live", State.Off => "off", State.Planned => "planned", _ => "ended",
    };

    public async Task OnGetAsync()
    {
        var now = DateTime.UtcNow;
        var banners = await db.Banners.Ordered().AsNoTracking().ToListAsync();
        var position = 0;
        All = banners.Select(b =>
        {
            var state = StateOf(b, now);
            return new Card(b, state, state == State.Live ? ++position : null);
        }).ToList();
        Counts = TabList.ToDictionary(t => t.Value, t => t.Value == "" ? All.Count : All.Count(c => Code(c.State) == t.Value));
        Cards = string.IsNullOrEmpty(Show) ? All : All.Where(c => Code(c.State) == Show).ToList();

        var slides = await db.ProductSlides.Include(s => s.Product).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).AsNoTracking().ToListAsync();
        var ids = slides.Select(s => s.ProductId).ToList();
        var products = await db.Products.ForListing().Where(p => ids.Contains(p.Id)).AsNoTracking().ToDictionaryAsync(p => p.Id);
        var shown = position;
        Slides = slides.Select(s =>
        {
            var product = products.GetValueOrDefault(s.ProductId);
            return new SlideCard(s, product, s.IsActive && product is not null ? ++shown : null);
        }).ToList();
    }

    public async Task<IActionResult> OnPostToggleAsync(int id)
    {
        var banner = await db.Banners.FindAsync(id);
        if (banner is null)
        {
            return NotFound();
        }
        banner.IsActive = !banner.IsActive;
        await db.SaveChangesAsync();
        TempData.Flash(banner.IsActive ? $"«{banner}» включён." : $"«{banner}» выключен и не показывается на сайте.");
        return RedirectToPage(new { show = Show });
    }

    /// <summary>Сдвинуть баннер раньше (step = -1) или позже (step = 1). Очерёдность у всех становится 1, 2, 3…</summary>
    public async Task<IActionResult> OnPostMoveAsync(int id, int step)
    {
        var banners = await db.Banners.Ordered().ToListAsync();
        var index = banners.FindIndex(b => b.Id == id);
        if (index < 0)
        {
            return NotFound();
        }
        var target = Math.Clamp(index + Math.Sign(step), 0, banners.Count - 1);
        (banners[index], banners[target]) = (banners[target], banners[index]);
        for (var i = 0; i < banners.Count; i++)
        {
            banners[i].SortOrder = i + 1;
        }
        await db.SaveChangesAsync();
        return RedirectToPage(new { show = Show });
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
        TempData.Flash($"Баннер «{banner}» удалён.");
        return RedirectToPage(new { show = Show });
    }

    // ---- Слайды с товарами: включить и выключить, раньше и позже, удалить — сразу ----

    public async Task<IActionResult> OnPostToggleSlideAsync(int id)
    {
        var slide = await db.ProductSlides.FindAsync(id);
        if (slide is null)
        {
            return NotFound();
        }
        slide.IsActive = !slide.IsActive;
        await db.SaveChangesAsync();
        TempData.Flash(slide.IsActive ? $"Слайд «{slide}» включён." : $"Слайд «{slide}» выключен и не показывается на сайте.");
        return RedirectToPage(new { show = Show });
    }

    public async Task<IActionResult> OnPostMoveSlideAsync(int id, int step)
    {
        var slides = await db.ProductSlides.OrderBy(s => s.SortOrder).ThenBy(s => s.Id).ToListAsync();
        var index = slides.FindIndex(s => s.Id == id);
        if (index < 0)
        {
            return NotFound();
        }
        var target = Math.Clamp(index + Math.Sign(step), 0, slides.Count - 1);
        (slides[index], slides[target]) = (slides[target], slides[index]);
        for (var i = 0; i < slides.Count; i++)
        {
            slides[i].SortOrder = i + 1;
        }
        await db.SaveChangesAsync();
        return RedirectToPage(new { show = Show });
    }

    public async Task<IActionResult> OnPostDeleteSlideAsync(int id)
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
        return RedirectToPage(new { show = Show });
    }
}
