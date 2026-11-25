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
    /// <summary>Клиент приехал, машину моют (CRM, 24.11). Бокс занят.</summary>
    Arrived,
    /// <summary>Клиент не приехал (CRM, 24.11): в отличие от отмены, видно, кто не приходит.</summary>
    NoShow,
}

/// <summary>Откуда пришла запись (CRM, 24.11). Хранится кодом.</summary>
public static class WashSources
{
    public const string Site = "site";
    public const string Phone = "phone";
    public const string WhatsApp = "whatsapp";
    public const string WalkIn = "walkin";

    public static readonly (string Code, string Text)[] All =
        [(Site, "Сайт"), (Phone, "Звонок"), (WhatsApp, "WhatsApp"), (WalkIn, "Приехал сам")];

    public static string Text(string? code) => All.FirstOrDefault(s => s.Code == code).Text ?? "Сайт";
}

/// <summary>Способы оплаты мойки (CRM, 24.11). Хранится кодом.</summary>
public static class WashPayments
{
    public static readonly (string Code, string Text)[] All =
        [("cash", "Наличные"), ("kaspi", "Kaspi"), ("card", "Карта")];

    public static string Text(string? code) => All.FirstOrDefault(s => s.Code == code).Text ?? "";
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

    // ---- CRM (24.11) ----

    /// <summary>Место (бокс) на мойке: 1…число мест. 0 — ещё не назначено.</summary>
    public int Box { get; set; }

    /// <summary>Откуда запись: <see cref="WashSources"/>.</summary>
    [StringLength(20)]
    public string Source { get; set; } = WashSources.Site;

    /// <summary>Цена услуги для этой записи, ₸. null — ещё не назначена.</summary>
    public decimal? Price { get; set; }

    public bool IsPaid { get; set; }

    /// <summary>Способ оплаты: <see cref="WashPayments"/>; пусто — не оплачено.</summary>
    [StringLength(20)]
    public string PaymentMethod { get; set; } = "";

    /// <summary>Заметка администратора, клиент её не видит.</summary>
    [StringLength(500)]
    public string ManagerNote { get; set; } = "";

    /// <summary>Кто подтвердил запись (имя в панели или в Telegram) — для истории.</summary>
    [StringLength(100)]
    public string ConfirmedBy { get; set; } = "";

    // ---- Напоминания (CRM, шаг 2, 25.11) ----

    /// <summary>Бот уже напомнил администраторам, что запись 30 минут ждёт подтверждения.</summary>
    public bool WaitReminderSent { get; set; }

    /// <summary>Бот уже прислал эту запись в вечернем списке «Завтра на мойке» — напомнить клиенту.</summary>
    public bool ClientReminderSent { get; set; }

    /// <summary>Правило мойки (25.11): если клиент опоздал больше чем на 15 минут, запись отменяется — «Не приехал».</summary>
    public static readonly TimeSpan LateLimit = TimeSpan.FromMinutes(15);

    /// <summary>Время пришло, клиента ещё нет, но 15 минут не прошло: ждём.</summary>
    public bool IsAwaited(DateTime now) =>
        Status is WashBookingStatus.New or WashBookingStatus.Confirmed && now >= StartsAt && now < StartsAt + LateLimit;

    /// <summary>Клиент опоздал больше чем на 15 минут, а запись всё ещё не отмечена: нужно нажать «Не приехал».</summary>
    public bool IsLate(DateTime now) =>
        Status is WashBookingStatus.New or WashBookingStatus.Confirmed && now >= StartsAt + LateLimit;

    /// <summary>Сколько секунд осталось ждать клиента (для отсчёта на странице).</summary>
    public int WaitSecondsLeft(DateTime now) => (int)Math.Max(0, Math.Ceiling((StartsAt + LateLimit - now).TotalSeconds));

    /// <summary>Сколько минут клиент опаздывает.</summary>
    public int LateMinutes(DateTime now) => (int)Math.Max(0, (now - StartsAt).TotalMinutes);

    /// <summary>Занимает ли запись бокс (отменённые — нет).</summary>
    [NotMapped]
    public bool Holds => Status is WashBookingStatus.New or WashBookingStatus.Confirmed or WashBookingStatus.Arrived;
}

public static class WashBookingStatusInfo
{
    public static string Text(this WashBookingStatus status) => status switch
    {
        WashBookingStatus.New => "Ждёт подтверждения",
        WashBookingStatus.Confirmed => "Подтверждена",
        WashBookingStatus.Cancelled => "Отменена",
        WashBookingStatus.Done => "Выполнена",
        WashBookingStatus.Arrived => "Приехал, моем",
        WashBookingStatus.NoShow => "Не приехал",
        _ => status.ToString(),
    };

    public static string Code(this WashBookingStatus status) => status.ToString().ToLowerInvariant();
}
