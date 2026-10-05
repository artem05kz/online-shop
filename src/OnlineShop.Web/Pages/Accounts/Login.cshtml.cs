using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Accounts;

public class LoginModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    public class InputModel
    {
        [Display(Name = "Логин")]
        [Required(ErrorMessage = "Обязательное поле.")]
        public string UserName { get; set; } = "";

        [Display(Name = "Пароль")]
        [Required(ErrorMessage = "Обязательное поле.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    // Куда вернуть после входа: так покупатель, которого увело на вход из корзины, сразу попадает к оформлению.
    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public string? Error { get; private set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        // Вход запоминается на две недели, как в Django-версии, и не блокируется после ошибок.
        var result = await signInManager.PasswordSignInAsync(
            Input.UserName.Trim(), Input.Password, isPersistent: true, lockoutOnFailure: false);
        if (!result.Succeeded)
        {
            Error = "Неверный логин или пароль.";
            return Page();
        }
        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToAction("Home", "Catalog");
    }
}
