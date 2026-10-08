using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Самовывоз при оформлении и лист заказа для печати в панели.</summary>
public class PickupTests : IClassFixture<StorefrontTests.ShopFactory>
{
    private readonly StorefrontTests.ShopFactory _factory;
    private readonly int _shopId;
    private readonly int _warehouseId;

    public PickupTests(StorefrontTests.ShopFactory factory)
    {
        _factory = factory;
        _factory.Seed();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        if (!db.Stores.Any())
        {
            db.Stores.AddRange(
                new Store { Address = "21 мкр, рынок «Алем», бутик 74", Kind = StoreKind.Shop, WorkingHours = "Пн-Сб, 9:00-18:00" },
                new Store { Address = "Промзона, склад 3", Kind = StoreKind.Warehouse });
            db.SaveChanges();
        }
        _shopId = db.Stores.Single(s => s.Kind == StoreKind.Shop).Id;
        _warehouseId = db.Stores.Single(s => s.Kind == StoreKind.Warehouse).Id;
    }

    private HttpClient Buyer()
    {
        var client = _factory.Browser();
        client.DefaultRequestHeaders.Add("X-Test-User", _factory.BuyerId.ToString());
        return client;
    }

    private async Task<string> FillCart(HttpClient client)
    {
        var token = await TokenFrom(client, "/product/шампунь-тест");
        await Post(client, $"/orders/cart/add/{_factory.InStockVariantId}", token);
        return token;
    }

    [Fact]
    public async Task Checkout_offers_shops_but_not_warehouses_for_pickup()
    {
        var client = Buyer();
        await FillCart(client);
        var page = await Page(client, "/orders/checkout");
        Assert.Contains("Самовывоз", page);
        Assert.Contains("рынок «Алем», бутик 74", page);
        Assert.DoesNotContain("склад 3", page);
    }

    [Fact]
    public async Task Pickup_order_needs_no_address_and_keeps_shop_address()
    {
        var client = Buyer();
        var token = await FillCart(client);

        // Самовывоз без выбранного магазина не принимается, а склад выбрать нельзя.
        var noShop = await Post(client, "/orders/checkout", token, ("Form.IsPickup", "true"),
            ("Form.ContactName", "Иван"), ("Form.ContactPhone", "+7 700 000-00-00"), ("Form.AcceptPrivacy", "true"));
        Assert.Contains("Выберите магазин", await Text(noShop));
        var warehouse = await Post(client, "/orders/checkout", token, ("Form.IsPickup", "true"), ("Form.PickupStoreId", _warehouseId.ToString()),
            ("Form.ContactName", "Иван"), ("Form.ContactPhone", "+7 700 000-00-00"), ("Form.AcceptPrivacy", "true"));
        Assert.Contains("Выберите магазин", await Text(warehouse));

        var placed = await Post(client, "/orders/checkout", token, ("Form.IsPickup", "true"), ("Form.PickupStoreId", _shopId.ToString()),
            ("Form.ContactName", "Иван"), ("Form.ContactPhone", "+7 700 000-00-00"), ("Form.AcceptPrivacy", "true"));
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        var orderId = int.Parse(placed.Headers.Location!.OriginalString.Split('/').Last());

        using (var scope = _factory.Services.CreateScope())
        {
            var order = scope.ServiceProvider.GetRequiredService<ShopDbContext>().Orders.Single(o => o.Id == orderId);
            Assert.True(order.IsPickup);
            Assert.Equal((_shopId, "21 мкр, рынок «Алем», бутик 74"), (order.PickupStoreId!.Value, order.StreetAndDetails));
        }
        Assert.Contains("<b>Самовывоз</b> из магазина: Актау, 21 мкр", await TelegramAbout(orderId));
        Assert.Contains("Самовывоз из магазина", (await LastMail(orderId)).Text);
    }

    [Fact]
    public async Task Delivery_still_requires_address()
    {
        var client = Buyer();
        var token = await FillCart(client);
        var noAddress = await Post(client, "/orders/checkout", token, ("Form.IsPickup", "false"),
            ("Form.ContactName", "Иван"), ("Form.ContactPhone", "+7 700"), ("Form.City", ""), ("Form.StreetAndDetails", ""), ("Form.AcceptPrivacy", "true"));
        Assert.Equal(HttpStatusCode.OK, noAddress.StatusCode);
        Assert.Contains("Обязательное поле.", await Text(noAddress));
    }

    [Fact]
    public void Pickup_order_in_delivery_status_reads_as_ready_for_buyer()
    {
        var order = new Order { IsPickup = true, Status = OrderStatus.Delivering };
        Assert.Equal("Готов к выдаче", order.StatusText(order.Status));
        Assert.Contains(order.ProgressSteps, s => s.Label == "Готов к выдаче" && s.IsCurrent);
        Assert.Equal("Доставляется", new Order { Status = OrderStatus.Delivering }.StatusText(OrderStatus.Delivering));
    }

    private async Task<string> TelegramAbout(int orderId)
    {
        for (var i = 0; i < 250; i++)
        {
            var text = _factory.Telegram.SentTo(StorefrontTests.ShopFactory.ManagerChatId)
                .Select(m => m.Text).LastOrDefault(t => t.Contains($"Новый заказ №{orderId}<"));
            if (text is not null)
            {
                return text;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Нет сообщения в Telegram.");
    }

    private async Task<OnlineShop.Web.Services.Email.EmailMessage> LastMail(int orderId)
    {
        for (var i = 0; i < 250; i++)
        {
            var mail = _factory.Email.SentTo("buyer@example.com").LastOrDefault(m => m.Subject.Contains($"№{orderId}"));
            if (mail is not null)
            {
                return mail;
            }
            await Task.Delay(20);
        }
        throw new TimeoutException("Нет письма о заказе.");
    }
}
