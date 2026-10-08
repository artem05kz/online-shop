using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

/// <summary>
/// Карточка товара на сайте. У товара может быть несколько фасовок со своим
/// артикулом, ценой и остатком: в 1С канистра 22 кг и бутылка 1 кг одного
/// средства считаются разными позициями.
/// </summary>
public class Product
{
    public const int SlugMaxLength = 320;

    public int Id { get; set; }

    [Display(Name = "Категория")]
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;

    [Display(Name = "Название")]
    [Required, StringLength(300)]
    public string Name { get; set; } = "";

    [Display(Name = "Ярлык для ссылки")]
    [StringLength(SlugMaxLength)]
    public string Slug { get; set; } = "";

    [Display(Name = "Описание")]
    public string Description { get; set; } = "";

    [Display(Name = "Кислотность (pH)",
        Description = "Необязательно. Заполняйте, только если для этого товара важно указать pH. "
            + "Если оставить пустым, на сайте эта строка просто не появится.")]
    [StringLength(50)]
    public string Ph { get; set; } = "";

    [Display(Name = "Показывать на сайте")]
    public bool IsActive { get; set; } = true;

    // Исходный текст группы из выгрузки 1С, чтобы при настройке импорта было видно, откуда взялся товар.
    [Display(Name = "Путь категории в файле 1С")]
    [StringLength(500)]
    public string SourceGroupPath { get; set; } = "";

    [Display(Name = "Дата создания")]
    public DateTime CreatedAt { get; set; }

    [Display(Name = "Дата изменения")]
    public DateTime UpdatedAt { get; set; }

    public List<ProductVariant> Variants { get; set; } = [];
    public List<ProductImage> Images { get; set; } = [];

    // Свойства ниже считаются по уже загруженным Variants и Images, поэтому запрос
    // списка товаров должен подгружать их через Include, иначе получится пусто.

    [NotMapped]
    public ProductImage? PrimaryImage
    {
        get
        {
            var ordered = Images.OrderBy(i => i.SortOrder).ThenBy(i => i.Id).ToList();
            return ordered.FirstOrDefault(i => i.IsPrimary) ?? ordered.FirstOrDefault();
        }
    }

    [NotMapped]
    public IReadOnlyList<ProductVariant> ActiveVariants =>
        Variants.Where(v => v.IsActive).OrderBy(v => v.SortVolume).ThenBy(v => v.Id).ToList();

    [NotMapped]
    public decimal? MinPrice =>
        ActiveVariants.Where(v => v.Price is not null).Select(v => v.Price).Min();

    [NotMapped]
    public bool HasSeveralPacks => ActiveVariants.Count > 1;

    /// <summary>
    /// Что кладёт в корзину кнопка «В корзину» на карточке в списке: самая дешёвая
    /// фасовка из тех, что есть на складе, а если на складе ничего нет, то просто самая дешёвая.
    /// </summary>
    [NotMapped]
    public ProductVariant? DefaultVariant
    {
        get
        {
            var priced = ActiveVariants.Where(v => v.Price is not null).ToList();
            if (priced.Count == 0)
            {
                return null;
            }
            var inStock = priced.Where(v => v.InStock).ToList();
            return (inStock.Count > 0 ? inStock : priced).MinBy(v => v.Price);
        }
    }

    [NotMapped]
    public bool InStock => ActiveVariants.Any(v => v.InStock);

    public override string ToString() => Name;
}
