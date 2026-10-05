using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

public enum OrderStatus
{
    New,
    Confirmed,
    Delivering,
    Completed,
    Canceled,
}

public static class OrderStatusInfo
{
    // Коды совпадают с Django-версией: они хранятся в базе и попадают в CSS-классы status-new и т. п.
    public static string ToCode(this OrderStatus status) => status switch
    {
        OrderStatus.Confirmed => "confirmed",
        OrderStatus.Delivering => "delivering",
        OrderStatus.Completed => "completed",
        OrderStatus.Canceled => "canceled",
        _ => "new",
    };

    public static OrderStatus FromCode(string code) => code switch
    {
        "confirmed" => OrderStatus.Confirmed,
        "delivering" => OrderStatus.Delivering,
        "completed" => OrderStatus.Completed,
        "canceled" => OrderStatus.Canceled,
        _ => OrderStatus.New,
    };

    public static string Label(this OrderStatus status) => status switch
    {
        OrderStatus.Confirmed => "Подтверждён",
        OrderStatus.Delivering => "Доставляется",
        OrderStatus.Completed => "Выполнен",
        OrderStatus.Canceled => "Отменён",
        _ => "Новый",
    };
}

/// <summary>Шаг полоски статуса заказа в истории заказов покупателя.</summary>
public record OrderProgressStep(string Label, bool IsDone, bool IsCurrent);

public class Order
{
    // Путь заказа для полоски статуса. Отмена в него не входит: у отменённого заказа полоски нет.
    public static readonly IReadOnlyList<OrderStatus> StatusFlow =
        [OrderStatus.New, OrderStatus.Confirmed, OrderStatus.Delivering, OrderStatus.Completed];

    public int Id { get; set; }

    [Display(Name = "Покупатель")]
    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [Display(Name = "Статус")]
    public OrderStatus Status { get; set; } = OrderStatus.New;

    // Данные доставки копируются в заказ на момент оформления, чтобы последующая правка
    // адресной книги покупателя не переписывала историю.

    [Display(Name = "Имя получателя")]
    [Required, StringLength(200)]
    public string ContactName { get; set; } = "";

    [Display(Name = "Телефон")]
    [Required, StringLength(30)]
    public string ContactPhone { get; set; } = "";

    [Display(Name = "Город")]
    [Required, StringLength(100)]
    public string City { get; set; } = "";

    [Display(Name = "Адрес доставки")]
    [Required, StringLength(300)]
    public string StreetAndDetails { get; set; } = "";

    [Display(Name = "Комментарий")]
    public string Comment { get; set; } = "";

    [Display(Name = "Дата создания")]
    public DateTime CreatedAt { get; set; }

    [Display(Name = "Дата изменения")]
    public DateTime UpdatedAt { get; set; }

    public List<OrderItem> Items { get; set; } = [];

    [NotMapped]
    public decimal Total => Items.Sum(i => i.Subtotal);

    [NotMapped]
    public int TotalQuantity => Items.Sum(i => i.Quantity);

    [NotMapped]
    public bool IsCanceled => Status == OrderStatus.Canceled;

    [NotMapped]
    public IReadOnlyList<OrderProgressStep> ProgressSteps
    {
        get
        {
            if (IsCanceled)
            {
                return [];
            }
            var currentIndex = Math.Max(0, StatusFlow.ToList().IndexOf(Status));
            return StatusFlow
                .Select((status, i) => new OrderProgressStep(status.Label(), i < currentIndex, i == currentIndex))
                .ToList();
        }
    }

    public override string ToString() => $"Заказ №{Id} ({Status.Label()})";
}
