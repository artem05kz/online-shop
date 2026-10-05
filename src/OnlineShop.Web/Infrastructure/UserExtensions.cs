using System.Security.Claims;

namespace OnlineShop.Web.Infrastructure;

public static class UserExtensions
{
    /// <summary>Номер вошедшего покупателя. Вызывать только там, где вход обязателен ([Authorize]).</summary>
    public static int ShopUserId(this ClaimsPrincipal user) =>
        int.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Покупатель не вошёл на сайт."));
}
