using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Controllers;

/// <summary>
/// Подсказки поисковикам: какие страницы есть на сайте (sitemap.xml) и куда ходить не надо (robots.txt).
/// Адрес сайта берётся из запроса, поэтому ссылки верны и на сервере за Caddy, и на своём компьютере.
/// </summary>
public class SeoController(ShopDbContext db) : Controller
{
    private static readonly XNamespace Ns = "http://www.sitemaps.org/schemas/sitemap/0.9";

    [HttpGet("/sitemap.xml")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> Sitemap()
    {
        var categories = await db.Categories
            .Where(c => c.IsActive && c.Brand.IsActive)
            .Select(c => new { BrandSlug = c.Brand.Slug, c.Slug })
            .ToListAsync();
        var products = await db.Products
            .Where(p => p.IsActive && p.Category.IsActive && p.Category.Brand.IsActive)
            .OrderBy(p => p.Id)
            .Select(p => new { p.Slug, p.UpdatedAt })
            .ToListAsync();

        XElement Entry(string? url, DateTime? changed = null) => new(Ns + "url",
            new XElement(Ns + "loc", url),
            changed is { } date ? new XElement(Ns + "lastmod", date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)) : null);

        string? Absolute(string action, string controller, object? values = null) =>
            Url.Action(action, controller, values, Request.Scheme, Request.Host.Value);

        var urls = new List<XElement>
        {
            Entry(Absolute("Home", "Catalog")),
            Entry(Absolute("Index", "Stores")),
        };
        urls.AddRange(categories.Select(c => Entry(Absolute("Category", "Catalog", new { brandSlug = c.BrandSlug, categorySlug = c.Slug }))));
        urls.AddRange(products.Select(p => Entry(Absolute("Product", "Catalog", new { slug = p.Slug }), p.UpdatedAt)));

        var xml = new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement(Ns + "urlset", urls));
        return Content(xml.Declaration + "\n" + xml.Root, "application/xml; charset=utf-8");
    }

    private static readonly XNamespace G = "http://base.google.com/ns/1.0";

    /// <summary>
    /// Товарная лента для Google Merchant Center: по ней товары показываются во вкладке «Покупки»
    /// с фото и ценой. Каждая фасовка отдельным товаром, фасовки одного товара связаны item_group_id.
    /// </summary>
    [HttpGet("/feed/google.xml")]
    [ResponseCache(Duration = 3600)]
    public async Task<IActionResult> GoogleFeed()
    {
        var site = $"{Request.Scheme}://{Request.Host}";
        var products = await db.Products
            .Where(p => p.IsActive && p.Category.IsActive && p.Category.Brand.IsActive)
            .ForListing()
            .OrderBy(p => p.Id)
            .AsNoTracking()
            .ToListAsync();

        var items = new List<XElement>();
        foreach (var product in products)
        {
            var link = site + Url.Action("Product", "Catalog", new { slug = product.Slug });
            var image = product.PrimaryImage is { } primary ? site + Media.Thumb(primary.ImagePath, Media.Large) : null;
            foreach (var variant in product.ActiveVariants.Where(v => v.Price is not null))
            {
                var title = variant.PackLabel.Length > 0 ? $"{product.Name}, {variant.PackLabel}" : product.Name;
                items.Add(new XElement("item",
                    new XElement(G + "id", variant.Article),
                    new XElement(G + "item_group_id", product.Id),
                    new XElement(G + "title", title.Length > 150 ? title[..150] : title),
                    new XElement(G + "description", product.Description.Length > 0 ? product.Description : title),
                    new XElement(G + "link", link),
                    image is null ? null : new XElement(G + "image_link", image),
                    new XElement(G + "availability", variant.InStock ? "in_stock" : "out_of_stock"),
                    new XElement(G + "price", variant.Price!.Value.ToString("0.00", CultureInfo.InvariantCulture) + " KZT"),
                    new XElement(G + "brand", product.Category.Brand.Name),
                    new XElement(G + "condition", "new")));
            }
        }

        var rss = new XElement("rss", new XAttribute("version", "2.0"), new XAttribute(XNamespace.Xmlns + "g", G),
            new XElement("channel",
                new XElement("title", "GRASS Актау"),
                new XElement("link", site),
                new XElement("description", "Автохимия и бытовая химия GRASS и IFOAM в Актау"),
                items));
        return Content(new XDeclaration("1.0", "utf-8", null) + "\n" + rss, "application/xml; charset=utf-8");
    }

    [HttpGet("/robots.txt")]
    public IActionResult Robots() => Content(
        "User-agent: *\n" +
        // Панель, кабинет, корзина и поиск поисковикам не нужны: там нет ничего для выдачи.
        "Disallow: /admin\n" +
        "Disallow: /accounts\n" +
        "Disallow: /orders\n" +
        "Disallow: /favorites\n" +
        "Disallow: /search\n" +
        $"Sitemap: {Request.Scheme}://{Request.Host}/sitemap.xml\n",
        "text/plain; charset=utf-8");
}
