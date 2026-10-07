using OnlineShop.Web.Services;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Accounts;

public class RegisterModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, Favorites favorites, Cart cart) : PageModel
{
    public class InputModel
    {
        [Display(Name = "Логин")]
        [Required(ErrorMessage = "Обязательное поле.")]
        [StringLength(150, ErrorMessage = "Не длиннее {1} символов.")]
        [RegularExpression(@"^[\w.@+-]+$", ErrorMessage = "Логин может содержать только буквы, цифры и символы @ . + - _.")]
        public string UserName { get; set; } = "";

        [Display(Name = "Электронная почта")]
        [EmailAddress(ErrorMessage = "Введите правильный адрес электронной почты.")]
        [StringLength(254, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Email { get; set; }

        [Display(Name = "Пароль")]
        [Required(ErrorMessage = "Обязательное поле.")]
        [DataType(DataType.Password)]
        public string Password { get; set; } = "";

        [Display(Name = "Подтверждение пароля")]
        [Required(ErrorMessage = "Обязательное поле.")]
        [DataType(DataType.Password)]
        [Compare(nameof(Password), ErrorMessage = "Пароли не совпадают.")]
        public string ConfirmPassword { get; set; } = "";
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    [BindProperty(SupportsGet = true)]
    public string? ReturnUrl { get; set; }

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            return Page();
        }

        var email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim();
        var user = new ApplicationUser { UserName = Input.UserName.Trim(), Email = email };

        // Identity при создании останавливается на первой неудаче: при плохом пароле про занятый
        // логин покупатель узнал бы только со второй попытки. Проверяем всё заранее и показываем разом.
        var errors = new List<IdentityError>();
        if (await userManager.FindByNameAsync(user.UserName) is not null)
        {
            errors.Add(userManager.ErrorDescriber.DuplicateUserName(user.UserName));
        }
        foreach (var validator in userManager.PasswordValidators)
        {
            errors.AddRange((await validator.ValidateAsync(userManager, user, Input.Password)).Errors);
        }
        if (errors.Count == 0)
        {
            errors.AddRange((await userManager.CreateAsync(user, Input.Password)).Errors);
        }
        if (errors.Count > 0)
        {
            foreach (var error in errors)
            {
                // Каждую ошибку показываем у того поля, к которому она относится.
                var field = error.Code.StartsWith("Password") ? "Input.Password"
                    : error.Code.Contains("Email") ? "Input.Email"
                    : "Input.UserName";
                ModelState.AddModelError(field, error.Description);
            }
            return Page();
        }

        // Сразу после регистрации покупатель уже вошёл: ещё раз вводить пароль не нужно.
        await signInManager.SignInAsync(user, isPersistent: true);
        await favorites.MergeGuestAsync(user.Id);
        await cart.MergeGuestAsync(user.Id);
        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToAction("Home", "Catalog");
    }
}
