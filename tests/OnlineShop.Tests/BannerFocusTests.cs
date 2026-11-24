using OnlineShop.Web.Services;
using SkiaSharp;

namespace OnlineShop.Tests;

/// <summary>Баннер первого экрана: поиск товаров на фото и сдвиг фото так, чтобы они были посередине.</summary>
public class BannerFocusTests
{
    // Цветной фон (стена) и «товар» — белая канистра в заданном месте.
    private static byte[] Picture(int width, int height, int objectFrom, int objectTo)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(240, 200, 90));
            using var paint = new SKPaint { Color = new SKColor(248, 248, 246) };
            canvas.DrawRect(objectFrom, height * 0.2f, objectTo - objectFrom, height * 0.7f, paint);
        }
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    [Fact]
    public void Finds_products_on_the_right_side()
    {
        var focus = BannerFocus.Detect(Picture(1800, 600, 1300, 1600));
        Assert.InRange(focus, 66, 85);
    }

    [Fact]
    public void Finds_products_on_the_left_side()
    {
        var focus = BannerFocus.Detect(Picture(1800, 600, 150, 500));
        Assert.InRange(focus, 15, 32);
    }

    [Fact]
    public void Unreadable_file_gives_middle()
    {
        Assert.Equal(50, BannerFocus.Detect([1, 2, 3]));
    }

    [Theory]
    [InlineData(50, 3.0, 50)]   // посередине — сдвига нет
    [InlineData(0, 3.0, 0)]     // у левого края — фото прижато влево
    [InlineData(100, 3.0, 100)] // у правого — вправо
    [InlineData(80, 1.5, 50)]   // фото уже видимой части — сдвигать нечего
    public void Object_position_centers_the_focus(int focus, double ratio, double expected)
    {
        Assert.Equal(expected, BannerFocus.ObjectPositionX(focus, ratio));
    }

    [Fact]
    public void Object_position_for_off_center_focus()
    {
        // Видно 0,6 ширины: точка 70% по центру → левый край кадра на 40% → 40 / 40 = 100%.
        Assert.Equal(100, BannerFocus.ObjectPositionX(70, 3.0));
        // Точка 60% → левый край на 30% → 30 / 40 = 75%.
        Assert.Equal(75, BannerFocus.ObjectPositionX(60, 3.0));
    }
}
