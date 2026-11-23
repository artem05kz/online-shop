using System.ComponentModel.DataAnnotations;

namespace OnlineShop.Web.Models;

public enum ReviewStatus
{
    Pending,
    Approved,
    Rejected,
}

public static class ReviewStatusInfo
{
    public static string ToCode(this ReviewStatus status) => status switch
    {
        ReviewStatus.Approved => "approved",
        ReviewStatus.Rejected => "rejected",
        _ => "pending",
    };

    public static ReviewStatus FromCode(string code) => code switch
    {
        "approved" => ReviewStatus.Approved,
        "rejected" => ReviewStatus.Rejected,
        _ => ReviewStatus.Pending,
    };

    public static string Label(this ReviewStatus status) => status switch
    {
        ReviewStatus.Approved => "Опубликован",
        ReviewStatus.Rejected => "Отклонён",
        _ => "Ждёт проверки",
    };
}

/// <summary>
/// Отзыв о товаре. Оставить его может только покупатель, получивший этот товар (выполненный заказ),
/// один отзыв на товар. На сайте отзыв появляется после проверки менеджером.
/// </summary>
public class Review
{
    /// <summary>Сколько символов покупатель может написать. Средний отзыв на Amazon — около 200 символов,
    /// 1000 — это абзац-другой, с запасом. Решено с дизайнером 23.11.2026 (было 2000).</summary>
    public const int MaxTextLength = 1000;

    /// <summary>Длина колонки в базе. Осталась прежней, чтобы не делать миграцию; ограничение задаёт MaxTextLength.</summary>
    public const int StorageLength = 2000;

    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    [Range(1, 5)]
    public int Rating { get; set; }

    [StringLength(StorageLength)]
    public string Text { get; set; } = "";

    public ReviewStatus Status { get; set; } = ReviewStatus.Pending;

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
