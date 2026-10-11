using System.Text.RegularExpressions;
﻿using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Отзывы: только от получивших товар, один на товар, на сайте после проверки менеджером.</summary>
public class ReviewTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private const string Password = "отзыв-о-товаре";
    private static int _counter;

    private T Db<T>(Func<ShopDbContext, T> query)
    {
        using var scope = factory.Services.CreateScope();
        return query(scope.ServiceProvider.GetRequiredService<ShopDbContext>());
    }

    private async Task<(HttpClient Client, int UserId)> SignIn(bool manager = false, string firstName = "")
    {
        var login = $"отзыв{Interlocked.Increment(ref _counter)}";
        int id;
        using (var scope = factory.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = new ApplicationUser { UserName = login, FirstName = firstName };
            await users.CreateAsync(user, Password);
            if (manager)
            {
                await users.AddToRoleAsync(user, RoleNames.Manager);
            }
            id = user.Id;
        }
        var client = factory.Browser();
        var token = await TokenFrom(client, "/accounts/login");
        await Post(client, "/accounts/login", token, ("Input.UserName", login), ("Input.Password", Password));
        return (client, id);
    }

    private (int ProductId, string Slug, int VariantId) SeedProduct() => Db(db =>
    {
        var variant = new ProductVariant { Article = $"V{Guid.NewGuid():N}"[..12], Price = 900m, StockQty = 5 };
        var product = new Product { Category = TestDb.NewCategory($"Отз{Guid.NewGuid():N}"[..12], "Воски"), Name = $"Воск {variant.Article}", Variants = [variant] };
        db.Products.Add(product);
        db.SaveChanges();
        return (product.Id, product.Slug, variant.Id);
    });

    private void SeedOrder(int userId, int variantId, OrderStatus status) => Db(db =>
    {
        db.Orders.Add(new Order
        {
            UserId = userId, Status = status, ContactName = "Иван", ContactPhone = "+7", City = "Актау", StreetAndDetails = "1",
            Items = [new OrderItem { VariantId = variantId, ProductName = "Воск", Price = 900m, Quantity = 1 }],
        });
        return db.SaveChanges();
    });

    private static async Task<HttpResponseMessage> Send(HttpClient client, string slug, int productId, int rating, string text)
    {
        // Токен берётся из самой формы отзыва: на показе у неё токена не было, а токен
        // соседней формы это скрывал. Формы нет (отзыв оставить нельзя), берём любой.
        var html = await client.GetStringAsync($"/product/{slug}");
        var form = Regex.Match(html, "<form[^>]*review-form[^>]*>(.*?)</form>", RegexOptions.Singleline);
        var token = form.Success
            ? Regex.Match(form.Groups[1].Value, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]+)\"").Groups[1].Value
            : await TokenFrom(client, $"/product/{slug}");
        Assert.True(!form.Success || token.Length > 0, "В форме отзыва нет защитного токена");
        return await Post(client, $"/product/{productId}/review", token, ("rating", rating.ToString()), ("text", text));
    }

    [Fact]
    public async Task Only_buyers_who_received_the_product_can_review()
    {
        var (productId, slug, variantId) = SeedProduct();
        Assert.Contains("Войдите</a>, чтобы оставить отзыв", await Page(factory.Browser(), $"/product/{slug}"));

        // Заказ ещё не выполнен: товар не получен, отзыв не принимается.
        var (client, userId) = await SignIn();
        SeedOrder(userId, variantId, OrderStatus.Delivering);
        Assert.DoesNotContain("Ваш отзыв", await Page(client, $"/product/{slug}"));
        await Send(client, slug, productId, 5, "Отлично");
        Assert.False(Db(db => db.Reviews.Any(r => r.ProductId == productId)));

        SeedOrder(userId, variantId, OrderStatus.Completed);
        Assert.Contains("Ваш отзыв", await Page(client, $"/product/{slug}"));
        var sent = await Send(client, slug, productId, 4, "Блестит хорошо");
        Assert.Equal(HttpStatusCode.Redirect, sent.StatusCode);
        Assert.Equal(ReviewStatus.Pending, Db(db => db.Reviews.Single(r => r.ProductId == productId)).Status);
    }

    [Fact]
    public async Task Review_appears_after_approval_and_editing_sends_it_back_to_check()
    {
        var (productId, slug, variantId) = SeedProduct();
        var (buyer, userId) = await SignIn(firstName: "Ержан");
        SeedOrder(userId, variantId, OrderStatus.Completed);
        await Send(buyer, slug, productId, 4, "Блестит <b>хорошо</b>");

        var guest = factory.Browser();
        Assert.Contains("Отзывов пока нет", await Page(guest, $"/product/{slug}"));

        var (manager, _) = await SignIn(manager: true);
        Assert.Contains("Блестит &lt;b&gt;хорошо&lt;/b&gt;", await manager.GetStringAsync("/admin/reviews"));
        Assert.Contains("Проверить отзывы", await Page(manager, "/admin"));
        var reviewId = Db(db => db.Reviews.Single(r => r.ProductId == productId).Id);
        var token = await TokenFrom(manager, "/admin/reviews");
        await Post(manager, $"/admin/reviews?handler=Approve&id={reviewId}", token);

        var page = await Page(guest, $"/product/{slug}");
        Assert.Contains("Ержан", page);
        Assert.Contains("4,0 из 5, 1 отзыв", page);
        // Текст покупателя выводится как текст: тег не станет разметкой.
        Assert.Contains("&lt;b&gt;хорошо&lt;/b&gt;", await guest.GetStringAsync($"/product/{slug}"));

        // Второй отзыв на тот же товар правит первый и снова отправляет его на проверку.
        await Send(buyer, slug, productId, 2, "Передумал");
        var review = Db(db => db.Reviews.Single(r => r.ProductId == productId));
        Assert.Equal((2, "Передумал", ReviewStatus.Pending), (review.Rating, review.Text, review.Status));
        Assert.Contains("Отзывов пока нет", await Page(guest, $"/product/{slug}"));
    }

    [Fact]
    public async Task Rating_must_be_one_to_five()
    {
        var (productId, slug, variantId) = SeedProduct();
        var (buyer, userId) = await SignIn();
        SeedOrder(userId, variantId, OrderStatus.Completed);
        await Send(buyer, slug, productId, 9, "Сломать оценку");
        Assert.False(Db(db => db.Reviews.Any(r => r.ProductId == productId)));
    }
}

