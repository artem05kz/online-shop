using System.Net;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Ошибки показываются страницей сайта с правильным кодом, а /health сообщает, жив ли сайт.</summary>
public class ErrorPagesTests(TempShopFactory factory) : IClassFixture<TempShopFactory>
{
    [Fact]
    public async Task Health_answers_ok_when_database_is_reachable()
    {
        var response = await factory.Browser().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("ok", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Missing_page_keeps_404_and_own_text()
    {
        var response = await factory.Browser().GetAsync("/no/such/page");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Contains("Такой страницы нет", await Text(response));
    }

    [Fact]
    public async Task Stale_form_gets_400_with_explanation_not_404()
    {
        // Форма без токена: так бывает, если страницу держали открытой, пока сайт обновляли.
        var response = await Post(factory.Browser(), "/orders/cart/add/1", "устаревший-токен");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Не получилось отправить форму", await Text(response));
    }
}
