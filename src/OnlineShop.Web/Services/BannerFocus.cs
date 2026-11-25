using SkiaSharp;

namespace OnlineShop.Web.Services;

/// <summary>
/// Где на фото баннера главное — товары. На первом экране фото занимает левую часть (8 колонок из 12)
/// и обрезается по бокам; чтобы в кадр попали товары, а не стена, фото сдвигается к их середине.
///
/// Как ищем: упаковка Ifoam и Grass почти вся белая, а фон на рекламных фото цветной (стены, растения,
/// машина, пена мелкая). Берём крупные почти белые места (яркие и без цвета), лёгкий перевес к середине
/// кадра и считаем их «центр тяжести» по ширине. Если белого почти нет, ищем самую резкую часть
/// (товары резкие, фон размыт). Проверено на четырёх баннерах прошлого сайта. Это догадка, не распознавание:
/// менеджер может поправить точку щелчком по превью в панели.
/// </summary>
public static class BannerFocus
{
    /// <summary>
    /// Пропорция видимой части фото на первом экране (ширина 8 колонок с полем ÷ высота) на компьютере.
    /// По ней же считается сдвиг в <see cref="ObjectPositionX"/>.
    /// </summary>
    public const double VisibleRatio = 1.8;

    private const int SampleWidth = 160;
    private const int WhiteBrightness = 225; // самый яркий из R, G, B не ниже
    private const int WhiteSpread = 15;      // разница между R, G, B не больше: без цвета
    private const double CenterPull = 0.3;   // ширина «колокола» перевеса к середине, доля кадра
    private const double MinWhiteShare = 0.005;

    /// <summary>Середина главного на фото, в процентах ширины (0–100). 50, если картинку не прочитать.</summary>
    public static int Detect(byte[] content)
    {
        try
        {
            using var bitmap = SKBitmap.Decode(content);
            return bitmap is null ? 50 : Detect(bitmap);
        }
        catch
        {
            return 50;
        }
    }

    public static int Detect(SKBitmap source)
    {
        if (source.Width < 4 || source.Height < 4)
        {
            return 50;
        }
        var w = Math.Min(SampleWidth, source.Width);
        var h = Math.Max(2, (int)Math.Round((double)source.Height * w / source.Width));
        using var small = source.Resize(new SKImageInfo(w, h, SKColorType.Rgba8888), new SKSamplingOptions(SKFilterMode.Linear));
        if (small is null)
        {
            return 50;
        }

        var white = new double[w];
        var sharp = new double[w];
        double whiteCount = 0;
        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                var c = small.GetPixel(x, y);
                var max = Math.Max(c.Red, Math.Max(c.Green, c.Blue));
                var min = Math.Min(c.Red, Math.Min(c.Green, c.Blue));
                if (max >= WhiteBrightness && max - min <= WhiteSpread)
                {
                    white[x]++;
                    whiteCount++;
                }
                if (x + 1 < w && y + 1 < h)
                {
                    var right = small.GetPixel(x + 1, y);
                    var below = small.GetPixel(x, y + 1);
                    sharp[x] += Math.Abs(Luma(c) - Luma(right)) + Math.Abs(Luma(c) - Luma(below));
                }
            }
        }
        var weights = whiteCount >= w * h * MinWhiteShare ? white : sharp;

        double mass = 0, moment = 0;
        for (var x = 0; x < w; x++)
        {
            var d = (x + 0.5) / w - 0.5;
            var weight = weights[x] * Math.Exp(-d * d / (2 * CenterPull * CenterPull));
            mass += weight;
            moment += weight * (x + 0.5);
        }
        if (mass <= 0)
        {
            return 50;
        }
        return (int)Math.Round(Math.Clamp(moment / mass / w, 0, 1) * 100);
    }

    private static double Luma(SKColor c) => 0.299 * c.Red + 0.587 * c.Green + 0.114 * c.Blue;

    /// <summary>
    /// Значение object-position по ширине (в процентах), при котором точка <paramref name="focusX"/> стоит
    /// посередине видимой части. Ширина фото к высоте — <paramref name="imageRatio"/>.
    /// </summary>
    public static double ObjectPositionX(int focusX, double imageRatio)
    {
        if (imageRatio <= VisibleRatio)
        {
            return 50;
        }
        var visible = VisibleRatio / imageRatio; // какая доля ширины фото видна
        var left = focusX / 100.0 - visible / 2;
        return Math.Round(Math.Clamp(left / (1 - visible), 0, 1) * 100, 1);
    }
}
