using Microsoft.AspNetCore.Identity;

namespace OnlineShop.Web.Infrastructure;

/// <summary>Сообщения Identity по-русски: по умолчанию они на английском и попали бы покупателю в форму.</summary>
public class RussianIdentityErrors : IdentityErrorDescriber
{
    public override IdentityError DefaultError() =>
        new() { Code = nameof(DefaultError), Description = "Что-то пошло не так, попробуйте ещё раз." };

    public override IdentityError DuplicateUserName(string userName) =>
        new() { Code = nameof(DuplicateUserName), Description = "Пользователь с таким логином уже существует." };

    public override IdentityError InvalidUserName(string? userName) =>
        new() { Code = nameof(InvalidUserName), Description = "Логин может содержать только буквы, цифры и символы @ . + - _." };

    public override IdentityError InvalidEmail(string? email) =>
        new() { Code = nameof(InvalidEmail), Description = "Введите правильный адрес электронной почты." };

    public override IdentityError PasswordTooShort(int length) =>
        new() { Code = nameof(PasswordTooShort), Description = $"Пароль слишком короткий: нужно не меньше {length} символов." };

    public override IdentityError PasswordMismatch() =>
        new() { Code = nameof(PasswordMismatch), Description = "Неверный пароль." };
}
