using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

/// <summary>Позиция, которую можно купить: конкретный артикул и фасовка товара.</summary>
public class ProductVariant
{
    public int Id { get; set; }

    [Display(Name = "Товар")]
    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    // По артикулу импорт из 1С сопоставляет прайс-лист и остатки, поэтому он уникален.
    [Display(Name = "Артикул")]
    [Required, StringLength(50)]
    public string Article { get; set; } = "";

    [Display(Name = "Фасовка")]
    [StringLength(50)]
    public string PackLabel { get; set; } = "";

    [Display(Name = "Объём, число")]
    public decimal? VolumeValue { get; set; }

    [Display(Name = "Единица измерения")]
    [StringLength(20)]
    public string VolumeUnit { get; set; } = "";

    [Display(Name = "Количество в коробке")]
    [Range(0, int.MaxValue)]
    public int? BoxQty { get; set; }

    [Display(Name = "Количество на паллете")]
    [Range(0, int.MaxValue)]
    public int? PalletQty { get; set; }

    [Display(Name = "Оптовая цена")]
    public decimal? WholesalePrice { get; set; }

    [Display(Name = "Розничная цена")]
    public decimal? Price { get; set; }

    [Display(Name = "Цена до скидки")]
    public decimal? OldPrice { get; set; }

    [Display(Name = "Остаток на складе")]
    public int StockQty { get; set; }

    // Сколько штук лежит в незакрытых заказах с сайта (см. Services/StockReservations).
    // Пересчитывается сайтом, руками и из 1С не меняется.
    [Display(Name = "В резерве")]
    public int ReservedQty { get; set; }

    [Display(Name = "Показывать на сайте")]
    public bool IsActive { get; set; } = true;

    [Display(Name = "Дата последней загрузки цены")]
    public DateTime? LastPriceImportAt { get; set; }

    [Display(Name = "Дата последнего обновления остатка")]
    public DateTime? LastStockImportAt { get; set; }

    public List<OrderItem> OrderItems { get; set; } = [];

    /// <summary>Сколько ещё можно заказать: остаток из 1С без того, что уже заказано на сайте.</summary>
    [NotMapped]
    public int AvailableQty => Math.Max(0, StockQty - ReservedQty);

    [NotMapped]
    public bool InStock => AvailableQty > 0;

    /// <summary>
    /// Объём для сортировки фасовок: миллилитры и граммы в литрах и килограммах, иначе
    /// «500 мл» вставало бы после «21 л», потому что 500 больше 21.
    /// </summary>
    [NotMapped]
    public decimal? SortVolume
    {
        get
        {
            // Единица бывает с хвостом: «500 мл триггер» разбирается как «мл триггер».
            var unit = VolumeUnit.Trim().ToLowerInvariant();
            var small = unit.StartsWith("мл") || unit == "г" || unit.StartsWith("гр");
            return small ? VolumeValue / 1000m : VolumeValue;
        }
    }

    public override string ToString() =>
        $"{Product?.Name} ({(string.IsNullOrEmpty(PackLabel) ? Article : PackLabel)})";
}
