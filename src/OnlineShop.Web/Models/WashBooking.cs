using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace OnlineShop.Web.Models;

public enum WashBookingStatus
{
    /// <summary>Только что записались, администратор ещё не подтвердил.</summary>
    New,
    Confirmed,
    /// <summary>Отменил клиент или администратор: время снова свободно.</summary>
    Cancelled,
    /// <summary>Машину помыли.</summary>
    Done,
}

/// <summary>
/// Запись на автомойку. Регистрироваться не нужно, как и для заказа: клиент оставляет имя и телефон,
/// свою запись видит по ссылке с <see cref="AccessToken"/>. Мойка и услуга хранятся ключом и названием:
/// список моек и услуг задан в коде (Infrastructure/CarWashes.cs) и может поменяться.
/// </summary>
public class WashBooking
{
    public int Id { get; set; }

    [Required, StringLength(40)]
    public string WashKey { get; set; } = "";

    [StringLength(100)]
    public string WashName { get; set; } = "";

    [Required, StringLength(40)]
    public string ServiceKey { get; set; } = "";

    [StringLength(100)]
    public string ServiceName { get; set; } = "";

    public int Minutes { get; set; }

    /// <summary>Начало, время Актау (как на часах у мойки, без перевода в UTC).</summary>
    public DateTime StartsAt { get; set; }

    [NotMapped]
    public DateTime EndsAt => StartsAt.AddMinutes(Minutes);

    [Required, StringLength(100)]
    public string Name { get; set; } = "";

    /// <summary>Только цифры с кодом страны (77011234567).</summary>
    [Required, StringLength(20)]
    public string Phone { get; set; } = "";

    [StringLength(100)]
    public string Car { get; set; } = "";

    [StringLength(500)]
    public string Comment { get; set; } = "";

    public WashBookingStatus Status { get; set; } = WashBookingStatus.New;

    /// <summary>Когда записались, UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? UserId { get; set; }

    [StringLength(32)]
    public string AccessToken { get; set; } = Order.NewAccessToken();

    /// <summary>Занимает ли запись бокс (отменённые — нет).</summary>
    [NotMapped]
    public bool Holds => Status is WashBookingStatus.New or WashBookingStatus.Confirmed;
}

public static class WashBookingStatusInfo
{
    public static string Text(this WashBookingStatus status) => status switch
    {
        WashBookingStatus.New => "Ждёт подтверждения",
        WashBookingStatus.Confirmed => "Подтверждена",
        WashBookingStatus.Cancelled => "Отменена",
        WashBookingStatus.Done => "Выполнена",
        _ => status.ToString(),
    };

    public static string Code(this WashBookingStatus status) => status.ToString().ToLowerInvariant();
}
