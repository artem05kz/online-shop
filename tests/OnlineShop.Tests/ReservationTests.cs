using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>
/// Резерв: заказанное с сайта не продаётся второй раз, даже если загрузка из 1С вернула прежний остаток.
/// Выполненный или отменённый заказ резерв снимает.
/// </summary>
public class ReservationTests : IClassFixture<StorefrontTests.ShopFactory>
{
    private readonly StorefrontTests.ShopFactory _factory;

    public ReservationTests(StorefrontTests.ShopFactory factory)
    {
        _factory = factory;
        _factory.Seed();
    }

    private T Db<T>(Func<ShopDbContext, T> query)
    {
        using var scope = _factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    private int SeedVariant(int stock) => Db(db =>
    {
        var variant = new ProductVariant { Article = $"R{Guid.NewGuid():N}"[..12], PackLabel = "5 л", Price = 5000m, StockQty = stock };
        db.Products.Add(new Product { Category = TestDb.NewCategory($"Резерв{Guid.NewGuid():N}"[..14], "Шампуни"), Name = $"Последний {variant.Article}", Variants = [variant] });
        db.SaveChanges();
        return variant.Id;
    });

    private HttpClient Buyer()
    {
        var client = _factory.Browser();
        client.DefaultRequestHeaders.Add("X-Test-User", _factory.BuyerId.ToString());
        return client;
    }

    private static Task<HttpResponseMessage> Checkout(HttpClient client, string token) =>
        Post(client, "/orders/checkout", token, ("Form.ContactName", "Иван"), ("Form.ContactPhone", "+7 700"),
            ("Form.City", "Актау"), ("Form.StreetAndDetails", "14 мкр"), ("Form.AcceptPrivacy", "true"));

    private async Task SetStatus(int orderId, OrderStatus status)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        await db.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status));
        await scope.ServiceProvider.GetRequiredService<StockReservations>().RecalculateForOrdersAsync([orderId]);
    }

    [Fact]
    public async Task Ordered_stock_is_held_until_order_is_completed_even_if_1c_restores_it()
    {
        var variantId = SeedVariant(stock: 2);
        var client = Buyer();
        var token = await TokenFrom(client, "/");
        await Post(client, $"/orders/cart/add/{variantId}", token, ("quantity", "2"));
        var placed = await Checkout(client, token);
        var orderId = int.Parse(placed.Headers.Location!.OriginalString.Split('/').Last());

        var variant = Db(db => db.ProductVariants.Single(v => v.Id == variantId));
        Assert.Equal((2, 2, 0, false), (variant.StockQty, variant.ReservedQty, variant.AvailableQty, variant.InStock));

        // Из 1С пришёл прежний остаток 2: продажа там ещё не проведена. Товар всё равно не продаётся второй раз.
        Db(db => db.ProductVariants.Where(v => v.Id == variantId).ExecuteUpdate(s => s.SetProperty(v => v.StockQty, 2)));
        await Post(client, $"/orders/cart/add/{variantId}", token);
        Assert.Contains("закончился, положить в корзину нельзя", await Page(client, "/orders/cart"));
        await Checkout(client, token);
        Assert.Equal(1, Db(db => db.Orders.Count(o => o.Items.Any(i => i.VariantId == variantId))));

        // Заказ выполнен: резерв снят, на сайте снова остаток из 1С.
        await SetStatus(orderId, OrderStatus.Completed);
        Assert.Equal(0, Db(db => db.ProductVariants.Single(v => v.Id == variantId).ReservedQty));
    }

    [Fact]
    public async Task Buyer_cannot_order_more_than_available_and_cancel_returns_reserve()
    {
        var variantId = SeedVariant(stock: 3);
        var client = Buyer();
        var token = await TokenFrom(client, "/");
        // Корзина сама не даёт положить больше остатка.
        await Post(client, $"/orders/cart/add/{variantId}", token, ("quantity", "5"));
        Assert.Contains("в корзине 3 шт.", await Page(client, "/orders/cart"));

        // Пока товар лежал в корзине, остаток уменьшился: оформление не пропускает и говорит, сколько осталось.
        Db(db => db.ProductVariants.Where(v => v.Id == variantId).ExecuteUpdate(s => s.SetProperty(v => v.StockQty, 2)));
        Assert.Contains("В наличии только 2 шт., уменьшите количество.", await Page(client, "/orders/cart"));
        var tooMany = await Checkout(client, token);
        Assert.Contains("осталось 2 шт.", await Text(tooMany));

        await Post(client, $"/orders/cart/update/{variantId}", token, ("quantity", "2"));
        var placed = await Checkout(client, token);
        var orderId = int.Parse(placed.Headers.Location!.OriginalString.Split('/').Last());
        Assert.Equal(2, Db(db => db.ProductVariants.Single(v => v.Id == variantId).ReservedQty));
        Assert.Contains("Нет в наличии", await Page(_factory.Browser(), "/search?q=" + Uri.EscapeDataString(Db(db => db.Products.Single(p => p.Variants.Any(v => v.Id == variantId)).Name))));

        await SetStatus(orderId, OrderStatus.Canceled);
        Assert.Equal(0, Db(db => db.ProductVariants.Single(v => v.Id == variantId).ReservedQty));
    }

    [Fact]
    public async Task Reserve_is_recalculated_on_startup_for_old_orders()
    {
        var variantId = SeedVariant(stock: 10);
        Db(db =>
        {
            db.Orders.Add(new Order
            {
                UserId = _factory.BuyerId, ContactName = "Иван", ContactPhone = "+7", City = "Актау", StreetAndDetails = "1",
                Items = [new OrderItem { VariantId = variantId, ProductName = "x", Price = 1m, Quantity = 4 }],
            });
            return db.SaveChanges();
        });
        Assert.Equal(0, Db(db => db.ProductVariants.Single(v => v.Id == variantId).ReservedQty));

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<StockReservations>().RecalculateAllAsync();
        Assert.Equal(4, Db(db => db.ProductVariants.Single(v => v.Id == variantId).ReservedQty));
    }
}
