using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services.Email;

namespace OnlineShop.Web.Pages.Accounts;

/// <summary>Забыли пароль: на подтверждённую почту уходит код, с ним пароль меняется на странице ResetPassword.</summary>
public class ForgotPasswordModel(AccountMail mail) : PageModel
{
    [BindProperty]
    [Display(Name = "Почта, указанная в аккаунте")]
    [Required(ErrorMessage = "Укажите почту.")]
    [EmailAddress(ErrorMessage = "Введите правильный адрес электронной почты.")]
    public string? Email { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        if (await mail.FindConfirmedAsync(Email!) is { } user)
        {
            await mail.SendResetCodeAsync(user);
        }
        // Ответ одинаковый, есть такая почта или нет: иначе по этой форме можно было бы проверять,
        // кто из знакомых зарегистрирован в магазине.
        TempData.Flash("Если эта почта есть в аккаунте и подтверждена, мы отправили на неё код. Письмо приходит за минуту, проверьте и папку «Спам».", FlashLevel.Info);
        return RedirectToPage("ResetPassword", new { email = Email!.Trim() });
    }
}
