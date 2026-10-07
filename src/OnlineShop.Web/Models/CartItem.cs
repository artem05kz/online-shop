namespace OnlineShop.Web.Models;

/// <summary>
/// Позиция корзины. Корзина хранится в базе, а не в памяти сервера: перезапуск или обновление сайта
/// её не стирает. Владелец: покупатель (u:номер) или гость (g:метка из куки).
/// </summary>
public class CartItem
{
    public int Id { get; set; }

    public string OwnerKey { get; set; } = "";

    public int VariantId { get; set; }
    public ProductVariant Variant { get; set; } = null!;

    public int Quantity { get; set; }

    public DateTime UpdatedAt { get; set; }
}
