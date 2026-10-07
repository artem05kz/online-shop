using System.Net;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using OnlineShop.Web.Models;
using static OnlineShop.Tests.TestSite;

namespace OnlineShop.Tests;

/// <summary>Защита входа: блокировка после неверных паролей, ограничение попыток, стойкость хеша.</summary>
public class LoginSecurityTests
{
    private const string Password = "верный-пароль-1";

    private static async Task<string> CreateUser(TempShopFactory site, string login)
    {
        using var scope = site.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.True((await users.CreateAsync(new ApplicationUser { UserName = login }, Password)).Succeeded);
        return login;
    }

    private static async Task<HttpResponseMessage> TryLogin(HttpClient client, string login, string password)
    {
        var token = await TokenFrom(client, "/accounts/login");
        return await Post(client, "/accounts/login", token, ("Input.UserName", login), ("Input.Password", password));
    }

    [Fact]
    public async Task Five_wrong_passwords_lock_the_account_even_for_the_right_one()
    {
        using var site = new TempShopFactory();
        var login = await CreateUser(site, "блокировка");
        var client = site.Browser();

        for (var i = 0; i < 4; i++)
        {
            Assert.Contains("Неверный логин или пароль", await Text(await TryLogin(client, login, "неверный" + i)));
        }
        Assert.Contains("закрыт на 15 минут", await Text(await TryLogin(client, login, "неверный-5")));

        // Даже верный пароль не пускает, пока блокировка не снята: подбор теряет смысл.
        var right = await TryLogin(client, login, Password);
        Assert.Equal(HttpStatusCode.OK, right.StatusCode);
        Assert.Contains("закрыт на 15 минут", await Text(right));
    }

    [Fact]
    public async Task Too_many_attempts_from_one_address_get_429()
    {
        using var site = new TempShopFactory { AuthAttemptsPerMinute = 3 };
        var client = site.Browser();
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await TryLogin(client, "кто-то", "x" + i)).StatusCode);
        }

        var blocked = await TryLogin(client, "кто-то", "x4");
        Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        Assert.Contains("Слишком много попыток", await Text(blocked));
        // Открывать страницу входа ограничение не мешает.
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/accounts/login")).StatusCode);
    }

    [Fact]
    public async Task Password_hash_uses_210000_iterations_and_old_hashes_are_upgraded_on_login()
    {
        // Без подмены: настоящее число повторов, как на сервере.
        using var site = new TempShopFactory { PasswordHashIterations = null };
        var login = await CreateUser(site, "хеш");
        Assert.Equal(210_000, Iterations(site, login));

        // Пароль со старым числом повторов, как у покупателей, зарегистрированных до этой правки.
        using (var scope = site.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByNameAsync(login))!;
            var old = new PasswordHasher<ApplicationUser>(Microsoft.Extensions.Options.Options.Create(
                new PasswordHasherOptions { IterationCount = 100_000 }));
            user.PasswordHash = old.HashPassword(user, Password);
            await users.UpdateAsync(user);
        }
        Assert.Equal(100_000, Iterations(site, login));

        Assert.Equal(HttpStatusCode.Redirect, (await TryLogin(site.Browser(), login, Password)).StatusCode);
        Assert.Equal(210_000, Iterations(site, login));
    }

    /// <summary>Число повторов из хеша формата v3: байты 5-8 после версии и алгоритма.</summary>
    private static int Iterations(TempShopFactory site, string login)
    {
        using var scope = site.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var hash = Convert.FromBase64String(users.FindByNameAsync(login).Result!.PasswordHash!);
        return (hash[5] << 24) | (hash[6] << 16) | (hash[7] << 8) | hash[8];
    }
}
