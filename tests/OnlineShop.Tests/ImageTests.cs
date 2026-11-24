using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using SkiaSharp;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Сжатие фото при загрузке и баннеры одной пропорции на всех экранах.</summary>
public class ImageTests(TempShopFactory factory) : IClassFixture<TempShopFactory>, IDisposable
{
    private readonly string _files = Directory.CreateTempSubdirectory("online-shop-images-").FullName;

    public void Dispose() => Directory.Delete(_files, recursive: true);

    /// <summary>Переход цвета с лёгким шумом: весит примерно как настоящее фото того же размера.</summary>
    private static byte[] Photo(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg, int quality = 95)
    {
        using var bitmap = new SKBitmap(width, height);
        var random = new Random(1);
        var pixels = new SKColor[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            int x = i % width, y = i / width, noise = random.Next(-12, 13);
            pixels[i] = new SKColor(
                (byte)Math.Clamp(x * 255 / width + noise, 0, 255),
                (byte)Math.Clamp(y * 255 / height + noise, 0, 255),
                (byte)Math.Clamp(128 + noise, 0, 255));
        }
        bitmap.Pixels = pixels;
        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(format, quality).ToArray();
    }

    /// <summary>JPEG «лёжа» с меткой EXIF «повернуть на 90° по часовой»: так снимают телефоны.</summary>
    private static byte[] SidewaysJpeg()
    {
        // Левая половина красная, правая синяя. После поворота по часовой красное окажется сверху.
        using var bitmap = new SKBitmap(300, 100);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.DrawRect(0, 0, 150, 100, new SKPaint { Color = SKColors.Red });
            canvas.DrawRect(150, 0, 150, 100, new SKPaint { Color = SKColors.Blue });
        }
        using var image = SKImage.FromBitmap(bitmap);
        var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 95).ToArray();
        // Сегмент APP1 с одной записью EXIF: Orientation (0x0112) = 6.
        byte[] exif =
        [
            0xFF, 0xE1, 0x00, 0x22, (byte)'E', (byte)'x', (byte)'i', (byte)'f', 0, 0,
            (byte)'I', (byte)'I', 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
            0x01, 0x00, 0x12, 0x01, 0x03, 0x00, 0x01, 0x00, 0x00, 0x00, 0x06, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00,
        ];
        return [.. jpeg[..2], .. exif, .. jpeg[2..]];
    }

    [Fact]
    public void Big_photo_is_reduced_to_limit_and_stored_as_webp()
    {
        var original = Photo(4000, 3000);

        var shrunk = ImageShrinker.Shrink(original, ".jpg", ImageShrinker.ProductMaxSide);

        Assert.Equal((".webp", 2000, 1500), (shrunk.Extension, shrunk.Width, shrunk.Height));
        Assert.True(shrunk.Content.Length < original.Length / 3, $"{original.Length} → {shrunk.Content.Length}");
        using var decoded = SKBitmap.Decode(shrunk.Content);
        Assert.Equal((2000, 1500), (decoded.Width, decoded.Height));
    }

    [Fact]
    public void Small_light_image_and_gif_are_kept_as_is()
    {
        var small = Photo(400, 300, SKEncodedImageFormat.Png);
        var kept = ImageShrinker.Shrink(small, ".png", ImageShrinker.ProductMaxSide);
        Assert.Same(small, kept.Content);
        Assert.Equal((".png", 400, 300), (kept.Extension, kept.Width, kept.Height));

        var gif = Photo(3000, 3000, SKEncodedImageFormat.Png);
        Assert.Same(gif, ImageShrinker.Shrink(gif, ".gif", 100).Content);
    }

    [Fact]
    public void Phone_photo_is_turned_upright_by_exif()
    {
        var shrunk = ImageShrinker.Shrink(SidewaysJpeg(), ".jpg", ImageShrinker.ProductMaxSide);

        Assert.Equal((100, 300), (shrunk.Width, shrunk.Height));
        using var decoded = SKBitmap.Decode(shrunk.Content);
        Assert.True(decoded.GetPixel(50, 20).Red > 200, "Сверху должна быть красная половина.");
        Assert.True(decoded.GetPixel(50, 280).Blue > 200, "Снизу должна быть синяя половина.");
    }

    [Fact]
    public void Banner_shape_warnings_name_what_will_be_cut()
    {
        Assert.Empty(BannerShape.Check(3840, 1280));
        Assert.Empty(BannerShape.Check(1920, 640));
        // Широкое фото — нормально (видна часть, сдвигается к товарам), высокое обрезается сверху и снизу.
        Assert.Empty(BannerShape.Check(4000, 1000));
        Assert.Contains("сверху и снизу обрежется примерно по 17%", Assert.Single(BannerShape.Check(1920, 1600)));
        Assert.Contains("нечётким", Assert.Single(BannerShape.Check(1200, 400)));
    }

    [Fact]
    public async Task Banner_srcset_offers_copies_narrower_than_the_original()
    {
        var media = factory.Services.GetRequiredService<MediaStorage>();
        var wide = await media.SaveAsync(Photo(3840, 1280, quality: 60), "banners", "wide.jpg");
        var narrow = await media.SaveAsync(Photo(1500, 500, quality: 60), "banners", "narrow.jpg");

        Assert.Equal(
            $"/media/thumb/960/{wide} 960w, /media/thumb/1280/{wide} 1280w, /media/thumb/1920/{wide} 1920w, " +
            $"/media/thumb/2560/{wide} 2560w, /media/{wide} 3840w",
            BannerShape.SrcSet(media, wide));
        Assert.Equal($"/media/thumb/960/{narrow} 960w, /media/thumb/1280/{narrow} 1280w, /media/{narrow} 1500w",
            BannerShape.SrcSet(media, narrow));

        // Копия нужной ширины делается по запросу браузера.
        var response = await factory.Browser().GetAsync($"/media/thumb/1920/{wide}");
        using var copy = SKBitmap.Decode(await response.Content.ReadAsByteArrayAsync());
        Assert.Equal((1920, 640), (copy.Width, copy.Height));
    }

    [Fact]
    public async Task Uploaded_banner_is_compressed_and_manager_is_warned_about_shape()
    {
        var client = await Manager();
        var token = await TokenFrom(client, "/admin/banners/add");
        Assert.Contains("3840 × 1280", await Page(client, "/admin/banners/add"));

        var big = Path.Combine(_files, "big.png");
        await File.WriteAllBytesAsync(big, Photo(4800, 1600, SKEncodedImageFormat.Png));
        var good = await Upload(client, token, big, "Ровный баннер");
        Assert.EndsWith(".webp", good.ImagePath);
        var media = factory.Services.GetRequiredService<MediaStorage>();
        Assert.Equal((3840, 1280), ImageShrinker.Size(media.FullPath(good.ImagePath)!)!.Value);
        Assert.DoesNotContain("обрежется", await Page(client, "/admin/banners"));

        var tall = Path.Combine(_files, "tall.jpg");
        await File.WriteAllBytesAsync(tall, Photo(1920, 1440));
        var cropped = await Upload(client, token, tall, "Высокий баннер");
        Assert.Contains("сверху и снизу обрежется", await Page(client, "/admin/banners"));
        // На странице баннера то же предупреждение остаётся под превью.
        Assert.Contains("Размер 1920 × 1440.", await Page(client, $"/admin/banners/{cropped.Id}"));
    }

    [Fact]
    public async Task Uploaded_product_photo_is_reduced()
    {
        var client = await Manager();
        var (productId, variantId, categoryId) = Seed();
        var photo = Path.Combine(_files, "phone.jpg");
        await File.WriteAllBytesAsync(photo, Photo(4000, 3000));
        var token = await TokenFrom(client, $"/admin/products/{productId}");

        await PostFiles(client, $"/admin/products/{productId}", token, [("NewImages", photo, "phone.jpg")],
            ("Input.Name", "Товар с фото"), ("Input.CategoryId", categoryId.ToString()), ("Input.IsActive", "true"),
            ("Variants[0].Id", variantId.ToString()), ("Variants[0].Article", $"IMG{productId}"), ("Variants[0].Price", "1000"),
            ("Variants[0].StockQty", "3"), ("Variants[0].IsActive", "true"));

        using var scope = factory.Services.CreateScope();
        var image = scope.ServiceProvider.GetRequiredService<ShopDbContext>().ProductImages.Single(i => i.ProductId == productId);
        var media = factory.Services.GetRequiredService<MediaStorage>();
        Assert.Equal((2000, 1500), ImageShrinker.Size(media.FullPath(image.ImagePath)!)!.Value);
    }

    private async Task<Banner> Upload(HttpClient client, string token, string path, string title)
    {
        await PostFiles(client, "/admin/banners/add", token, [("Image", path, Path.GetFileName(path))],
            ("Input.Title", title), ("Input.IsActive", "true"));
        using var scope = factory.Services.CreateScope();
        return scope.ServiceProvider.GetRequiredService<ShopDbContext>().Banners.Single(b => b.Title == title);
    }

    private (int ProductId, int VariantId, int CategoryId) Seed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var product = new Product
        {
            Category = new Category { Brand = new Brand { Name = $"Бренд {Guid.NewGuid():N}"[..14] }, Name = "Категория" },
            Name = $"Товар {Guid.NewGuid():N}"[..14],
            Variants = [new ProductVariant { Article = "tmp" + Guid.NewGuid().ToString("N")[..6], Price = 1000m, StockQty = 3 }],
        };
        db.Products.Add(product);
        db.SaveChanges();
        product.Variants[0].Article = $"IMG{product.Id}";
        db.SaveChanges();
        return (product.Id, product.Variants[0].Id, product.CategoryId);
    }

    private async Task<HttpClient> Manager()
    {
        var login = $"менеджер-фото{Guid.NewGuid():N}"[..20];
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = login };
            Assert.True((await users.CreateAsync(user, "панель-управления")).Succeeded);
            await users.AddToRoleAsync(user, RoleNames.Manager);
        }
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/login");
        await Post(client, "/accounts/login", token, ("Input.UserName", login), ("Input.Password", "панель-управления"));
        return client;
    }

    private static async Task<HttpResponseMessage> PostFiles(HttpClient client, string url, string token,
        IEnumerable<(string Field, string Path, string FileName)> files, params (string, string)[] fields)
    {
        var form = new MultipartFormDataContent { { new StringContent(token), "__RequestVerificationToken" } };
        foreach (var (name, value) in fields)
        {
            form.Add(new StringContent(value), name);
        }
        foreach (var (field, path, fileName) in files)
        {
            form.Add(new ByteArrayContent(await File.ReadAllBytesAsync(path)), field, fileName);
        }
        return await client.PostAsync(url, form);
    }
}
