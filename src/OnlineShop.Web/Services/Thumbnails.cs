using SkiaSharp;

namespace OnlineShop.Web.Services;

/// <summary>
/// Уменьшенные копии фото в WebP. Фото из прайса весят до полумегабайта, а в списке товаров
/// показываются размером с ладонь: без копий главная весила бы несколько мегабайт.
/// Копия делается при первом показе и лежит в media/_thumbs/{размер}/, пока не поменяется исходник.
/// </summary>
public class Thumbnails(MediaStorage media, ILogger<Thumbnails> logger)
{
    /// <summary>Размеры, которые просит сайт: миниатюра в панели и корзине, карточка товара, большое фото.</summary>
    public static readonly IReadOnlySet<int> Sizes = new HashSet<int> { 160, 400, 800 };

    private const int Quality = 82;
    public const string Folder = "_thumbs";

    /// <summary>
    /// Путь к готовой копии, при необходимости делает её. null, если исходника нет
    /// или это не картинка, которую можно уменьшить (например, SVG-баннер): тогда отдаётся исходник.
    /// </summary>
    public string? GetOrCreate(string relativePath, int size)
    {
        var source = media.FullPath(relativePath);
        if (source is null || !File.Exists(source) || !Sizes.Contains(size) || IsVector(source))
        {
            return null;
        }

        var target = Path.Combine(media.Root, Folder, size.ToString(), relativePath + ".webp");
        if (File.Exists(target) && File.GetLastWriteTimeUtc(target) >= File.GetLastWriteTimeUtc(source))
        {
            return target;
        }

        try
        {
            using var original = SKBitmap.Decode(source);
            if (original is null)
            {
                return null;
            }
            var scale = Math.Min(1.0, (double)size / Math.Max(original.Width, original.Height));
            var width = Math.Max(1, (int)Math.Round(original.Width * scale));
            var height = Math.Max(1, (int)Math.Round(original.Height * scale));

            using var resized = scale < 1.0
                ? original.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKCubicResampler.Mitchell))
                : original.Copy();
            using var image = SKImage.FromBitmap(resized);
            using var data = image.Encode(SKEncodedImageFormat.Webp, Quality);

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            // Сначала во временный файл: два одновременных запроса не отдадут недописанную картинку.
            var temp = target + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            using (var stream = File.Create(temp))
            {
                data.SaveTo(stream);
            }
            File.Move(temp, target, overwrite: true);
            return target;
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "Не удалось сделать уменьшенную копию {Path}", relativePath);
            return null;
        }
    }

    private static bool IsVector(string path) => Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase);
}
