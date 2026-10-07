using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Accounts;

/// <summary>
/// Выход только POST-запросом с токеном формы: иначе выйти «за покупателя» можно было бы
/// чужой ссылкой или картинкой на любом сайте.
/// </summary>
public class LogoutModel(SignInManager<ApplicationUser> signInManager, Cart cart) : PageModel
{
    public IActionResult OnGet() => RedirectToAction("Home", "Catalog");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        // Корзина покупателя остаётся в базе до следующего входа, а браузер начинает с пустой:
        // на общем компьютере следующий человек не увидит чужие товары.
        cart.ForgetGuest();
        return RedirectToAction("Home", "Catalog");
    }
}
