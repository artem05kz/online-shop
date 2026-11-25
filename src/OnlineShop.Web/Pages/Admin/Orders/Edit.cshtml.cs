using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Services;
using OnlineShop.Web.Services.Email;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Orders;

/// <summary>
/// Карточка заказа (редизайн 24.11): сверху статус полосой шагов и кнопка «следующий шаг»; слева состав
/// с фото и суммой с доставкой (правило 7 000 ₸, как у покупателя), справа покупатель с кнопками
/// «Позвонить» и WhatsApp, получение и комментарий. Данные и количество правятся ниже, «Сохранить» внизу.
/// Заказ: статус, данные доставки, количество по позициям. Название, фасовка и цена позиции
/// только для чтения: это снимок на момент заказа, и покупатель видит именно его.
/// </summary>
public class EditModel(ShopDbContext db, AccountMail mail, StockReservations reservations) : PageModel
{
    private const string Required = "Обязательное поле.";
    private const string TooLong = "Не длиннее {1} символов.";

    public class InputModel
    {
        [Display(Name = "Статус")] public OrderStatus Status { get; set; }

        [Display(Name = "Имя получателя")]
        [Required(ErrorMessage = Required), StringLength(200, ErrorMessage = TooLong)]
        public string ContactName { get; set; } = "";

        [Display(Name = "Телефон")]
        [Required(ErrorMessage = Required), StringLength(30, ErrorMessage = TooLong)]
        public string ContactPhone { get; set; } = "";

        [Display(Name = "Город")]
        [Required(ErrorMessage = Required), StringLength(100, ErrorMessage = TooLong)]
        public string City { get; set; } = "";

        [Display(Name = "Адрес доставки")]
        [Required(ErrorMessage = Required), StringLength(300, ErrorMessage = TooLong)]
        public string StreetAndDetails { get; set; } = "";

        [Display(Name = "Комментарий")] public string? Comment { get; set; }
    }

    public class ItemRow
    {
        public int Id { get; set; }
        [Range(1, 100000, ErrorMessage = "От 1 до 100 000.")]
        public int Quantity { get; set; }
        public bool Delete { get; set; }
    }

    [BindProperty] public InputModel Input { get; set; } = new();
    [BindProperty] public List<ItemRow> Items { get; set; } = [];

    public Order Order { get; private set; } = null!;

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var order = await Load(id);
        if (order is null)
        {
            return NotFound();
        }
        Order = order;
        Input = new InputModel
        {
            Status = order.Status, ContactName = order.ContactName, ContactPhone = order.ContactPhone, City = order.City,
            StreetAndDetails = order.StreetAndDetails, Comment = order.Comment,
        };
        Items = order.Items.OrderBy(i => i.Id).Select(i => new ItemRow { Id = i.Id, Quantity = i.Quantity }).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        var order = await Load(id);
        if (order is null)
        {
            return NotFound();
        }
        Order = order;
        if (Items.Count(i => !i.Delete) == 0 && order.Items.Count > 0)
        {
            ModelState.AddModelError("", "В заказе должна остаться хотя бы одна позиция. Чтобы отказаться от заказа, поставьте статус «Отменён».");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var statusChanged = order.Status != Input.Status;
        order.Status = Input.Status;
        order.ContactName = Input.ContactName.Trim();
        order.ContactPhone = Input.ContactPhone.Trim();
        order.City = Input.City.Trim();
        order.StreetAndDetails = Input.StreetAndDetails.Trim();
        order.Comment = Input.Comment?.Trim() ?? "";
        foreach (var row in Items)
        {
            var item = order.Items.FirstOrDefault(i => i.Id == row.Id);
            if (item is null)
            {
                continue;
            }
            if (row.Delete)
            {
                db.OrderItems.Remove(item);
            }
            else
            {
                item.Quantity = row.Quantity;
            }
        }
        // Фасовки до правки тоже: удалённая позиция больше не держит резерв.
        var variantIds = order.Items.Select(i => i.VariantId).Distinct().ToList();
        await db.SaveChangesAsync();
        await reservations.RecalculateAsync(variantIds);
        if (statusChanged)
        {
            await mail.NotifyStatusChangedAsync([order.Id], $"{Request.Scheme}://{Request.Host}");
        }
        TempData.Flash($"Заказ №{order.Id} сохранён.");
        return RedirectToPage(new { id });
    }

    /// <summary>Кнопка «следующий шаг» наверху карточки: подтвердить, передать в доставку, выполнен.</summary>
    public async Task<IActionResult> OnPostNextAsync(int id)
    {
        var next = await OrderSteps.AdvanceAsync(db, reservations, mail, id, $"{Request.Scheme}://{Request.Host}");
        TempData.Flash(next is { } status ? $"Заказ №{id}: статус «{status.Label()}». Покупателю с подтверждённой почтой ушло письмо." : $"Заказ №{id} уже нельзя продвинуть дальше.");
        return RedirectToPage(new { id });
    }

    private Task<Order?> Load(int id) =>
        db.Orders.Include(o => o.User).Include(o => o.PickupStore)
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v.Product).ThenInclude(p => p.Images)
            .AsSplitQuery()
            .FirstOrDefaultAsync(o => o.Id == id);
}
