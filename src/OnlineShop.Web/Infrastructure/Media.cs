using OnlineShop.Web.Models;

namespace OnlineShop.Web.Infrastructure;

/// <summary>Адреса загруженных файлов: фото товаров и баннеров отдаются из папки загрузок по пути /media/.</summary>
public static class Media
{
    public const string RequestPath = "/media";
    public const string NoPhoto = "/img/no-photo.svg";

    /// <summary>Миниатюра в панели и корзине.</summary>
    public const int Small = 160;
    /// <summary>Карточка товара в списке, плитка категории.</summary>
    public const int Card = 400;
    /// <summary>Большое фото на странице товара.</summary>
    public const int Large = 800;

    public static string Url(string relativePath) => $"{RequestPath}/{relativePath.TrimStart('/')}";

    /// <summary>Уменьшенная копия фото нужного размера (см. Thumbnails).</summary>
    public static string Thumb(string relativePath, int size) => $"{RequestPath}/thumb/{size}/{relativePath.TrimStart('/')}";

    public static string PhotoUrl(Product product, int size = Card) =>
        product.PrimaryImage is { } image ? Thumb(image.ImagePath, size) : NoPhoto;
}
