using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.WebEncoders;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

var builder = WebApplication.CreateBuilder(args);

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
        // заглавных, цифр и спецсимволов.
        options.Password.RequiredLength = 8;
        options.Password.RequireDigit = false;
        options.Password.RequireUppercase = false;
        options.Password.RequireLowercase = false;
        options.Password.RequireNonAlphanumeric = false;
        options.User.RequireUniqueEmail = false;
    })
    .AddEntityFrameworkStores<ShopDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/accounts/login";
    options.AccessDeniedPath = "/accounts/login";
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

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<Cart>();
builder.Services.AddScoped<StorefrontLayout>();

builder.Services.AddControllersWithViews(options =>
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddRazorPages();

var app = builder.Build();

// Схему базы обновляем при старте, чтобы после git pull не нужно было вручную запускать миграции.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShopDbContext>();
    db.Database.Migrate();

    // dotnet run --project src/OnlineShop.Web -- --seed-demo
    if (args.Contains("--seed-demo"))
    {
        Console.WriteLine(await DemoData.SeedAsync(db, mediaRoot));
        return;
    }
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
