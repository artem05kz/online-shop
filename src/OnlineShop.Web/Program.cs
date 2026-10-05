using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Models;

var builder = WebApplication.CreateBuilder(args);

// Относительный путь к файлу базы считаем от папки проекта, а не от текущей папки консоли,
// иначе при запуске из корня репозитория и из src/ создавались бы две разные базы.
var connection = new SqliteConnectionStringBuilder(builder.Configuration.GetConnectionString("Shop"));
if (!Path.IsPathRooted(connection.DataSource))
{
    connection.DataSource = Path.Combine(builder.Environment.ContentRootPath, connection.DataSource);
}
Directory.CreateDirectory(Path.GetDirectoryName(connection.DataSource)!);

builder.Services.AddDbContext<ShopDbContext>(options => options.UseSqlite(connection.ToString()));

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

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

// Схему базы обновляем при старте, чтобы после git pull не нужно было вручную запускать миграции.
using (var scope = app.Services.CreateScope())
{
    scope.ServiceProvider.GetRequiredService<ShopDbContext>().Database.Migrate();
}

app.UseStaticFiles();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute("default", "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

// Временная заглушка до переноса витрины: показывает, что база подключена и что в ней лежит.
app.MapGet("/", async (ShopDbContext db) => Results.Text(
    $"База подключена. Брендов: {await db.Brands.CountAsync()}, товаров: {await db.Products.CountAsync()}, "
        + $"артикулов: {await db.ProductVariants.CountAsync()}, заказов: {await db.Orders.CountAsync()}.\n"
        + "Витрина появится на следующем шаге переноса.",
    "text/plain; charset=utf-8"));

app.Run();
