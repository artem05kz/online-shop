using SkiaSharp;

namespace OnlineShop.Web.Services;

/// <summary>Картинка после сжатия и её размер в точках.</summary>
public record ShrunkImage(byte[] Content, string Extension, int Width, int Height);

/// <summary>
/// Сжатие фото при загрузке. Снимок с телефона весит 5-10 МБ и имеет 4000 точек по длинной стороне,
/// а сайту хватает 2000. Хранить исходник целиком незачем: место на сервере и резервные копии растут,
/// а покупатель всё равно видит уменьшенные копии (см. <see cref="Thumbnails"/>).
/// </summary>
public static class ImageShrinker
{
    /// <summary>Длинная сторона фото товара: с запасом для большого фото на странице товара.</summary>
    public const int ProductMaxSide = 2000;

    /// <summary>Ширина баннера: 4K, больше экранов не бывает.</summary>
    public const int BannerMaxSide = 3840;

    // Файлы меньше этого и не больше предела по размеру оставляем как есть: пересжатие
    // небольшой картинки почти ничего не даёт, а качество теряется.
    private const int KeepBelowBytes = 700 * 1024;

    /// <summary>
    /// Уменьшает до maxSide по длинной стороне, поворачивает по метке EXIF (фото с телефона часто
    /// хранятся «лёжа» с пометкой «повернуть») и сохраняет в WebP. Метки EXIF при этом пропадают,
    /// вместе с координатами места съёмки. GIF не трогаем: он может быть анимацией.
    /// </summary>
    public static ShrunkImage Shrink(byte[] content, string extension, int maxSide, int quality = 85)
    {
        extension = extension.ToLowerInvariant();
        using var codec = SKCodec.Create(new SKMemoryStream(content));
        if (codec is null)
        {
            return new ShrunkImage(content, extension, 0, 0);
        }

        var origin = codec.EncodedOrigin;
        var rotated = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (width, height) = rotated
            ? (codec.Info.Height, codec.Info.Width)
            : (codec.Info.Width, codec.Info.Height);

        var tooBig = Math.Max(width, height) > maxSide;
        var needsTurn = origin != SKEncodedOrigin.TopLeft && origin != SKEncodedOrigin.Default;
        if (extension == ".gif" || (!tooBig && !needsTurn && content.Length <= KeepBelowBytes))
        {
            return new ShrunkImage(content, extension, width, height);
        }

        using var decoded = SKBitmap.Decode(codec);
        if (decoded is null)
        {
            return new ShrunkImage(content, extension, width, height);
        }
        using var upright = Orient(decoded, origin);

        var scale = Math.Min(1.0, (double)maxSide / Math.Max(upright.Width, upright.Height));
        var (newWidth, newHeight) = (Math.Max(1, (int)Math.Round(upright.Width * scale)), Math.Max(1, (int)Math.Round(upright.Height * scale)));
        using var resized = scale < 1.0
            ? upright.Resize(new SKImageInfo(newWidth, newHeight), new SKSamplingOptions(SKCubicResampler.Mitchell))
            : upright.Copy();
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Webp, quality);
        var result = data.ToArray();

        // Уже хорошо сжатый файл нужного размера без поворота: WebP вышел не меньше, оставляем исходник.
        if (!tooBig && !needsTurn && result.Length >= content.Length)
        {
            return new ShrunkImage(content, extension, width, height);
        }
        return new ShrunkImage(result, ".webp", newWidth, newHeight);
    }

    /// <summary>Только размер в точках с учётом поворота, без расшифровки всей картинки.</summary>
    public static (int Width, int Height)? Size(string path)
    {
        using var codec = SKCodec.Create(path);
        if (codec is null)
        {
            return null;
        }
        return codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom
            ? (codec.Info.Height, codec.Info.Width)
            : (codec.Info.Width, codec.Info.Height);
    }

    private static SKBitmap Orient(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return source.Copy();
        }
        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop
            or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var (w, h) = swap ? (source.Height, source.Width) : (source.Width, source.Height);
        var target = new SKBitmap(new SKImageInfo(w, h, source.ColorType, source.AlphaType));
        using var canvas = new SKCanvas(target);
        // Матрица переводит точку исходника в точку повёрнутой картинки по таблице меток EXIF 1-8.
        var matrix = origin switch
        {
            SKEncodedOrigin.TopRight => new SKMatrix(-1, 0, w, 0, 1, 0, 0, 0, 1),
            SKEncodedOrigin.BottomRight => new SKMatrix(-1, 0, w, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.BottomLeft => new SKMatrix(1, 0, 0, 0, -1, h, 0, 0, 1),
            SKEncodedOrigin.LeftTop => new SKMatrix(0, 1, 0, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightTop => new SKMatrix(0, -1, w, 1, 0, 0, 0, 0, 1),
            SKEncodedOrigin.RightBottom => new SKMatrix(0, -1, w, -1, 0, h, 0, 0, 1),
            SKEncodedOrigin.LeftBottom => new SKMatrix(0, 1, 0, -1, 0, h, 0, 0, 1),
            _ => SKMatrix.Identity,
        };
        canvas.SetMatrix(matrix);
        canvas.DrawBitmap(source, 0, 0);
        return target;
    }
}
