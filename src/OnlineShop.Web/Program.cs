using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApplicationModels;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.WebEncoders;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web;
using OnlineShop.Web.Services;
using OnlineShop.Web.Services.Import;
using OnlineShop.Web.Services.Telegram;

var builder = WebApplication.CreateBuilder(args);

// Токен бота Telegram на машине разработчика хранится в user-secrets, вне репозитория. В режиме
// Development их подключает сам ASP.NET, но конфигурация Claude Code запускает сайт без профиля,
// то есть в Production. На сервере файла секретов нет, там токен задаётся переменной окружения.
if (!builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>(optional: true);
}

// Относительный путь к файлу базы считаем от папки проекта, а не от текущей папки консоли,
// иначе при запуске из корня репозитория и из src/ создавались бы две разные базы.
var connection = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Shop"));
if (!Path.IsPathRooted(connection.DataSource))
{
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
}
Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);

// Папка загрузок: фото товаров и баннеров. Лежит вне wwwroot, чтобы не смешивать загруженное с кодом сайта.
var mediaRoot = Path.Combine(builder.Environment.ContentRootPath, builder.Configuration["Media:Root"] ?? "media");
Directory.CreateDirectory(mediaRoot);

builder.Services.AddDbContext<ShopDbContext>(options => options
    .UseSqlite(connection.ToString())
    .AddInterceptors(new SqliteFunctions.Interceptor()));

builder.Services
    .AddIdentity<ApplicationUser, IdentityRole<int>>(options =>
    {
        // Требования к паролю как в Django-версии: не короче 8 символов, без обязательных
        // заглавных, цифр и спецсимволов. Остальные проверки в PasswordRules.
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = false;
        // Логин может быть и кириллицей. Допустимые символы проверяет форма регистрации.
        options.User.AllowedUserNameCharacters = "";
    })
    .AddEntityFrameworkStores<ShopDbContext>()
    .AddErrorDescriber<RussianIdentityErrors>()
    .AddPasswordValidator<PasswordRules>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = ShopUrls.Login;
    // Вошёл, но не менеджер: объясняем, что доступа нет, а не просим войти ещё раз.
    options.AccessDeniedPath = "/accounts/denied";
    // Вход живёт две недели, как сессии в Django-версии.
    options.ExpireTimeSpan = TimeSpan.FromDays(14);
});

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.Cookie.Name = "shop.session";
    options.Cookie.HttpOnly = true;
    // Без сессии не работает корзина, поэтому кука нужна сайту для работы, а не для аналитики.
    options.Cookie.IsEssential = true;
    options.IdleTimeout = TimeSpan.FromDays(14);
});

// По умолчанию Razor выводит всё, кроме латиницы, кодами вида &#x428;. Браузер это показывает,
// но исходный код страниц нечитаем и тяжелее, поэтому кириллицу и знак тенге выводим как есть.
builder.Services.Configure<WebEncoderOptions>(options =>
    options.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));

builder.Services.AddAuthorization(options =>
    options.AddPolicy(RoleNames.Manager, policy => policy.RequireRole(RoleNames.Manager)));

builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton(new MediaStorage(mediaRoot));
builder.Services.AddSingleton<Thumbnails>();
builder.Services.AddSingleton(new ImportUploads(Path.Combine(Path.GetDirectoryName(connection.DataSource)!, "imports")));
builder.Services.AddScoped<ImportApplier>();
builder.Services.AddScoped<Cart>();
builder.Services.AddScoped<StorefrontLayout>();

// Уведомления о заказах в Telegram. Без токена бот не запускается, остальной сайт работает как обычно.
builder.Services.AddSingleton(sp => TelegramOptions.From(sp.GetRequiredService<IConfiguration>()));
builder.Services.AddSingleton<ITelegramApi, TelegramApi>();
builder.Services.AddSingleton<OrderAlerts>();
builder.Services.AddSingleton<TelegramBot>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelegramBot>());
builder.Services.AddScoped<TelegramLinker>();
builder.Services.AddScoped<TelegramNotifier>();

builder.Services.AddControllersWithViews(options =>
        options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    // В ответах JSON (подсказки поиска) кириллица как есть, а не кодами Ш: вдвое короче.
    .AddJsonOptions(options => options.JsonSerializerOptions.Encoder = JavaScriptEncoder.Create(UnicodeRanges.All));
// Скрытое поле «false» у галочки ставим рядом с ней, а не в конец формы: строки таблиц в панели
// копируются скриптом, и без этого у новой строки снятая галочка читалась бы как включённая.
builder.Services.Configure<MvcViewOptions>(options =>
    options.HtmlHelperOptions.CheckBoxHiddenInputRenderMode = CheckBoxHiddenInputRenderMode.Inline);
builder.Services.AddRazorPages(options =>
{
    // Вся панель управления только для менеджеров.
    options.Conventions.AuthorizeFolder("/Admin", RoleNames.Manager);

    // У форм два адреса: новая запись (/add) и правка существующей по номеру. Адрес /add не участвует
    // в построении ссылок, иначе ссылка «Изменить» превращалась бы в /add?id=5.
    void AddCreateRoute(string page, string template) =>
        options.Conventions.AddPageRouteModelConvention(page, model => model.Selectors.Add(new SelectorModel
        {
            AttributeRouteModel = new AttributeRouteModel { Template = template, SuppressLinkGeneration = true },
        }));
    AddCreateRoute("/Accounts/AddressEdit", "/accounts/profile/addresses/add");
    foreach (var section in new[] { "Brands", "Categories", "Products", "Variants", "Banners", "Stores", "Contacts" })
    {
        AddCreateRoute($"/Admin/{section}/Edit", $"/admin/{section.ToLowerInvariant()}/add");
    }
});

var app = builder.Build();

// Схему базы обновляем при старте, чтобы после git pull не нужно было вручную запускать миграции.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.Migrate();
}

// Консольные команды (демо-данные, выдача доступа, импорт из 1С) вместо запуска сайта, см. Cli.cs.
if (await Cli.RunAsync(args, app.Services))
{
    return;
}

var russian = new CultureInfo("ru-RU");
app.UseRequestLocalization(new RequestLocalizationOptions
{
    DefaultRequestCulture = new RequestCulture(russian),
    SupportedCultures = [russian],
    SupportedUICultures = [russian],
});

app.UseStatusCodePagesWithReExecute("/not-found");
app.UseStaticFiles();
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(mediaRoot),
    RequestPath = Media.RequestPath,
});

app.UseRouting();
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapRazorPages();

app.Run();

// Нужен интеграционным тестам: WebApplicationFactory<Program> поднимает приложение целиком.
public partial class Program;
