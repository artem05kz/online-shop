using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Services.Email;

/// <summary>
/// Почта покупателя: поиск по адресу, коды подтверждения и смены пароля, письма о заказах.
/// Коды шестизначные (поставщик Identity «Email»), привязаны к адресу и паролю: после смены
/// почты или пароля старый код перестаёт подходить.
/// </summary>
public class AccountMail(ShopDbContext db, UserManager<ApplicationUser> users, EmailQueue queue)
{
    /// <summary>Нормализованный адрес для сравнения, как его хранит Identity.</summary>
    public string Normalize(string email) => users.NormalizeEmail(email.Trim()) ?? "";

    /// <summary>
    /// Занят ли адрес другим покупателем. Проверяем сами, а не через RequireUniqueEmail Identity:
    /// у покупателей, зарегистрированных раньше, почты может не быть, и Identity перестал бы их сохранять.
    /// </summary>
    public Task<bool> IsTakenAsync(string email, int? exceptUserId = null)
    {
        var normalized = Normalize(email);
        return db.Users.AnyAsync(u => u.NormalizedEmail == normalized && u.Id != exceptUserId);
    }

    /// <summary>Покупатель с этим подтверждённым адресом. На неподтверждённый адрес коды смены пароля не шлём.</summary>
    public Task<ApplicationUser?> FindConfirmedAsync(string email)
    {
        var normalized = Normalize(email);
        return db.Users.FirstOrDefaultAsync(u => u.NormalizedEmail == normalized && u.EmailConfirmed);
    }

    public async Task SendConfirmationCodeAsync(ApplicationUser user) =>
        queue.Enqueue(MailTemplates.ConfirmCode(user.Email!, await users.GenerateEmailConfirmationTokenAsync(user)));

    public async Task SendResetCodeAsync(ApplicationUser user) =>
        queue.Enqueue(MailTemplates.ResetCode(user.Email!, await users.GeneratePasswordResetTokenAsync(user)));

    public async Task NotifyOrderPlacedAsync(int orderId, string siteUrl)
    {
        if (await LoadOrderAsync(orderId) is { User: { EmailConfirmed: true, Email: { } email } } order)
        {
            queue.Enqueue(MailTemplates.OrderPlaced(email, order, OrderUrl(siteUrl, order.Id)));
        }
    }

    public async Task NotifyStatusChangedAsync(IEnumerable<int> orderIds, string siteUrl)
    {
        foreach (var id in orderIds)
        {
            if (await LoadOrderAsync(id) is { User: { EmailConfirmed: true, Email: { } email } } order
                && MailTemplates.StatusChanged(email, order, OrderUrl(siteUrl, order.Id)) is { } message)
            {
                queue.Enqueue(message);
            }
        }
    }

    private Task<Order?> LoadOrderAsync(int id) =>
        db.Orders.Include(o => o.Items).Include(o => o.User).AsNoTracking().FirstOrDefaultAsync(o => o.Id == id);

    private static string OrderUrl(string siteUrl, int id) => $"{siteUrl.TrimEnd('/')}/orders/my/{id}";
}
