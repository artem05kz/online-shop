using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Tests;

/// <summary>Временный список хитов заказчицы: товар — хит, если в названии есть слово из списка.</summary>
public class HitProductsTests
{
    [Theory]
    [InlineData("Очиститель двигателя  \"MOTOR SHINE\"", true)]
    [InlineData("Полироль для шин \"TYRE BLACK\"", true)]
    [InlineData("Очиститель стёкол «GLASS GLOSS»", true)]
    [InlineData("Жидкое мыло «Лимон»", true)]
    [InlineData("Холодный воск «TOP WAX»", false)]
    public void Hit_is_found_by_name(string name, bool expected) =>
        Assert.Equal(expected, HitProducts.IsHit(new Product { Name = name }));
}
