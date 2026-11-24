using System.Globalization;
using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Web.Services;

/// <summary>
/// Размеры фото для баннера первого экрана. Рекомендуем 3:1: фото видно частично (около 1,8:1), а
/// запас по ширине позволяет сдвинуть его к товарам (BannerFocus). Надписи на фото не нужны: текст пишется в панели.
/// </summary>
public static class BannerShape
{
    public const int RatioWidth = 3;
    public const int RatioHeight = 1;
    public const int RecommendedWidth = 3840;
    public const int RecommendedHeight = 1280;
    public const int MinWidth = 1920;

    /// <summary>Уменьшенные копии по ширине. Браузер сам выбирает подходящую экрану (srcset).</summary>
    public static readonly IReadOnlyList<int> Widths = [960, 1280, 1920, 2560];

    // До 3% отклонения от пропорции глаз не замечает, обрезка таких краёв незаметна.
    private const double Tolerance = 0.03;

    /// <summary>Что сказать менеджеру о загруженной картинке. Пусто, если всё хорошо.</summary>
    public static IEnumerable<string> Check(int width, int height)
    {
        if (width <= 0 || height <= 0)
        {
            yield break;
        }
        // На первом экране видна часть фото пропорции BannerFocus.VisibleRatio (около 1,8:1), остальное уходит
        // за края слева и справа, и это нормально: фото сдвигается к товарам. Плохо только фото уже этой части:
        // тогда обрезаются верх и низ.
        var ratio = (double)width / height;
        if (ratio < BannerFocus.VisibleRatio * (1 - Tolerance))
        {
            var cut = Percent((height - width / BannerFocus.VisibleRatio) / height / 2);
            yield return $"Картинка слишком высокая: сверху и снизу обрежется примерно по {cut}%. " +
                $"Лучше сделать её размером {RecommendedWidth} × {RecommendedHeight}.";
        }
        if (width < MinWidth)
        {
            yield return $"Ширина картинки {width} точек: на больших экранах баннер будет нечётким. " +
                $"Лучше {RecommendedWidth} × {RecommendedHeight}, не меньше {MinWidth} × {MinWidth / RatioWidth * RatioHeight}.";
        }
    }

    /// <summary>
    /// srcset для картинки баннера: копии уже исходника и сам исходник с его настоящей шириной.
    /// Копии шире исходника не предлагаем, увеличение не добавило бы чёткости.
    /// </summary>
    public static string SrcSet(MediaStorage media, string imagePath)
    {
        var size = media.FullPath(imagePath) is { } full && File.Exists(full) ? ImageShrinker.Size(full) : null;
        if (size is not { Width: > 0 } known)
        {
            return "";
        }
        var parts = Widths.Where(w => w < known.Width)
            .Select(w => $"{Media.Thumb(imagePath, w)} {w}w")
            .Append($"{Media.Url(imagePath)} {known.Width}w");
        return string.Join(", ", parts);
    }

    /// <summary>Какой ширины баннер на странице: вся ширина экрана, но не больше колонки сайта.</summary>
    public const string Sizes = "(min-width: 1280px) 1240px, 100vw";

    /// <summary>Ширина фото баннера на первом экране: левые 8 колонок из 12 (около двух третей).</summary>
    public const string HeroSizes = "(min-width: 1280px) 820px, 70vw";

    private static string Percent(double share) => Math.Round(share * 100).ToString(CultureInfo.InvariantCulture);
}
