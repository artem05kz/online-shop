using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Tests;

/// <summary>Экранная типографика: висячие предлоги и число с единицей склеиваются неразрывным пробелом.</summary>
public class TypoTests
{
    private const string N = " ";

    [Theory]
    [InlineData("Автошампуни для моек самообслуживания", "Автошампуни для" + N + "моек самообслуживания")]
    [InlineData("Стёкла и зеркала без разводов", "Стёкла и" + N + "зеркала без" + N + "разводов")]
    [InlineData("Блеск и в дождь", "Блеск и" + N + "в" + N + "дождь")]
    [InlineData("Канистра 5 кг", "Канистра 5" + N + "кг")]
    [InlineData("Будет ли блеск", "Будет" + N + "ли блеск")]
    [InlineData("Шампунь Тест", "Шампунь Тест")]
    [InlineData("Холодный воск  \"TOP WAX\"", "Холодный воск «TOP WAX»")]
    public void Short_words_stick_to_the_next_word(string input, string expected)
    {
        Assert.Equal(expected, Typo.Text(input));
    }

    [Fact]
    public void Empty_text_stays_empty()
    {
        Assert.Equal("", Typo.Text(null));
        Assert.Equal("", Typo.Text(""));
    }
}
