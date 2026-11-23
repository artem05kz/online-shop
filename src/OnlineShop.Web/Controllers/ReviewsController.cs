using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Controllers;

[Authorize]
public class ReviewsController(ShopDbContext db) : Controller
{
    /// <summary>Получал ли покупатель этот товар: только тогда ему есть что о нём сказать.</summary>
    public static Task<bool> CanReviewAsync(ShopDbContext db, int userId, int productId) =>
        db.OrderItems.AnyAsync(i => i.Order.UserId == userId && i.Order.Status == OrderStatus.Completed && i.Variant.ProductId == productId);

    [HttpPost("/product/{productId:int}/review")]
    public async Task<IActionResult> Save(int productId, int rating, string? text)
    {
        var product = await db.Products.Where(p => p.Id == productId && p.IsActive).Select(p => new { p.Id, p.Slug }).FirstOrDefaultAsync();
        if (product is null)
        {
            return NotFound();
        }
        var back = Url.Action("Product", "Catalog", new { slug = product.Slug }) + "#reviews";
        var userId = User.ShopUserId();
        if (!await CanReviewAsync(db, userId, productId))
        {
            TempData.Flash("Отзыв можно оставить после того, как получите этот товар.", FlashLevel.Error);
            return LocalRedirect(back);
        }
        text = text?.Trim() ?? "";
        if (rating is < 1 or > 5 || text.Length > Review.MaxTextLength)
        {
            TempData.Flash($"Поставьте оценку от 1 до 5 звёзд. Текст не длиннее {Review.MaxTextLength} символов.", FlashLevel.Error);
            return LocalRedirect(back);
        }

        // Один отзыв на товар: повторная отправка правит прежний, и он снова уходит на проверку.
        var review = await db.Reviews.FirstOrDefaultAsync(r => r.ProductId == productId && r.UserId == userId);
        var now = DateTime.UtcNow;
        if (review is null)
        {
            db.Reviews.Add(review = new Review { ProductId = productId, UserId = userId, CreatedAt = now });
        }
        review.Rating = rating;
        review.Text = text;
        review.Status = ReviewStatus.Pending;
        review.UpdatedAt = now;
        await db.SaveChangesAsync();

        // Сообщение показывается прямо в блоке отзывов (карточка «Отзыв отправлен»), а не плашкой вверху страницы:
        // после отправки страница открывается на отзывах, и верх страницы покупатель не видит.
        TempData["ReviewSent"] = true;
        return LocalRedirect(back);
    }
}
