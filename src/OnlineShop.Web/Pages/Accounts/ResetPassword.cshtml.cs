using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services.Email;

namespace OnlineShop.Web.Pages.Accounts;

/// <summary>
/// Новый пароль по коду из письма. Неверные коды считаются вместе с неверными паролями:
/// после пяти подряд аккаунт закрывается на 15 минут, перебирать шестизначный код бессмысленно.
/// </summary>
public class ResetPasswordModel(UserManager<ApplicationUser> users, AccountMail mail) : PageModel
{
    public class InputModel
    {
        [Display(Name = "Почта")]
        [Required(ErrorMessage = "Укажите почту.")]
        [EmailAddress(ErrorMessage = "Введите правильный адрес электронной почты.")]
        public string Email { get; set; } = "";

        [Display(Name = "Код из письма")]
        [Required(ErrorMessage = "Введите код из письма.")]
        public string Code { get; set; } = "";

        [Display(Name = "Новый пароль")]
        [Required(ErrorMessage = "Обязательное поле.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "Новый пароль ещё раз")]
        [Required(ErrorMessage = "Обязательное поле.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают.")]
        public string ConfirmPassword { get; set; } = "";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public void OnGet(string? email) => Input.Email = email ?? "";

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }
        const string wrongCode = "Код не подошёл. Проверьте цифры или запросите новый код.";
        var user = await mail.FindConfirmedAsync(Input.Email);
        if (user is null)
        {
            ModelState.AddModelError("Input.Code", wrongCode);
            return Page();
        }
        if (await users.IsLockedOutAsync(user))
        {
            ModelState.AddModelError("Input.Code", "Слишком много неверных попыток. Попробуйте через 15 минут.");
            return Page();
        }

        var code = new string(Input.Code.Where(char.IsAsciiDigit).ToArray());
        var result = await users.ResetPasswordAsync(user, code, Input.Password);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                await users.AccessFailedAsync(user);
                ModelState.AddModelError("Input.Code", wrongCode);
            }
            foreach (var error in result.Errors.Where(e => e.Code != nameof(IdentityErrorDescriber.InvalidToken)))
            {
                ModelState.AddModelError("Input.Password", error.Description);
            }
            return Page();
        }

        await users.ResetAccessFailedCountAsync(user);
        await users.SetLockoutEndDateAsync(user, null);
        TempData.Flash("Пароль изменён. Войдите с новым паролем.");
        return RedirectToPage("Login");
    }
}
