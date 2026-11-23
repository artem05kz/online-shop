using OnlineShop.Web.Services;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Accounts;

public class LoginModel(SignInManager<ApplicationUser> signInManager, Favorites favorites, Cart cart, GuestOrders guestOrders) : PageModel
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

        // Вход запоминается на две недели, как в Django-версии. Неверные пароли считаются:
        // после пяти подряд аккаунт закрывается на 15 минут (настройки Lockout в Program.cs).
        var result = await signInManager.PasswordSignInAsync(
            Input.UserName.Trim(), Input.Password, isPersistent: true, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            Error = "Слишком много неверных паролей подряд. Вход в этот аккаунт закрыт на 15 минут.";
            return Page();
        }
        if (!result.Succeeded)
        {
            Error = "Неверный логин или пароль.";
            return Page();
        }
        if (await signInManager.UserManager.FindByNameAsync(Input.UserName.Trim()) is { } user)
        {
            await favorites.MergeGuestAsync(user.Id);
            await cart.MergeGuestAsync(user.Id);
            await guestOrders.ClaimAsync(user.Id);
        }
        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToAction("Home", "Catalog");
    }
}
