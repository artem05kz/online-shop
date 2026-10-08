using ClosedXML.Excel;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Отчёты: выручка за период без отменённых заказов, популярные товары, выгрузка в Excel.</summary>
public class ReportTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private async Task<HttpClient> Manager()
    {
        var login = $"отчёт{Guid.NewGuid():N}"[..16];
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = login };
            await users.CreateAsync(user, "отчёты-за-месяц");
            await users.AddToRoleAsync(user, RoleNames.Manager);
        }
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/login");
        await Post(client, "/accounts/login", token, ("Input.UserName", login), ("Input.Password", "отчёты-за-месяц"));
        return client;
    }

    private void Seed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        if (db.Orders.Any())
        {
            return;
        }
        var variant = new ProductVariant { Article = "REP1", PackLabel = "1 л", Price = 1000m, StockQty = 100 };
        db.Products.Add(new Product { Category = TestDb.NewCategory("Отчёт", "Шампуни"), Name = "Шампунь отчётный", Variants = [variant] });
        var user = new ApplicationUser { UserName = "покупатель-отчёта" };
        db.Users.Add(user);
        db.SaveChanges();

        Order Make(int quantity, OrderStatus status) => new()
        {
            UserId = user.Id, Status = status, ContactName = "Иван", ContactPhone = "+7 700", City = "Актау", StreetAndDetails = "1",
            Items = [new OrderItem { VariantId = variant.Id, ProductName = "Шампунь отчётный", PackLabel = "1 л", Article = "REP1", Price = 1000m, Quantity = quantity }],
        };
        db.Orders.AddRange(Make(2, OrderStatus.Completed), Make(3, OrderStatus.New), Make(10, OrderStatus.Canceled));
        db.SaveChanges();
    }

    [Fact]
    public async Task Report_counts_revenue_without_canceled_orders()
    {
        Seed();
        var page = await Page(await Manager(), "/admin/reports?period=7d");
        // Два заказа на 2 и 3 штуки по 1 000 ₸, отменённый на 10 штук не считается.
        Assert.Contains("<div class=\"v\">2</div><div class=\"l\">заказов</div>", page);
        Assert.Contains("5 000 ₸</div><div class=\"l\">выручка</div>", page);
        Assert.Contains("2 500 ₸</div><div class=\"l\">средний чек</div>", page);
        Assert.Contains("<div class=\"v\">1</div><div class=\"l\">отменено</div>", page);
        Assert.Contains("Шампунь отчётный", page);
        Assert.Contains("Выручка по дням", page);
    }

    [Fact]
    public async Task Long_period_is_shown_by_months_and_empty_period_says_so()
    {
        Seed();
        var client = await Manager();
        Assert.Contains("Выручка по месяцам", await Page(client, "/admin/reports?from=2026-01-01&to=2026-12-31"));
        Assert.Contains("За этот период продаж нет", await Page(client, "/admin/reports?from=2020-01-01&to=2020-01-31"));
    }

    [Fact]
    public async Task Excel_has_orders_and_items_for_the_period()
    {
        Seed();
        var today = DateOnly.FromDateTime(Format.ToLocal(DateTime.UtcNow)).ToString("yyyy-MM-dd");
        var response = await (await Manager()).GetAsync($"/admin/reports?handler=Excel&from={today}&to={today}");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", response.Content.Headers.ContentType!.MediaType);

        using var book = new XLWorkbook(await response.Content.ReadAsStreamAsync());
        var orders = book.Worksheet("Заказы");
        Assert.Equal("Номер", orders.Cell(1, 1).GetString());
        Assert.Equal(3, orders.RowsUsed().Count() - 1);
        Assert.Contains(orders.Column(3).CellsUsed().Select(c => c.GetString()), s => s == "Отменён");
        var items = book.Worksheet("Товары");
        Assert.Equal("REP1", items.Cell(2, 4).GetString());
    }

    [Fact]
    public async Task Reports_are_for_managers_only()
    {
        var guest = await factory.Browser().GetAsync("/admin/reports");
        Assert.Equal(System.Net.HttpStatusCode.Redirect, guest.StatusCode);
    }
}
