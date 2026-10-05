using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Users;

/// <summary>
/// Покупатели и доступ в панель. В Django этим занимался встроенный раздел «Пользователи»
/// с флагом «Статус персонала»; здесь то же самое через роль «Менеджер».
/// </summary>
public class IndexModel(ShopDbContext db, UserManager<ApplicationUser> users) : PageModel
{
    public record Row(int Id, string UserName, string FullName, string Email, int Orders, bool IsManager);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public PagedList<Row> List { get; private set; } = null!;

    public async Task OnGetAsync(string? p)
    {
        var managerRole = await db.Roles.Where(r => r.Name == RoleNames.Manager).Select(r => r.Id).SingleAsync();
        var query = db.Users.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var needle = Q.Trim().ToLowerInvariant();
            query = query.Where(u => SqliteFunctions.Lower(u.UserName!).Contains(needle)
                || SqliteFunctions.Lower(u.FirstName).Contains(needle)
                || SqliteFunctions.Lower(u.LastName).Contains(needle)
                || (u.Email != null && SqliteFunctions.Lower(u.Email).Contains(needle)));
        }
        var rows = query.OrderBy(u => u.UserName).Select(u => new Row(
            u.Id, u.UserName ?? "", (u.FirstName + " " + u.LastName).Trim(), u.Email ?? "", u.Orders.Count,
            db.UserRoles.Any(r => r.UserId == u.Id && r.RoleId == managerRole)));
        List = await PagedList<Row>.CreateAsync(rows, p, AdminList.PageSize);
    }

    public async Task<IActionResult> OnPostToggleManagerAsync(int id)
    {
        var user = await users.FindByIdAsync(id.ToString());
        if (user is null)
        {
            return NotFound();
        }
        if (await users.IsInRoleAsync(user, RoleNames.Manager))
        {
            // Последний вход в панель самому себе не закрыть: иначе панель останется без менеджера.
            if (id == User.ShopUserId())
            {
                TempData.Flash("Снять доступ с самого себя нельзя. Попросите об этом другого менеджера.", FlashLevel.Error);
                return RedirectToPage(new { q = Q });
            }
            await users.RemoveFromRoleAsync(user, RoleNames.Manager);
            await users.UpdateSecurityStampAsync(user);
            TempData.Flash($"У «{user.UserName}» больше нет доступа в панель.");
        }
        else
        {
            await users.AddToRoleAsync(user, RoleNames.Manager);
            await users.UpdateSecurityStampAsync(user);
            TempData.Flash($"«{user.UserName}» теперь менеджер. Доступ появится после повторного входа на сайт.");
        }
        return RedirectToPage(new { q = Q });
    }
}
