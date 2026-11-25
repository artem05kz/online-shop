using OnlineShop.Web.Services;

namespace OnlineShop.Tests;

/// <summary>Фасовки из 1С раскладываются по группам для кнопок на странице раздела.</summary>
public class PackSizeTests
{
    [Theory]
    [InlineData("500 мл триггер", PackSize.Spray)]
    [InlineData("500мл триггер", PackSize.Spray)]
    [InlineData("1 кг", PackSize.Small)]
    [InlineData("1 л", PackSize.Small)]
    [InlineData("5,5 кг", PackSize.Medium)]
    [InlineData("6 кг", PackSize.Medium)]
    [InlineData("21 л", PackSize.Large)]
    [InlineData("23", PackSize.Large)]
    public void Pack_goes_to_its_group(string label, string group)
    {
        Assert.Equal(group, PackSize.Parse(label)!.Value.Group);
    }

    [Fact]
    public void Unreadable_pack_has_no_group()
    {
        Assert.Null(PackSize.Parse("большая канистра"));
        Assert.Null(PackSize.Parse(null));
    }

    [Fact]
    public void Label_shows_the_real_range_in_the_section()
    {
        var packs = new[] { "5 кг", "5,5 кг", "6 кг", "1 кг", "1 л", "500 мл триггер", "20 кг", "26 кг" }
            .Select(l => PackSize.Parse(l)!.Value).ToList();
        Assert.Equal("5–6 кг", PackSize.Label(PackSize.Medium, packs).Replace(" ", " "));
        Assert.Equal("1 кг/л", PackSize.Label(PackSize.Small, packs).Replace(" ", " "));
        Assert.Equal("20–26 кг", PackSize.Label(PackSize.Large, packs).Replace(" ", " "));
        Assert.Equal("Триггер 500 мл", PackSize.Label(PackSize.Spray, packs).Replace(" ", " "));
    }
}
