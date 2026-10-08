using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

/// <summary>
/// Описание товара для поисковиков в формате schema.org (JSON-LD). По нему Google и Яндекс могут
/// показать в выдаче карточку с фото, ценой, наличием и звёздами отзывов. Покупатель его не видит.
/// </summary>
public static class ProductMarkup
{
    // Кириллица как есть, а < > & и кавычки кодировщик всё равно экранирует: текст товара
    // не закроет тег script раньше времени.
    private static readonly JsonSerializerOptions Json = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    /// <summary>Готовый тег. Пишем его целиком сами: Razor закодировал бы «+» в type="application/ld+json".</summary>
    public static string ScriptTag(Product product, string productUrl, string siteUrl, IReadOnlyList<Review> reviews) =>
        $"<script type=\"application/ld+json\">{Build(product, productUrl, siteUrl, reviews)}</script>";

    public static string Build(Product product, string productUrl, string siteUrl, IReadOnlyList<Review> reviews)
    {
        var images = product.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.SortOrder)
            .Select(i => siteUrl + Media.Thumb(i.ImagePath, Media.Large)).ToList();
        var offers = product.ActiveVariants.Where(v => v.Price is not null).Select(v => new Dictionary<string, object?>
        {
            ["@type"] = "Offer",
            ["sku"] = v.Article,
            ["name"] = v.PackLabel.Length > 0 ? $"{product.Name}, {v.PackLabel}" : product.Name,
            ["price"] = v.Price!.Value.ToString("0.00", CultureInfo.InvariantCulture),
            ["priceCurrency"] = "KZT",
            ["availability"] = v.InStock ? "https://schema.org/InStock" : "https://schema.org/OutOfStock",
            ["itemCondition"] = "https://schema.org/NewCondition",
            ["url"] = productUrl,
        }).ToList();

        var markup = new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org",
            ["@type"] = "Product",
            ["name"] = product.Name,
            ["url"] = productUrl,
            ["image"] = images,
            ["description"] = product.Description.Length > 0 ? product.Description : product.Name,
            ["sku"] = product.ActiveVariants.FirstOrDefault()?.Article,
            ["brand"] = new Dictionary<string, object?> { ["@type"] = "Brand", ["name"] = product.Category.Brand.Name },
            ["category"] = product.Category.Name,
        };
        if (offers.Count > 0)
        {
            markup["offers"] = offers;
        }
        if (reviews.Count > 0)
        {
            markup["aggregateRating"] = new Dictionary<string, object?>
            {
                ["@type"] = "AggregateRating",
                ["ratingValue"] = reviews.Average(r => r.Rating).ToString("0.0", CultureInfo.InvariantCulture),
                ["reviewCount"] = reviews.Count,
            };
            markup["review"] = reviews.Take(5).Select(r => new Dictionary<string, object?>
            {
                ["@type"] = "Review",
                ["reviewRating"] = new Dictionary<string, object?> { ["@type"] = "Rating", ["ratingValue"] = r.Rating },
                ["author"] = new Dictionary<string, object?> { ["@type"] = "Person", ["name"] = r.User.FirstName.Length > 0 ? r.User.FirstName : "Покупатель" },
                ["datePublished"] = Format.ToLocal(r.CreatedAt).ToString("yyyy-MM-dd"),
                ["reviewBody"] = r.Text,
            }).ToList();
        }
        return JsonSerializer.Serialize(markup, Json);
    }
}
