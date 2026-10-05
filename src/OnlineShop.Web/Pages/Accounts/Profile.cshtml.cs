using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Accounts;

/// <summary>
/// Вкладка «Личные данные». Адреса и заказы на своих вкладках, чтобы каждый экран
/// оставался коротким и читался с телефона.
/// </summary>
[Authorize]
public class ProfileModel(UserManager<ApplicationUser> userManager) : PageModel
{
    public class InputModel
    {
        [Display(Name = "Имя")]
        [StringLength(150, ErrorMessage = "Не длиннее {1} символов.")]
        public string? FirstName { get; set; }

        [Display(Name = "Фамилия")]
        [StringLength(150, ErrorMessage = "Не длиннее {1} символов.")]
        public string? LastName { get; set; }

        [Display(Name = "Электронная почта")]
        [EmailAddress(ErrorMessage = "Введите правильный адрес электронной почты.")]
        [StringLength(254, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Email { get; set; }
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public string UserName { get; private set; } = "";

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await CurrentUser();
        UserName = user.UserName ?? "";
        Input = new InputModel { FirstName = user.FirstName, LastName = user.LastName, Email = user.Email };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await CurrentUser();
        UserName = user.UserName ?? "";
        if (!ModelState.IsValid)
        {
            return Page();
        }

        user.FirstName = Input.FirstName?.Trim() ?? "";
        user.LastName = Input.LastName?.Trim() ?? "";
        user.Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        var result = await userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors)
            {
                ModelState.AddModelError("Input.Email", error.Description);
            }
            return Page();
        }

        TempData.Flash("Данные сохранены.");
        return RedirectToPage();
    }

    private async Task<ApplicationUser> CurrentUser() =>
        await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("Покупатель не найден.");
}
