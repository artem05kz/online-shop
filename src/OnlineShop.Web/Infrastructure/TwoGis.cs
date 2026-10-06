using System.Globalization;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Карты 2ГИС. Сама карта и поиск адреса по точке требуют ключа (переменная TWOGIS_API_KEY),
/// без него сайт показывает открытые карты OpenStreetMap. Ссылки «открыть в 2ГИС» и маршрут
/// работают и без ключа: это обычные адреса на сайте 2gis.kz.
/// </summary>
public static class TwoGis
{
    public const string ConfigKey = "TWOGIS_API_KEY";

    public static string Key(IConfiguration configuration) => configuration[ConfigKey]?.Trim() ?? "";

    private static string Point(double lat, double lng) =>
        lng.ToString("0.######", CultureInfo.InvariantCulture) + "," + lat.ToString("0.######", CultureInfo.InvariantCulture);

    /// <summary>Точка на карте 2ГИС.</summary>
    public static string PlaceUrl(double lat, double lng) => $"https://2gis.kz/geo/{Point(lat, lng)}";

    /// <summary>Маршрут до точки: 2ГИС сам предложит начать от места, где сейчас покупатель.</summary>
    public static string RouteUrl(double lat, double lng) =>
        $"https://2gis.kz/directions/points/%7C{Uri.EscapeDataString(Point(lat, lng))}";
}
