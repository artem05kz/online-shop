using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Services.Email;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Orders;

public class IndexModel(ShopDbContext db, AccountMail mail, StockReservations reservations) : PageModel
{
    public class Row
    {
        public int Id { get; set; }
        public string UserName { get; set; } = "";
        public string ContactName { get; set; } = "";
        public string ContactPhone { get; set; } = "";
        public string City { get; set; } = "";
        public bool IsPickup { get; set; }
        public DateTime CreatedAt { get; set; }
        public OrderStatus Status { get; set; }
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? O { get; set; }

    [BindProperty] public List<Row> Rows { get; set; } = [];
    public PagerInfo Pager { get; private set; } = new(1, 1, 0);

    public async Task OnGetAsync(string? p)
    {
        var query = db.Orders.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var needle = Q.Trim().ToLowerInvariant();
            query = query.Where(o => SqliteFunctions.Lower(o.ContactPhone).Contains(needle)
                || SqliteFunctions.Lower(o.ContactName).Contains(needle)
                || SqliteFunctions.Lower(o.User.UserName!).Contains(needle)
                // По артикулу: найти, кто заказывал конкретную фасовку.
                || o.Items.Any(i => SqliteFunctions.Lower(i.Article).Contains(needle))
                || o.Id.ToString() == needle);
        }
        if (!string.IsNullOrEmpty(Status))
        {
            var status = OrderStatusInfo.FromCode(Status);
            query = query.Where(o => o.Status == status);
        }
        query = O switch
        {
            "id" => query.OrderBy(o => o.Id),
            "-id" => query.OrderByDescending(o => o.Id),
            "created" => query.OrderBy(o => o.CreatedAt),
            _ => query.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id),
        };

        var rows = query.Select(o => new Row
        {
            Id = o.Id, UserName = o.User.UserName ?? "", ContactName = o.ContactName, ContactPhone = o.ContactPhone,
            City = o.City, IsPickup = o.IsPickup, CreatedAt = o.CreatedAt, Status = o.Status,
        });
        var list = await PagedList<Row>.CreateAsync(rows, p, AdminList.PageSize);
        Pager = PagerInfo.From(list);
        Rows = list.Items.ToList();
    }

    /// <summary>Статусы правятся прямо в списке: обычно это всё, что нужно сделать с заказом.</summary>
    public async Task<IActionResult> OnPostSaveListAsync()
    {
        var byId = Rows.ToDictionary(r => r.Id, r => r.Status);
        var orders = await db.Orders.Where(o => byId.Keys.Contains(o.Id)).ToListAsync();
        var changed = new List<int>();
        foreach (var order in orders.Where(o => o.Status != byId[o.Id]))
        {
            order.Status = byId[order.Id];
            changed.Add(order.Id);
        }
        await db.SaveChangesAsync();
        await reservations.RecalculateForOrdersAsync(changed);
        // Покупатель узнаёт о новом статусе из письма.
        await mail.NotifyStatusChangedAsync(changed, $"{Request.Scheme}://{Request.Host}");
        TempData.Flash(changed.Count == 0 ? "Изменений не было." : $"Статус изменён у заказов: {changed.Count}. Покупателям с подтверждённой почтой ушли письма.");
        return RedirectToPage(new { q = Q, status = Status, o = O, p = Request.Query["p"].ToString() });
    }
}
