using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Письмо, когда товар из избранного снова появился в наличии.</summary>
public class RestockTests : IClassFixture<StorefrontTests.ShopFactory>
{
    private readonly StorefrontTests.ShopFactory _factory;

    public RestockTests(StorefrontTests.ShopFactory factory)
    {
        _factory = factory;
        _factory.Seed();
    }

    private T Db<T>(Func<ShopDbContext, T> query)
    {
        using var scope = _factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    private async Task<int> Check()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RestockNotifier>().CheckAsync();
    }

    private void SetStock(int variantId, int stock) =>
        Db(db => db.ProductVariants.Where(v => v.Id == variantId).ExecuteUpdate(s => s.SetProperty(v => v.StockQty, stock)));

    /// <summary>Письмо номер count о товаре: письма уходят из фоновой очереди, их надо подождать.</summary>
    private async Task<OnlineShop.Web.Services.Email.EmailMessage> WaitLetter(string name, int count)
    {
        for (var i = 0; i < 250; i++)
        {
            var letters = _factory.Email.SentTo("buyer@example.com").Where(m => m.Text.Contains(name) && m.Subject.Contains("наличии")).ToList();
            if (letters.Count >= count)
            {
                return letters[count - 1];
            }
            await Task.Delay(20);
        }
        throw new TimeoutException($"Нет письма №{count} о товаре {name}.");
    }

    private int Letters(string name) =>
        _factory.Email.SentTo("buyer@example.com").Count(m => m.Text.Contains(name) && m.Subject.Contains("наличии"));

    [Fact]
    public async Task One_letter_each_time_a_favorite_comes_back_in_stock()
    {
        var name = $"Воск {Guid.NewGuid():N}"[..13];
        var (productId, variantId, slug) = Db(db =>
        {
            var variant = new ProductVariant { Article = $"W{Guid.NewGuid():N}"[..12], Price = 700m, StockQty = 0 };
            var product = new Product { Category = TestDb.NewCategory($"Пост{Guid.NewGuid():N}"[..12], "Воски"), Name = name, Variants = [variant] };
            db.Products.Add(product);
            db.SaveChanges();
            return (product.Id, variant.Id, product.Slug);
        });

        var client = _factory.Browser();
        client.DefaultRequestHeaders.Add("X-Test-User", _factory.BuyerId.ToString());
        Assert.Contains("мы напишем на почту, когда он появится", await Page(client, $"/product/{slug}"));
        var token = await TokenFrom(client, $"/product/{slug}");
        await Post(client, $"/favorites/toggle/{productId}", token);

        await Check();
        Assert.Equal(0, Letters(name));

        // Пришли остатки из 1С: письмо со ссылкой на товар.
        SetStock(variantId, 4);
        Assert.Equal(1, await Check());
        var letter = await WaitLetter(name, 1);
        Assert.Contains($"Снова в наличии: {name}", letter.Subject);
        Assert.Contains($"/product/{Uri.EscapeDataString(slug)}", letter.Text);

        // Повторная проверка без изменений писем не шлёт.
        await Check();
        Assert.Equal(1, Letters(name));

        // Снова закончился и снова появился: ещё одно письмо.
        SetStock(variantId, 0);
        await Check();
        SetStock(variantId, 2);
        await Check();
        await WaitLetter(name, 2);
        Assert.Equal(2, Letters(name));
    }

    [Fact]
    public async Task Favorites_older_than_the_feature_do_not_get_a_letter_on_first_check()
    {
        var name = $"Старый {Guid.NewGuid():N}"[..14];
        Db(db =>
        {
            var product = new Product
            {
                Category = TestDb.NewCategory($"Ст{Guid.NewGuid():N}"[..12], "Воски"), Name = name,
                Variants = [new ProductVariant { Article = $"O{Guid.NewGuid():N}"[..12], Price = 700m, StockQty = 5 }],
            };
            db.Products.Add(product);
            db.SaveChanges();
            db.Favorites.Add(new Favorite { UserId = _factory.BuyerId, ProductId = product.Id, CreatedAt = DateTime.UtcNow, WasInStock = null });
            return db.SaveChanges();
        });

        await Check();
        Assert.Equal(0, Letters(name));
    }
}
