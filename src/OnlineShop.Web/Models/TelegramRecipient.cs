using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

/// <summary>
/// Номер из списка получателей уведомлений о заказах. Telegram не даёт боту писать человеку
/// по номеру телефона, поэтому номер становится чатом только после того, как человек сам
/// написал боту и отправил свой контакт кнопкой: тогда здесь появляется ChatId.
/// </summary>
public class TelegramRecipient
{
    public int Id { get; set; }

    [Display(Name = "Кто это")]
    [StringLength(100)]
    public string Name { get; set; } = "";

    // Только цифры с кодом страны (77011234567): так номер из панели сравнивается
    // с номером из Telegram, как бы его ни написали.
    [Display(Name = "Телефон")]
    [Required, StringLength(20)]
    public string Phone { get; set; } = "";

    public long? ChatId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LinkedAt { get; set; }

    [NotMapped]
    public bool IsLinked => ChatId is not null;
}
