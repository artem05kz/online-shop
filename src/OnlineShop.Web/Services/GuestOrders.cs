using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services;

/// <summary>
/// Заказы, которые гость оформил без регистрации в этом браузере. Их секретные метки лежат в куке,
/// и если гость потом зарегистрируется или войдёт, эти заказы переходят в его «Мои заказы».
/// Так обещание на карточке «Заказ принят» («зарегистрируйтесь, и заказ будет в личном кабинете») правда.
/// </summary>
public class GuestOrders(IHttpContextAccessor httpContextAccessor, ShopDbContext db)
{
    public const string CookieName = "shop.orders";

    // Больше меток не храним: кука маленькая, а столько заказов без аккаунта почти не бывает.
    private const int MaxTokens = 20;

    private HttpContext Context => httpContextAccessor.HttpContext!;

    private List<string> Tokens() =>
        (Context.Request.Cookies[CookieName] ?? "")
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length == 32 && t.All(Uri.IsHexDigit))
            .ToList();

    /// <summary>Запомнить заказ гостя в этом браузере.</summary>
    public void Remember(Order order)
    {
        var tokens = Tokens();
        tokens.Remove(order.AccessToken);
        tokens.Insert(0, order.AccessToken);
        Context.Response.Cookies.Append(CookieName, string.Join('.', tokens.Take(MaxTokens)), new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            // Кука нужна, чтобы отдать покупателю его же заказы, а не для слежки.
            IsEssential = true,
            Expires = DateTimeOffset.UtcNow.AddDays(180),
        });
    }

    /// <summary>После регистрации или входа: заказы гостя из этого браузера переходят покупателю.</summary>
    public async Task ClaimAsync(int userId)
    {
        var tokens = Tokens();
        if (tokens.Count == 0)
        {
            return;
        }
        var orders = await db.Orders.Where(o => o.UserId == null && tokens.Contains(o.AccessToken)).ToListAsync();
        foreach (var order in orders)
        {
            order.UserId = userId;
        }
        await db.SaveChangesAsync();
        Context.Response.Cookies.Delete(CookieName);
    }
}
