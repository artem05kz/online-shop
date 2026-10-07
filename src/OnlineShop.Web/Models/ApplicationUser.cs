using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;

namespace OnlineShop.Web.Models;

/// <summary>
/// Покупатель или сотрудник. Логин, почта и пароль хранятся в полях IdentityUser,
/// доступ в панель управления даёт роль <see cref="RoleNames.Manager"/>.
/// </summary>
public class ApplicationUser : IdentityUser<int>
{
    // Имя необязательно: покупателя не надо заставлять его вводить, чтобы смотреть каталог или повторить заказ.
    [Display(Name = "Имя")]
    [StringLength(150)]
    public string FirstName { get; set; } = "";

    [Display(Name = "Фамилия")]
    [StringLength(150)]
    public string LastName { get; set; } = "";

    // Когда покупатель согласился на обработку персональных данных (галочка при регистрации или при
    // первом заказе). По закону согласие нужно уметь подтвердить.
    public DateTime? PersonalDataConsentAt { get; set; }

    public List<Address> Addresses { get; set; } = [];
    public List<Order> Orders { get; set; } = [];

    [NotMapped]
    public string FullName => $"{FirstName} {LastName}".Trim();
}

public static class RoleNames
{
    public const string Manager = "Manager";
}
