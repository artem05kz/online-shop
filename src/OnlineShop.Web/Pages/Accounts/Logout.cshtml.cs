using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Accounts;

/// <summary>
/// Выход только POST-запросом с токеном формы: иначе выйти «за покупателя» можно было бы
/// чужой ссылкой или картинкой на любом сайте.
/// </summary>
public class LogoutModel(SignInManager<ApplicationUser> signInManager) : PageModel
{
    public IActionResult OnGet() => RedirectToAction("Home", "Catalog");

    public async Task<IActionResult> OnPostAsync()
    {
        await signInManager.SignOutAsync();
        // Как в Django: при выходе сессия очищается вместе с корзиной, чтобы на общем компьютере
        // следующий человек не увидел чужие товары.
        HttpContext.Session.Clear();
        return RedirectToAction("Home", "Catalog");
    }
}
