using System.ComponentModel.DataAnnotations;

namespace OnlineShop.Web.Models;

public class ProductImage
{
    public int Id { get; set; }

    [Display(Name = "Товар")]
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // Путь к файлу относительно папки загрузок, например products/2026/10/131122.jpg.
    [Display(Name = "Фотография")]
    [Required, StringLength(255)]
    public string ImagePath { get; set; } = "";

    [Display(Name = "Основная фотография")]
    public bool IsPrimary { get; set; }

    [Display(Name = "Очерёдность фотографий",
        Description = "Если фотографий несколько, они выводятся по этому числу от меньшего к большему.")]
    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    public override string ToString() => $"фото товара «{Product?.Name}»";
}
