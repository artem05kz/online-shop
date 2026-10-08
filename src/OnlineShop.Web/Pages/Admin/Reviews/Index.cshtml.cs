using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Reviews;

/// <summary>Проверка отзывов: на сайте показываются только опубликованные менеджером.</summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string Status { get; set; } = "pending";

    public PagedList<Review> List { get; private set; } = null!;
    public Dictionary<ReviewStatus, int> Counts { get; private set; } = [];

    public async Task OnGetAsync(string? p)
    {
        var status = ReviewStatusInfo.FromCode(Status);
        Status = status.ToCode();
        Counts = await db.Reviews.GroupBy(r => r.Status).Select(g => new { g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Key, x => x.Count);
        var query = db.Reviews.Where(r => r.Status == status)
            .Include(r => r.Product).ThenInclude(pr => pr.Images)
            .Include(r => r.User)
            .OrderByDescending(r => r.UpdatedAt);
        List = await PagedList<Review>.CreateAsync(query.AsNoTracking(), p, AdminList.PageSize);
    }

    public Task<IActionResult> OnPostApproveAsync(int id) => SetStatus(id, ReviewStatus.Approved, "Отзыв опубликован.");

    public Task<IActionResult> OnPostRejectAsync(int id) => SetStatus(id, ReviewStatus.Rejected, "Отзыв отклонён, на сайте его не видно.");

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        await db.Reviews.Where(r => r.Id == id).ExecuteDeleteAsync();
        TempData.Flash("Отзыв удалён.");
        return RedirectToPage(new { status = Status });
    }

    private async Task<IActionResult> SetStatus(int id, ReviewStatus status, string message)
    {
        await db.Reviews.Where(r => r.Id == id).ExecuteUpdateAsync(s => s.SetProperty(r => r.Status, status));
        TempData.Flash(message);
        return RedirectToPage(new { status = Status });
    }
}
