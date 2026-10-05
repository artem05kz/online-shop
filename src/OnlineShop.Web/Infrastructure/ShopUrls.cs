using Microsoft.AspNetCore.Mvc;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Адреса страниц магазина, на которые ссылаются шапка, подвал и вкладки кабинета.
/// Панель управления переносится на этапе 4, до этого её адрес никуда не ведёт.
/// </summary>
public static class ShopUrls
{
    public const string Login = "/accounts/login";
    public const string Logout = "/accounts/logout";
    public const string Register = "/accounts/register";
    public const string Profile = "/accounts/profile";
    public const string AddressBook = "/accounts/profile/addresses";
    public const string OrderHistory = "/orders/my";
    public const string Admin = "/admin";

    public static string Category(this IUrlHelper url, Category category) =>
        url.Action("Category", "Catalog", new { brandSlug = category.Brand.Slug, categorySlug = category.Slug })!;

    public static string Product(this IUrlHelper url, Product product) =>
        url.Action("Product", "Catalog", new { slug = product.Slug })!;
}
