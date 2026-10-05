using System.ComponentModel.DataAnnotations;

namespace OnlineShop.Web.Models;

/// <summary>
/// Линейка товаров верхнего уровня, например Ifoam или Grass.
/// На главной странице это крупные заголовки каталога.
/// </summary>
public class Brand
{
    public int Id { get; set; }

    [Display(Name = "Название")]
    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    [Display(Name = "Ярлык для ссылки")]
    [StringLength(110)]
    public string Slug { get; set; } = "";

    [Display(Name = "Очерёдность на главной странице",
        Description = "Бренды выводятся по этому числу от меньшего к большему. Например, бренд с числом 1 "
            + "будет выше бренда с числом 2. При одинаковых числах бренды сортируются по названию.")]
    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    [Display(Name = "Показывать на сайте")]
    public bool IsActive { get; set; } = true;

    public List<Category> Categories { get; set; } = [];

    public override string ToString() => Name;
}
