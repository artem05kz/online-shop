using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Политика конфиденциальности и согласие на обработку данных при регистрации и первом заказе.</summary>
public class PrivacyTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private const string Password = "согласие-на-данные";

    private T Db<T>(Func<ShopDbContext, T> query)
    {
        using var scope = factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    private static Task<HttpResponseMessage> Register(HttpClient client, string token, string login, bool accept) =>
        Post(client, "/accounts/register", token, [("Input.UserName", login), ("Input.Email", ""),
            ("Input.Password", Password), ("Input.ConfirmPassword", Password),
            .. accept ? new[] { ("Input.AcceptPrivacy", "true") } : []]);

    [Fact]
    public async Task Policy_page_names_seller_from_contacts_and_footer_links_to_it()
    {
        Db(db =>
        {
            db.SiteContacts.Add(new SiteContact
            {
                Phone = "+7 701 245-48-48", LegalName = "ИП Тестов Тест", Bin = "123456789012",
                LegalAddress = "Актау, 14 мкр, 5",
            });
            return db.SaveChanges();
        });

        var page = await Page(factory.Browser(), "/privacy");
        Assert.Contains("ИП Тестов Тест, БИН/ИИН 123456789012, адрес: Актау, 14 мкр, 5.", page);
        Assert.Contains("Telegram", page);
        Assert.Contains("№ 94-V", page);
        Assert.Contains("href=\"/privacy\"", await Page(factory.Browser(), "/"));
    }

    [Fact]
    public async Task Registration_requires_consent_and_remembers_when_it_was_given()
    {
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/register");
        Assert.Contains("политики конфиденциальности", await Page(client, "/accounts/register"));

        var refused = await Register(client, token, "без-согласия", accept: false);
        Assert.Equal(HttpStatusCode.OK, refused.StatusCode);
        Assert.Contains("без согласия на обработку данных", await Text(refused));
        Assert.False(Db(db => db.Users.Any(u => u.UserName == "без-согласия")));

        var before = DateTime.UtcNow.AddMinutes(-1);
        Assert.Equal(HttpStatusCode.Redirect, (await Register(client, token, "с-согласием", accept: true)).StatusCode);
        var consent = Db(db => db.Users.Single(u => u.UserName == "с-согласием").PersonalDataConsentAt);
        Assert.True(consent > before);
    }

    [Fact]
    public async Task Buyer_registered_before_the_checkbox_confirms_consent_at_first_checkout()
    {
        // Покупатель из старой базы: согласия ещё нет.
        var variantId = Db(db =>
        {
            var variant = new ProductVariant { Article = "PRV1", Price = 500m, StockQty = 3 };
            db.Products.Add(new Product { Category = TestDb.NewCategory("Ifoam", "Воски"), Name = "Воск", Variants = [variant] });
            db.SaveChanges();
            return variant.Id;
        });
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/register");
        await Register(client, token, "старый-покупатель", accept: true);
        Db(db => db.Users.Where(u => u.UserName == "старый-покупатель")
            .ExecuteUpdate(s => s.SetProperty(u => u.PersonalDataConsentAt, (DateTime?)null)));

        // После входа нужен новый токен формы: прежний выдан гостю.
        token = await TokenFrom(client, "/");
        await Post(client, $"/orders/cart/add/{variantId}", token);
        Assert.Contains("политики конфиденциальности", await Page(client, "/orders/checkout"));
        (string, string)[] address = [("Form.ContactName", "Иван"), ("Form.ContactPhone", "+7 700 000-00-00"),
            ("Form.City", "Актау"), ("Form.StreetAndDetails", "14 мкр, дом 5")];

        var refused = await Post(client, "/orders/checkout", token, address);
        Assert.Contains("без согласия на обработку данных", await Text(refused));
        Assert.False(Db(db => db.Orders.Any(o => o.User.UserName == "старый-покупатель")));

        var placed = await Post(client, "/orders/checkout", token, [.. address, ("Form.AcceptPrivacy", "true")]);
        Assert.Equal(HttpStatusCode.Redirect, placed.StatusCode);
        Assert.NotNull(Db(db => db.Users.Single(u => u.UserName == "старый-покупатель").PersonalDataConsentAt));

        // Второй заказ галочку уже не спрашивает.
        await Post(client, $"/orders/cart/add/{variantId}", token);
        Assert.DoesNotContain("политики конфиденциальности</a></span>", await Page(client, "/orders/checkout"));
    }
}
