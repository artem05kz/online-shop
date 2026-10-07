using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services.Email;

namespace OnlineShop.Web.Pages.Accounts;

/// <summary>
/// Почта покупателя и её подтверждение кодом из письма. На подтверждённую почту приходят статусы
/// заказов и код смены пароля, поэтому без неё заказ не оформить.
/// </summary>
[Authorize]
public class EmailModel(UserManager<ApplicationUser> users, SignInManager<ApplicationUser> signIn, AccountMail mail) : PageModel
{
    public const string PageUrl = "/accounts/email";

    [BindProperty]
    [Display(Name = "Электронная почта")]
    [Required(ErrorMessage = "Укажите почту.")]
    [EmailAddress(ErrorMessage = "Введите правильный адрес электронной почты.")]
    [StringLength(254)]
    public string? NewEmail { get; set; }

    [BindProperty]
    [Display(Name = "Код из письма")]
    public string? Code { get; set; }

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? CurrentEmail { get; private set; }
    public bool IsConfirmed { get; private set; }

    public async Task OnGetAsync() => await LoadAsync();

    /// <summary>Указать или сменить почту: адрес сохраняется неподтверждённым, на него уходит код.</summary>
    public async Task<IActionResult> OnPostSendAsync()
    {
        var user = await LoadAsync();
        ModelState.Remove(nameof(Code));
        if (ModelState.IsValid && await mail.IsTakenAsync(NewEmail!, user.Id))
        {
            ModelState.AddModelError(nameof(NewEmail), "Эта почта уже привязана к другому аккаунту.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        if (!string.Equals(user.Email, NewEmail!.Trim(), StringComparison.OrdinalIgnoreCase) || !user.EmailConfirmed)
        {
            await users.SetEmailAsync(user, NewEmail.Trim());
            // Смена почты меняет отметку безопасности аккаунта: обновляем вход, иначе через полчаса
            // проверка отметки выкинула бы покупателя из аккаунта.
            await signIn.RefreshSignInAsync(user);
            await mail.SendConfirmationCodeAsync(user);
            TempData.Flash($"Отправили код на {user.Email}. Письмо приходит за минуту, проверьте и папку «Спам».", FlashLevel.Info);
        }
        return RedirectToPage(new { ReturnUrl });
    }

    public async Task<IActionResult> OnPostResendAsync()
    {
        var user = await LoadAsync();
        if (user.Email is not null && !user.EmailConfirmed)
        {
            await mail.SendConfirmationCodeAsync(user);
            TempData.Flash($"Отправили новый код на {user.Email}.", FlashLevel.Info);
        }
        return RedirectToPage(new { ReturnUrl });
    }

    public async Task<IActionResult> OnPostConfirmAsync()
    {
        var user = await LoadAsync();
        ModelState.Clear();
        var code = new string((Code ?? "").Where(char.IsAsciiDigit).ToArray());
        if (user.Email is null || code.Length == 0 || !(await users.ConfirmEmailAsync(user, code)).Succeeded)
        {
            ModelState.AddModelError(nameof(Code), $"Код не подошёл. Проверьте цифры или отправьте новый код: каждый действует {MailTemplates.CodeLifetime}.");
            return Page();
        }
        TempData.Flash("Почта подтверждена. Сюда будут приходить статусы заказов.");
        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToPage("Profile");
    }

    private async Task<ApplicationUser> LoadAsync()
    {
        var user = await users.GetUserAsync(User) ?? throw new InvalidOperationException("Покупатель не найден.");
        CurrentEmail = user.Email;
        IsConfirmed = user.EmailConfirmed;
        NewEmail ??= user.Email;
        return user;
    }
}
