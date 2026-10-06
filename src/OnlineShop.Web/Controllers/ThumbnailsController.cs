using Microsoft.AspNetCore.Mvc;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Controllers;

public class ThumbnailsController(Thumbnails thumbnails) : Controller
{
    /// <summary>
    /// Уменьшенная копия фото: /media/thumb/400/products/2026/10/131122.jpeg. Если уменьшить нельзя
    /// (SVG или битый файл), перенаправляет на исходник, чтобы картинка всё равно показалась.
    /// </summary>
    [HttpGet("/media/thumb/{size:int}/{**path}")]
    public IActionResult Get(int size, string path)
    {
        if (thumbnails.GetOrCreate(path, size) is { } file)
        {
            // Имя файла меняется при замене фото, поэтому копию можно долго хранить в браузере.
            Response.Headers.CacheControl = "public, max-age=604800";
            return PhysicalFile(file, "image/webp");
        }
        return Redirect(Media.Url(path));
    }
}
