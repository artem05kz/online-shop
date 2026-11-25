using System.Globalization;
using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Web.Services;

/// <summary>
/// Размеры баннера первого экрана (25.11.2026): баннер — готовая картинка во весь первый экран, надписи на ней.
/// Первый экран на компьютере 1200 × 440, картинку просим вдвое крупнее (2400 × 880), чтобы была чёткой.
/// Картинка заполняет блок целиком: если пропорция другая, лишнее обрезается по краям.
/// </summary>
public static class BannerShape
{
    public const int RecommendedWidth = 2400;
    public const int RecommendedHeight = 880;
    public const int MinWidth = 1200;
    public const double Ratio = (double)RecommendedWidth / RecommendedHeight;

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
        if (ratio < Ratio * (1 - Tolerance))
        {
            var cut = Percent((height - width / Ratio) / height / 2);
            yield return $"Картинка выше, чем первый экран: сверху и снизу обрежется примерно по {cut}%. " +
                $"Лучше сделать её размером {RecommendedWidth} × {RecommendedHeight}.";
        }
        else if (ratio > Ratio * (1 + Tolerance))
        {
            var cut = Percent((width - height * Ratio) / width / 2);
            yield return $"Картинка шире, чем первый экран: слева и справа обрежется примерно по {cut}%. " +
                $"Лучше сделать её размером {RecommendedWidth} × {RecommendedHeight}.";
        }
        if (width < MinWidth)
        {
            yield return $"Ширина картинки {width} точек: на компьютере баннер будет нечётким. " +
                $"Лучше {RecommendedWidth} × {RecommendedHeight}, не меньше {MinWidth} × {MinWidth * RecommendedHeight / RecommendedWidth}.";
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

    /// <summary>Ширина баннера на первом экране: вся колонка сайта (1200 точек на компьютере).</summary>
    public const string HeroSizes = "(min-width: 1280px) 1200px, 100vw";

    private static string Percent(double share) => Math.Round(share * 100).ToString(CultureInfo.InvariantCulture);
}
