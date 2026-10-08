namespace OnlineShop.Web.Models;

/// <summary>Товар в избранном у покупателя. У гостя избранное лежит в куке, см. Services.Favorites.</summary>
public class Favorite
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public ApplicationUser User { get; set; } = null!;

    public int ProductId { get; set; }
    public Product Product { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    // Был ли товар в наличии при прошлой проверке (Services/RestockNotifier). null: ещё не проверяли.
    public bool? WasInStock { get; set; }
}
