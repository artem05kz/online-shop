using OnlineShop.Web.Models;

namespace OnlineShop.Web.Infrastructure;

/// <summary>Адреса загруженных файлов: фото товаров и баннеров отдаются из папки загрузок по пути /media/.</summary>
public static class Media
{
    public const string RequestPath = "/media";
    public const string NoPhoto = "/img/no-photo.svg";

    public static string Url(string relativePath) => $"{RequestPath}/{relativePath.TrimStart('/')}";

    public static string PhotoUrl(Product product) =>
        product.PrimaryImage is { } image ? Url(image.ImagePath) : NoPhoto;
}
