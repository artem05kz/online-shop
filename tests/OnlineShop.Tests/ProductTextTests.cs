using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;

namespace OnlineShop.Tests;

/// <summary>Описание товара делится на абзац и шаги «Способ применения», цена считается за 1 кг или 1 л.</summary>
public class ProductTextTests
{
    [Fact]
    public void How_to_steps_become_a_list()
    {
        var parts = ProductText.Split("Средство для мойки.\n\nСпособ применения:\n1. Сбить пыль водой.\n2. Нанести средство,\nвыдержать 2 минуты.\n3. Смыть водой.");
        Assert.Equal("Средство для мойки.", parts.Intro);
        Assert.Equal(["Сбить пыль водой.", "Нанести средство, выдержать 2 минуты.", "Смыть водой."], parts.Steps);
        Assert.Equal("", parts.Outro);
    }

    [Fact]
    public void Wrapped_step_starting_with_bracket_stays_in_its_step()
    {
        // Так в 1С записан «Бесконтактный автошампунь PREMIUM»: первый шаг перенесён, продолжение начинается со скобки.
        var parts = ProductText.Split("Средство.\n\nСпособ применения:\n1. Разбавить 1:6 - 1:12 или 1:60 - 1:120\n(10-15 г/л) для пеногенератора. \n2. Сбить грязь. \n3. Выдержать 1-2 мин.! \n4. Смыть (15-20 см) снизу вверх.\n\nХранить при температуре от +5 °C.");
        Assert.Equal(4, parts.Steps.Count);
        Assert.Equal("Разбавить 1:6 - 1:12 или 1:60 - 1:120 (10-15 г/л) для пеногенератора.", parts.Steps[0]);
        Assert.Equal("Смыть (15-20 см) снизу вверх.", parts.Steps[3]);
        Assert.Equal("Хранить при температуре от +5 °C.", parts.Outro);
    }

    [Fact]
    public void Text_without_steps_stays_as_is()
    {
        var parts = ProductText.Split("Просто описание без шагов.");
        Assert.Equal("Просто описание без шагов.", parts.Intro);
        Assert.Empty(parts.Steps);
        Assert.Empty(ProductText.Split(null).Steps);
    }

    [Theory]
    [InlineData("22 кг", 2900, 132, "кг")]
    [InlineData("1 кг", 200, 200, "кг")]
    [InlineData("500 мл триггер", 170, 340, "л")]
    [InlineData("5 л", 750, 150, "л")]
    public void Price_per_kilo_or_litre(string pack, int price, int perUnit, string unit)
    {
        var result = PackSize.PerUnit(pack, price)!.Value;
        Assert.Equal(perUnit, result.Value);
        Assert.Equal(unit, result.Unit);
    }

    [Fact]
    public void No_price_no_unit_price()
    {
        Assert.Null(PackSize.PerUnit("5 кг", null));
        Assert.Null(PackSize.PerUnit("канистра", 100m));
    }
}
