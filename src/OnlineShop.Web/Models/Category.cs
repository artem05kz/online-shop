using System.ComponentModel.DataAnnotations;

namespace OnlineShop.Web.Models;

/// <summary>
/// Категория внутри бренда, например «Профессиональная автохимия».
/// Вложенность через Parent нужна потому, что 1С выгружает группы деревом
/// (категория, подкатегория, товар).
/// </summary>
public class Category
{
    public int Id { get; set; }

    [Display(Name = "Бренд")]
    public int BrandId { get; set; }
    public Brand Brand { get; set; } = null!;

    [Display(Name = "Родительская категория")]
    public int? ParentId { get; set; }
    public Category? Parent { get; set; }
    public List<Category> Children { get; set; } = [];

    [Display(Name = "Название")]
    [Required, StringLength(200)]
    public string Name { get; set; } = "";

    // Уникален в пределах бренда: адрес категории строится как /catalog/{бренд}/{категория}/.
    [Display(Name = "Ярлык для ссылки")]
    [StringLength(220)]
    public string Slug { get; set; } = "";

    [Display(Name = "Очерёдность в списке категорий",
        Description = "Категории выводятся по этому числу от меньшего к большему. Например, категория "
            + "с числом 1 будет показана раньше категории с числом 2. При одинаковых числах категории "
            + "сортируются по названию.")]
    [Range(0, int.MaxValue)]
    public int SortOrder { get; set; }

    [Display(Name = "Показывать на сайте")]
    public bool IsActive { get; set; } = true;

    public List<Product> Products { get; set; } = [];

    public override string ToString() => $"{Brand?.Name} / {Name}";
}
