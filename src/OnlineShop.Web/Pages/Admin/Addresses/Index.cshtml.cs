using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Addresses;

public class IndexModel(ShopDbContext db) : PageModel
{
    public record Row(int Id, string UserName, string FullName, string Phone, string City, string Street, bool IsDefault);

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }

    public PagedList<Row> List { get; private set; } = null!;

    public async Task OnGetAsync(string? p)
    {
        var query = db.Addresses.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var needle = Q.Trim().ToLowerInvariant();
            query = query.Where(a => SqliteFunctions.Lower(a.FullName).Contains(needle)
                || SqliteFunctions.Lower(a.Phone).Contains(needle)
                || SqliteFunctions.Lower(a.User.UserName!).Contains(needle));
        }
        var rows = query.OrderBy(a => a.User.UserName).ThenByDescending(a => a.IsDefault).ThenByDescending(a => a.Id)
            .Select(a => new Row(a.Id, a.User.UserName ?? "", a.FullName, a.Phone, a.City, a.Street, a.IsDefault));
        List = await PagedList<Row>.CreateAsync(rows, p, AdminList.PageSize);
    }
}
