using OnlineShop.Web.Models;

namespace OnlineShop.Web.Data;

/// <summary>
/// Порядок вывода по умолчанию. В Django он задавался в Meta.ordering моделей,
/// в EF Core такого нет, поэтому запросы вызывают эти методы явно.
/// </summary>
public static class QueryExtensions
{
    public static IOrderedQueryable<Brand> Ordered(this IQueryable<Brand> q) =>
        q.OrderBy(b => b.SortOrder).ThenBy(b => b.Name);

    public static IOrderedQueryable<Category> Ordered(this IQueryable<Category> q) =>
        q.OrderBy(c => c.SortOrder).ThenBy(c => c.Name);

    public static IOrderedQueryable<Product> Ordered(this IQueryable<Product> q) =>
        q.OrderBy(p => p.Name);

    public static IOrderedQueryable<ProductVariant> Ordered(this IQueryable<ProductVariant> q) =>
        q.OrderBy(v => v.VolumeValue).ThenBy(v => v.Id);

    public static IOrderedQueryable<ProductImage> Ordered(this IQueryable<ProductImage> q) =>
        q.OrderBy(i => i.SortOrder).ThenBy(i => i.Id);

    public static IOrderedQueryable<Banner> Ordered(this IQueryable<Banner> q) =>
        q.OrderBy(b => b.SortOrder).ThenByDescending(b => b.Id);

    public static IOrderedQueryable<Store> Ordered(this IQueryable<Store> q) =>
        q.OrderBy(s => s.SortOrder).ThenBy(s => s.City).ThenBy(s => s.Id);

    public static IOrderedQueryable<SiteContact> Ordered(this IQueryable<SiteContact> q) =>
        q.OrderByDescending(c => c.IsActive).ThenBy(c => c.Id);

    public static IOrderedQueryable<Address> Ordered(this IQueryable<Address> q) =>
        q.OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.Id);

    public static IOrderedQueryable<Order> Ordered(this IQueryable<Order> q) =>
        q.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id);

    /// <summary>Баннеры, которые должны быть на главной в момент nowUtc: включены и попадают в сроки показа.</summary>
    public static IQueryable<Banner> VisibleAt(this IQueryable<Banner> q, DateTime nowUtc) =>
        q.Where(b => b.IsActive
            && (b.StartsAt == null || b.StartsAt <= nowUtc)
            && (b.EndsAt == null || b.EndsAt >= nowUtc));
}
