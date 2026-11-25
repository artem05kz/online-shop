namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Доставка (уточнение заказчицы 24.11.2026). По Актау — минимальная сумма заказа с доставкой 7 000 ₸:
/// товары на 7 000 ₸ и больше — доставка бесплатная; меньше — покупатель доплачивает разницу до 7 000 ₸
/// (товары на 1 000 ₸ → доставка 6 000 ₸, к оплате 7 000 ₸). То есть товары «сами оплачивают» доставку.
/// По Мангистауской области (Жанаозен, Шетпе, районы) — от 10 000 ₸, точную сумму называет менеджер.
/// Сайт показывает доставку в корзине, при оформлении и на странице заказа; в сам заказ (база, письма, панель)
/// цена доставки не записывается — менеджер подтверждает её при звонке.
/// </summary>
public static class DeliveryPrices
{
    /// <summary>Минимальная сумма заказа с доставкой по Актау; от неё доставка бесплатная.</summary>
    public const decimal CityMinimum = 7000m;

    /// <summary>«7 000 ₸» для текстов.</summary>
    public static string CityMinimumText => Format.Money(CityMinimum);

    /// <summary>Короткий текст условия для плиток и карточек.</summary>
    public static string City => $"бесплатно от {CityMinimumText}";

    public const string Region = "от 10 000 ₸";

    /// <summary>Сколько доплатить за доставку по Актау: разница до 7 000 ₸ (0 — доставка бесплатная).</summary>
    public static decimal CityFee(decimal goods) => goods >= CityMinimum ? 0m : CityMinimum - goods;

    /// <summary>Город доставки — Актау (тогда цена известна заранее). Пусто тоже считаем Актау: это значение по умолчанию.</summary>
    public static bool IsCity(string? city) =>
        string.IsNullOrWhiteSpace(city) || city.Contains("актау", StringComparison.OrdinalIgnoreCase) || city.Contains("aktau", StringComparison.OrdinalIgnoreCase);
}
