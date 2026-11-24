namespace OnlineShop.Web.Infrastructure;

/// <summary>Автомойка, на которую можно записаться с сайта.</summary>
/// <param name="Key">Короткое имя для адреса и базы (не меняется, иначе старые записи «потеряют» мойку).</param>
/// <param name="Opens">Первая запись не раньше.</param>
/// <param name="Closes">Машина должна быть готова к закрытию.</param>
/// <param name="Boxes">Сколько машин моют одновременно: столько записей может быть на одно время.</param>
public record CarWash(string Key, string Name, string Address, TimeOnly Opens, TimeOnly Closes, int Boxes,
    string Phone = "", string MapUrl = "");

/// <summary>Услуга мойки.</summary>
/// <param name="Minutes">Сколько времени занимает: на это время бокс занят.</param>
/// <param name="PriceFrom">Цена «от»; пусто — цену называет администратор.</param>
public record WashService(string Key, string Name, string Text, int Minutes, decimal? PriceFrom = null);

/// <summary>
/// Мойки и услуги для записи. Адрес, часы и телефон мойки — из 2ГИС; 4 места, запись на 5 дней вперёд
/// не позже чем за 2 часа — ответы заказчицы (24.11.2026). Услуги и цены пришлют списком — пока условные. Вторая мойка GRASS (самообслуживание, Промзона 1, 67/1,
/// круглосуточно) сюда не входит: на неё не записываются. Когда придут данные, правим здесь
/// (или переносим в панель — это к Артёму).
/// </summary>
public static class CarWashes
{
    /// <summary>Шаг сетки времени, минуты.</summary>
    public const int SlotStep = 30;

    /// <summary>На сколько дней вперёд можно записаться.</summary>
    public const int DaysAhead = 5;

    /// <summary>Записаться можно не позже, чем за столько минут до начала: администратор должен успеть увидеть запись.</summary>
    public const int MinLeadMinutes = 120;

    /// <summary>Сколько ждём опоздавшего; потом место может занять другой клиент (пишем на странице записи).</summary>
    public const int LateMinutes = 15;

    public static readonly IReadOnlyList<CarWash> All =
    [
        new("wash-1", "Автомойка Grass", "10-й микрорайон, 3/1", new(9, 0), new(21, 0), 4,
            Phone: "77757000888", MapUrl: "https://2gis.kz/aktau/firm/70000001109684189"),
    ];

    public static readonly IReadOnlyList<WashService> Services =
    [
        new("express", "Экспресс-мойка", "Кузов с пеной, ополаскивание, сушка", 30),
        new("complex", "Комплексная мойка", "Кузов, коврики, пылесос и протирка салона", 60),
        new("wax", "Мойка с воском", "Комплексная мойка и защитный воск для блеска", 90),
        new("interior", "Химчистка салона", "Сиденья, потолок, ковры и пластик", 180),
    ];

    public static CarWash? Find(string? key) => All.FirstOrDefault(w => w.Key == key);

    public static WashService? FindService(string? key) => Services.FirstOrDefault(s => s.Key == key);
}
