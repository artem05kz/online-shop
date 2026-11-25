using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Services.Email;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Orders;

/// <summary>
/// Список заказов (редизайн 24.11): по умолчанию — заказы «в работе», сгруппированные по дням.
/// Строка-карточка: номер и время, фото товаров, покупатель с телефоном, куда везти, сумма к оплате
/// с доставкой, статус и кнопка «следующий шаг». Ручная смена статуса — в карточке заказа;
/// обработчик SaveList оставлен для совместимости.
/// </summary>
public class IndexModel(ShopDbContext db, AccountMail mail, StockReservations reservations) : PageModel
{
    public class Row
    {
        public int Id { get; set; }
        public string UserName { get; set; } = "";
        public string ContactName { get; set; } = "";
        public string ContactPhone { get; set; } = "";
        public string City { get; set; } = "";
        /// <summary>Улица и дом при доставке или адрес магазина при самовывозе — только для показа.</summary>
        public string Place { get; set; } = "";
        public bool IsPickup { get; set; }
        public DateTime CreatedAt { get; set; }
        public OrderStatus Status { get; set; }
    }

    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public string? Status { get; set; }
    [BindProperty(SupportsGet = true)] public string? O { get; set; }

    [BindProperty] public List<Row> Rows { get; set; } = [];

    public const string ActiveCode = "active";
    public const string AllCode = "all";

    /// <summary>Фото товаров заказа (до трёх) и сколько позиций всего.</summary>
    public Dictionary<int, (List<string> Photos, int Lines)> Previews { get; private set; } = [];

    /// <summary>Сколько заказов в каждом статусе — для вкладок.</summary>
    public Dictionary<OrderStatus, int> Counts { get; private set; } = [];
    public int AllCount => Counts.Values.Sum();

    /// <summary>Сумма товаров и число штук по заказам на этой странице.</summary>
    public Dictionary<int, (decimal Total, int Quantity)> Totals { get; private set; } = [];
    public PagerInfo Pager { get; private set; } = new(1, 1, 0);

    public async Task OnGetAsync(string? p)
    {
        var query = db.Orders.AsQueryable();
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var needle = Q.Trim().ToLowerInvariant();
            query = query.Where(o => SqliteFunctions.Lower(o.ContactPhone).Contains(needle)
                || SqliteFunctions.Lower(o.ContactName).Contains(needle)
                || (o.User != null && SqliteFunctions.Lower(o.User.UserName!).Contains(needle))
                // По артикулу: найти, кто заказывал конкретную фасовку.
                || o.Items.Any(i => SqliteFunctions.Lower(i.Article).Contains(needle))
                || o.Id.ToString() == needle);
        }
        // Без поиска и вкладки открываются заказы «в работе»: новые, подтверждённые, в доставке.
        // Выполненные и отменённые — на своих вкладках, чтобы не мешали.
        if (string.IsNullOrEmpty(Status) && string.IsNullOrWhiteSpace(Q))
        {
            Status = ActiveCode;
        }
        if (Status == ActiveCode)
        {
            query = query.Where(o => o.Status == OrderStatus.New || o.Status == OrderStatus.Confirmed || o.Status == OrderStatus.Delivering);
        }
        else if (!string.IsNullOrEmpty(Status) && Status != AllCode)
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
            Id = o.Id, UserName = o.User != null ? o.User.UserName ?? "" : "без регистрации", ContactName = o.ContactName, ContactPhone = o.ContactPhone,
            City = o.City, Place = o.IsPickup ? (o.PickupStore != null ? o.PickupStore.Address : "") : o.StreetAndDetails, IsPickup = o.IsPickup, CreatedAt = o.CreatedAt, Status = o.Status,
        });
        var list = await PagedList<Row>.CreateAsync(rows, p, AdminList.PageSize);
        Pager = PagerInfo.From(list);
        Rows = list.Items.ToList();

        Counts = (await db.Orders.GroupBy(o => o.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync())
            .ToDictionary(x => x.Key, x => x.Count);
        var ids = Rows.Select(r => r.Id).ToList();
        var lines = await db.OrderItems.Where(i => ids.Contains(i.OrderId))
            .Include(i => i.Variant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .AsNoTracking().AsSplitQuery().ToListAsync();
        Totals = lines.GroupBy(i => i.OrderId)
            .ToDictionary(g => g.Key, g => (g.Sum(i => i.Price * i.Quantity), g.Sum(i => i.Quantity)));
        Previews = lines.GroupBy(i => i.OrderId).ToDictionary(g => g.Key, g =>
            (g.OrderBy(i => i.Id).Take(3).Select(i => Media.PhotoUrl(i.Variant.Product, Media.Small)).ToList(), g.Count()));
    }

    /// <summary>Кнопка «следующий шаг» в строке: подтвердить, передать в доставку, выполнен.</summary>
    public async Task<IActionResult> OnPostNextAsync(int id)
    {
        var next = await OrderSteps.AdvanceAsync(db, reservations, mail, id, $"{Request.Scheme}://{Request.Host}");
        TempData.Flash(next is { } status ? $"Заказ №{id}: статус «{status.Label()}». Покупателю с подтверждённой почтой ушло письмо." : $"Заказ №{id} уже нельзя продвинуть дальше.");
        return RedirectToPage(new { q = Q, status = Status, o = O, p = Request.Query["p"].ToString() });
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
