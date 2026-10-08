using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Поиск по словам и исправление опечаток, раскладки и латиницы.</summary>
public class SearchSpellingTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    private static readonly IReadOnlyDictionary<string, int> Words = new[]
        {
            "Шампунь для бесконтактной мойки", "Шампунь для ручной мойки", "Автошампунь-воск", "Полироль для шин TYRE BLACK, концентрат",
            "Очиститель салона ALL-CLEANER", "Воск горячий", "Ifoam",
            "Автошампунь SHAMPOO", "SHAMPOO концентрат", "SHAMPOO для мойки",
        }
        .SelectMany(SearchSpelling.Tokenize).Where(w => w.Length >= 3)
        .GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());

    [Theory]
    [InlineData("шампнь", "шампунь")]
    [InlineData("ифвгеуым", null)]
    [InlineData("ifvgeym", "шампунь")]
    [InlineData("ifvgeym lkz vjqrb", "шампунь для мойки")]
    [InlineData("полироль iby", "полироль шин")]
    [InlineData("polirol", "полирол")]
    [InlineData("моики", "мойки")]
    [InlineData("очистеть салона", "очистит салона")]
    [InlineData("шампнь бесконтктной", "шампунь бесконтактной")]
    [InlineData("тайр", null)]
    [InlineData("шампунь", null)]
    [InlineData("zzzzzzzz", null)]
    public void Query_is_corrected_only_towards_words_from_the_catalog(string query, string? expected) =>
        Assert.Equal(expected, SearchSpelling.Correct(query, Words));

    [Fact]
    public void Layout_and_transliteration_both_ways()
    {
        Assert.Equal("шампунь", SearchSpelling.SwitchLayout("ifvgeym"));
        Assert.Equal("ifvgeym", SearchSpelling.SwitchLayout("шампунь"));
        Assert.Equal("shampun", SearchSpelling.Transliterate("шампунь"));
        Assert.Equal(1, SearchSpelling.Distance("шампнуь", "шампунь"));
    }

    private void Seed()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
        if (db.Products.Any(p => p.Name == "Шампунь для бесконтактной мойки"))
        {
            return;
        }
        db.Products.Add(new Product
        {
            Category = TestDb.NewCategory("Ifoam", "Автошампуни"), Name = "Шампунь для бесконтактной мойки",
            Variants = [new ProductVariant { Article = "777001", Price = 1500m, StockQty = 3 }],
        });
        db.SaveChanges();
    }

    [Fact]
    public async Task Search_matches_each_word_separately()
    {
        Seed();
        var page = await Page(factory.Browser(), "/search?q=" + Uri.EscapeDataString("шампунь мойки"));
        Assert.Contains("Шампунь для бесконтактной мойки", page);
        Assert.DoesNotContain("ничего не нашлось", page);
        Assert.Contains("Шампунь для бесконтактной мойки", await Page(factory.Browser(), "/search?q=777001"));
    }

    [Fact]
    public async Task Wrong_layout_and_typos_still_find_the_product()
    {
        Seed();
        var layout = await Page(factory.Browser(), "/search?q=ifvgeym");
        Assert.Contains("Показаны результаты по запросу <b>«шампунь»</b>", layout);
        Assert.Contains("Шампунь для бесконтактной мойки", layout);

        var typo = await Page(factory.Browser(), "/search?q=" + Uri.EscapeDataString("шампнь бесконтктной"));
        Assert.Contains("Шампунь для бесконтактной мойки", typo);
        Assert.Contains("<b>«шампунь бесконтактной»</b>", typo);

        using var json = JsonDocument.Parse(await factory.Browser().GetStringAsync("/search/suggest?q=" + Uri.EscapeDataString("шампнь")));
        Assert.Equal("Шампунь для бесконтактной мойки", json.RootElement[0].GetProperty("name").GetString());
    }
}



