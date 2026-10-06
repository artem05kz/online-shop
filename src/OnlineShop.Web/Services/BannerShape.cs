using System.Globalization;
using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Web.Services;

/// <summary>
/// Форма баннера. Пропорция одна на всех экранах (3:1), меняется только масштаб: на телефоне
/// это та же картинка, только меньше. Поэтому надпись на баннере не съезжает и не обрезается,
/// где бы её ни поставили. Та же пропорция задана в style.css (.banner-carousel).
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
        var ratio = (double)width / height;
        var target = (double)RatioWidth / RatioHeight;
        if (ratio > target * (1 + Tolerance))
        {
            var cut = Percent((width - height * target) / width / 2);
            yield return $"Картинка шире, чем {RatioWidth}:{RatioHeight}: слева и справа обрежется примерно по {cut}%. " +
                $"Лучше сделать её размером {RecommendedWidth} × {RecommendedHeight}.";
        }
        else if (ratio < target * (1 - Tolerance))
        {
            var cut = Percent((height - width / target) / height / 2);
            yield return $"Картинка выше, чем {RatioWidth}:{RatioHeight}: сверху и снизу обрежется примерно по {cut}%. " +
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

    private static string Percent(double share) => Math.Round(share * 100).ToString(CultureInfo.InvariantCulture);
}
