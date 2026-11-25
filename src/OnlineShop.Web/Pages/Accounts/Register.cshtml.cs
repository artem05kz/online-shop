using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services;
using OnlineShop.Web.Services.Email;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Accounts;

public class RegisterModel(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, Favorites favorites, Cart cart, GuestOrders guestOrders, AccountMail mail) : PageModel
{
    public class InputModel
    {
        [Display(Name = "Логин")]
        [Required(ErrorMessage = "Обязательное поле.")]
        [StringLength(150, ErrorMessage = "Не длиннее {1} символов.")]
        [RegularExpression(@"^[\w.@+-]+$", ErrorMessage = "Логин может содержать только буквы, цифры и символы @ . + - _.")]
        public string UserName { get; set; } = "";

        [Display(Name = "Электронная почта")]
        [Required(ErrorMessage = "Укажите почту: на неё придут статусы заказов и код, если забудете пароль.")]
        [EmailAddress(ErrorMessage = "Введите правильный адрес электронной почты.")]
        [StringLength(254)]
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

        public bool AcceptPrivacy { get; set; }
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

        var email = Input.Email!.Trim();
        var user = new ApplicationUser { UserName = Input.UserName.Trim(), Email = email, PersonalDataConsentAt = DateTime.UtcNow };

        // Identity при создании останавливается на первой неудаче: при плохом пароле про занятый
        // логин покупатель узнал бы только со второй попытки. Проверяем всё заранее и показываем разом.
        var errors = new List<IdentityError>();
        if (await userManager.FindByNameAsync(user.UserName) is not null)
        {
            errors.Add(userManager.ErrorDescriber.DuplicateUserName(user.UserName));
        }
        if (await mail.IsTakenAsync(email))
        {
            ModelState.AddModelError("Input.Email", "Эта почта уже привязана к другому аккаунту. Если он ваш, войдите или восстановите пароль.");
        }
        foreach (var validator in userManager.PasswordValidators)
        {
            errors.AddRange((await validator.ValidateAsync(userManager, user, Input.Password)).Errors);
        }
        // Галочку проверяем вместе с логином и паролем, чтобы все ошибки были видны разом.
        if (!Input.AcceptPrivacy)
        {
            ModelState.AddModelError("Input.AcceptPrivacy", PrivacyConsent.Required);
        }
        if (errors.Count == 0 && ModelState.IsValid && Input.AcceptPrivacy)
        {
            errors.AddRange((await userManager.CreateAsync(user, Input.Password)).Errors);
        }
        if (errors.Count > 0 || !ModelState.IsValid)
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
        await guestOrders.ClaimAsync(user.Id);
        await mail.SendConfirmationCodeAsync(user);
        TempData.Flash($"Аккаунт создан. Отправили код на {email}: подтвердите почту, чтобы получать статусы заказов.", FlashLevel.Info);
        return RedirectToPage("Email", new { returnUrl = Url.IsLocalUrl(ReturnUrl) ? ReturnUrl : null });
    }
}
