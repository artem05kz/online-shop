using System.Globalization;
using System.Xml.Linq;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;

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
