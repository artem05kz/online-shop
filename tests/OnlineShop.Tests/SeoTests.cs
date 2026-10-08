using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Tests;

public class SeoTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    [Fact]
    public async Task Sitemap_lists_visible_pages_only_with_absolute_escaped_urls()
    {
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
            var category = new Category { Brand = new Brand { Name = "Grass" }, Name = "Автошампуни" };
            db.Products.Add(new Product { Category = category, Name = "Видимый шампунь" });
            db.Products.Add(new Product { Category = category, Name = "Скрытый шампунь", IsActive = false });
            db.Categories.Add(new Category { Brand = category.Brand, Name = "Скрытая категория", IsActive = false });
            db.SaveChanges();
        }

        var response = await factory.Browser().GetAsync("/sitemap.xml");
        Assert.Equal("application/xml", response.Content.Headers.ContentType!.MediaType);
        XNamespace ns = "http://www.sitemaps.org/schemas/sitemap/0.9";
        var urls = XDocument.Parse(await response.Content.ReadAsStringAsync())
            .Descendants(ns + "loc").Select(l => Uri.UnescapeDataString(l.Value)).ToList();

        Assert.Contains("http://localhost/", urls);
        Assert.Contains("http://localhost/catalog/grass/автошампуни", urls);
        Assert.Contains("http://localhost/product/видимый-шампунь", urls);
        Assert.DoesNotContain(urls, u => u.Contains("скрыт"));
        // В самом файле адреса закодированы: так требует формат sitemap.
        Assert.DoesNotContain("автошампуни", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Robots_hides_private_sections_and_points_to_sitemap()
    {
        var robots = await factory.Browser().GetStringAsync("/robots.txt");
        Assert.Contains("Disallow: /admin", robots);
        Assert.Contains("Disallow: /accounts", robots);
        Assert.Contains("Sitemap: http://localhost/sitemap.xml", robots);
    }

    private string SeedRichProduct()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        var existing = db.Products.FirstOrDefault(p => p.Name == "Полироль </script> для шин");
        if (existing is not null)
        {
            return existing.Slug;
        }
        var user = new ApplicationUser { UserName = "автор-отзыва", FirstName = "Ержан" };
        var product = new Product
        {
            Category = new Category { Brand = new Brand { Name = "Ifoam" }, Name = "Полироли" },
            Name = "Полироль </script> для шин", Description = "Чернит & блестит",
            Variants =
            [
                new ProductVariant { Article = "SEO1", PackLabel = "1 л", Price = 1890m, StockQty = 3 },
                new ProductVariant { Article = "SEO5", PackLabel = "5 л", Price = 7900m, StockQty = 0 },
            ],
        };
        db.Products.Add(product);
        db.Users.Add(user);
        db.SaveChanges();
        db.Reviews.Add(new Review { ProductId = product.Id, UserId = user.Id, Rating = 5, Text = "Отлично", Status = ReviewStatus.Approved, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow });
        db.SaveChanges();
        return product.Slug;
    }

    [Fact]
    public async Task Product_page_has_schema_org_markup_with_prices_stock_and_rating()
    {
        var slug = SeedRichProduct();
        var html = await factory.Browser().GetStringAsync($"/product/{Uri.EscapeDataString(slug)}");
        var json = Regex.Match(html, @"<script type=""application/ld\+json"">(.*?)</script>", RegexOptions.Singleline).Groups[1].Value;
        // Название с </script> не закрыло тег раньше времени: JSON целиком разбирается.
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("Product", root.GetProperty("@type").GetString());
        Assert.Equal("Полироль </script> для шин", root.GetProperty("name").GetString());
        Assert.Equal("Ifoam", root.GetProperty("brand").GetProperty("name").GetString());
        var offers = root.GetProperty("offers").EnumerateArray().ToList();
        Assert.Equal(("1890.00", "KZT", "https://schema.org/InStock"),
            (offers[0].GetProperty("price").GetString(), offers[0].GetProperty("priceCurrency").GetString(), offers[0].GetProperty("availability").GetString()));
        Assert.Equal("https://schema.org/OutOfStock", offers[1].GetProperty("availability").GetString());
        Assert.Equal("5.0", root.GetProperty("aggregateRating").GetProperty("ratingValue").GetString());
        Assert.Contains("<link rel=\"canonical\" href=\"http://localhost/product/", html);
        Assert.Contains("<meta property=\"og:title\"", html);
    }

    [Fact]
    public async Task Google_feed_lists_each_pack_with_price_and_availability()
    {
        SeedRichProduct();
        var xml = XDocument.Parse(await factory.Browser().GetStringAsync("/feed/google.xml"));
        XNamespace g = "http://base.google.com/ns/1.0";
        var items = xml.Descendants("item").ToList();
        var first = items.Single(i => i.Element(g + "id")!.Value == "SEO1");
        Assert.Equal("1890.00 KZT", first.Element(g + "price")!.Value);
        Assert.Equal("in_stock", first.Element(g + "availability")!.Value);
        Assert.Equal("Полироль </script> для шин, 1 л", first.Element(g + "title")!.Value);
        Assert.Equal("out_of_stock", items.Single(i => i.Element(g + "id")!.Value == "SEO5").Element(g + "availability")!.Value);
        Assert.Equal(first.Element(g + "item_group_id")!.Value, items.Single(i => i.Element(g + "id")!.Value == "SEO5").Element(g + "item_group_id")!.Value);
    }
}
