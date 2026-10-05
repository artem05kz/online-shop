using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

public class OrderItem
{
    public int Id { get; set; }

    [Display(Name = "Заказ")]
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [Display(Name = "Артикул товара")]
    public int VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;

    // Название и цена копируются на момент заказа: цена в каталоге потом может измениться.

    [Display(Name = "Название товара")]
    [Required, StringLength(300)]
    public string ProductName { get; set; } = "";

    [Display(Name = "Фасовка")]
    [StringLength(50)]
    public string PackLabel { get; set; } = "";

    [Display(Name = "Артикул")]
    [StringLength(50)]
    public string Article { get; set; } = "";

    [Display(Name = "Цена")]
    public decimal Price { get; set; }

    [Display(Name = "Количество")]
    [Range(0, int.MaxValue)]
    public int Quantity { get; set; } = 1;

    [NotMapped]
    public decimal Subtotal => Price * Quantity;

    public override string ToString() => $"{ProductName} ({PackLabel})";
}
