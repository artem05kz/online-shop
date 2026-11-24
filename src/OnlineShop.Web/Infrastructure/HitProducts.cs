using OnlineShop.Web.Models;

namespace OnlineShop.Web.Infrastructure;

/// <summary>
/// Хиты продаж — ВРЕМЕННО в коде (ответ заказчицы 24.11.2026): очиститель двигателя, чернитель резины,
/// средство для стёкол; из бытовой химии — жидкое мыло, антижир, пятновыводитель (появятся, когда их загрузят
/// из 1С). Товар считается хитом, если в названии есть одно из слов ниже. Когда в панели появится галочка «Хит»
/// (задача Артёма), этот список заменяется ею.
/// </summary>
public static class HitProducts
{
    private static readonly string[] Words =
        ["MOTOR SHINE", "TYRE BLACK", "GLASS GLOSS", "жидкое мыло", "антижир", "пятновывод"];

    public static bool IsHit(Product product) =>
        Words.Any(w => product.Name.Contains(w, StringComparison.OrdinalIgnoreCase));
}
