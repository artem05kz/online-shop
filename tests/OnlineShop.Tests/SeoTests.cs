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
}
