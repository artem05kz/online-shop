using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Data;

/// <summary>
/// Выдуманный каталог, чтобы витрину было видно до того, как появится импорт из 1С.
/// Названия, цены, адреса и телефоны не настоящие. Заполняет только пустую базу.
/// </summary>
public static class DemoData
{
    public static async Task<string> SeedAsync(ShopDbContext db, string mediaRoot)
    {
        if (await db.Brands.AnyAsync())
        {
            return "В базе уже есть товары, демо-данные не добавлены.";
        }

        var grass = new Brand { Name = "Grass", SortOrder = 1 };
        var ifoam = new Brand { Name = "Ifoam", SortOrder = 2 };

        var autoShampoo = new Category { Brand = grass, Name = "Автошампуни", SortOrder = 1 };
        var household = new Category { Brand = grass, Name = "Бытовая химия", SortOrder = 2 };
        var cleaning = new Category { Brand = grass, Name = "Профессиональный клининг", SortOrder = 3 };
        var foam = new Category { Brand = ifoam, Name = "Активная пена", SortOrder = 1 };
        var body = new Category { Brand = ifoam, Name = "Очистители кузова", SortOrder = 2 };

        // Фасовка: артикул, подпись, объём, единица, розничная цена, остаток.
        static List<ProductVariant> Packs(params (string Article, string Label, decimal Volume, string Unit, decimal? Price, int Stock)[] packs) =>
            packs.Select(p => new ProductVariant
            {
                Article = p.Article,
                PackLabel = p.Label,
                VolumeValue = p.Volume,
                VolumeUnit = p.Unit,
                Price = p.Price,
                WholesalePrice = p.Price is null ? null : decimal.Round(p.Price.Value * 0.8m),
                StockQty = p.Stock,
                BoxQty = p.Volume >= 5 ? 1 : 12,
            }).ToList();

        Product Make(Category category, string name, string description, string ph, List<ProductVariant> variants) =>
            new() { Category = category, Name = name, Description = description, Ph = ph, Variants = variants };

        var luxe = Make(autoShampoo, "Шампунь для ручной мойки Демо Люкс",
            "Концентрированный шампунь для ручной мойки кузова. Хорошо пенится и не оставляет разводов.",
            "7,5", Packs(("900101", "1 л", 1m, "л", 1890m, 24), ("900102", "5 л", 5m, "л", 7490m, 6)));

        // Скрытая фасовка: на сайте её быть не должно.
        luxe.Variants.Add(new ProductVariant
        {
            Article = "900109", PackLabel = "20 л", VolumeValue = 20m, VolumeUnit = "л",
            Price = 25900m, StockQty = 2, IsActive = false,
        });

        db.Products.AddRange(
            luxe,
            Make(autoShampoo, "Шампунь с воском Демо Глянец",
                "Моет и сразу оставляет тонкий защитный слой воска.",
                "", Packs(("900111", "500 мл", 0.5m, "л", 1290m, 0), ("900112", "1 л", 1m, "л", 2190m, 0))),
            Make(autoShampoo, "Автошампунь для бесконтактной мойки Демо",
                "Для аппаратов высокого давления, разводится водой 1:50.",
                "12", Packs(("900121", "20 кг", 20m, "кг", 18900m, 3))),
            Make(household, "Средство для мытья посуды Демо Лимон",
                "Справляется с жиром даже в холодной воде.",
                "", Packs(("900201", "500 мл", 0.5m, "л", 690m, 50), ("900202", "5 л", 5m, "л", 4290m, 9))),
            Make(household, "Гель для стирки Демо Колор",
                "Для цветного белья, сохраняет яркость цвета.",
                "", Packs(("900211", "1,5 л", 1.5m, "л", 2490m, 15))),
            Make(household, "Чистящее средство для ванной Демо",
                "Удаляет известковый налёт и ржавчину.",
                "2", Packs(("900221", "600 мл", 0.6m, "л", 990m, 0))),
            Make(household, "Освежитель воздуха Демо Морской бриз",
                "Новинка, цена появится после поступления.",
                "", Packs(("900231", "300 мл", 0.3m, "л", null, 0))),
            Make(cleaning, "Средство для мытья полов Демо Проф",
                "Для ежедневной уборки больших площадей, без разводов.",
                "", Packs(("900301", "5 л", 5m, "л", 5690m, 12), ("900302", "20 л", 20m, "л", 19900m, 2))),
            Make(cleaning, "Обезжириватель Демо Индустрия",
                "Для кухонь общепита и производственных помещений.",
                "13", Packs(("900311", "5 кг", 5m, "кг", 6990m, 4))),
            Make(foam, "Активная пена Демо Ифоам Стандарт",
                "Для бесконтактной мойки легковых автомобилей.",
                "11,5", Packs(("900401", "1 л", 1m, "л", 2390m, 18), ("900402", "5 л", 5m, "л", 9990m, 5),
                    ("900403", "22 кг", 22m, "кг", 34900m, 1))),
            Make(foam, "Активная пена Демо Ифоам Трак",
                "Для грузовых автомобилей и спецтехники, снимает дорожную плёнку.",
                "12,5", Packs(("900411", "22 кг", 22m, "кг", 38900m, 0))),
            Make(body, "Очиститель битума Демо",
                "Удаляет следы битума и смолы, безопасен для лака.",
                "", Packs(("900501", "500 мл", 0.5m, "л", 1590m, 7))),
            Make(body, "Очиститель следов насекомых Демо",
                "Наносится перед основной мойкой.",
                "", Packs(("900511", "500 мл", 0.5m, "л", 1390m, 11), ("900512", "1 л", 1m, "л", 2290m, 0))));

        var bannerDir = Path.Combine(mediaRoot, "banners", "demo");
        Directory.CreateDirectory(bannerDir);
        await File.WriteAllTextAsync(Path.Combine(bannerDir, "1.svg"), BannerSvg("#62b22c", "Демо-баннер: скидки на автошампуни"));
        await File.WriteAllTextAsync(Path.Combine(bannerDir, "2.svg"), BannerSvg("#1f5b96", "Демо-баннер: новинки Ifoam"));
        db.Banners.AddRange(
            new Banner { Title = "Скидки на автошампуни", ImagePath = "banners/demo/1.svg", SortOrder = 1, LinkUrl = "/search?q=шампунь" },
            new Banner { Title = "Новинки Ifoam", ImagePath = "banners/demo/2.svg", SortOrder = 2 });

        db.Stores.AddRange(
            new Store
            {
                Address = "г. Актау, 1 микрорайон, демо-адрес, бутик №1", Kind = StoreKind.Shop,
                WorkingHours = "Пн-Сб, 9:00-18:00", LunchHours = "с 13:00 до 14:00", Phone = "+7 700 000-00-01",
                Latitude = 43.6530, Longitude = 51.1620, SortOrder = 1,
            },
            new Store
            {
                Address = "г. Актау, промзона, демо-склад", Kind = StoreKind.Pickup,
                WorkingHours = "Пн-Пт, 10:00-17:00", Phone = "+7 700 000-00-02", Latitude = 43.6395, Longitude = 51.2155, SortOrder = 2,
            },
            new Store { City = "Жанаозен", Address = "г. Жанаозен, демо-адрес, пункт выдачи", SortOrder = 3 });

        db.SiteContacts.Add(new SiteContact
        {
            Phone = "+7 700 000-00-00", Email = "demo@example.com",
            Whatsapp = "+7 700 000-00-00", Telegram = "@demo_shop", Instagram = "demo_shop",
        });

        await db.SaveChangesAsync();
        return $"Демо-данные добавлены: брендов {await db.Brands.CountAsync()}, товаров {await db.Products.CountAsync()}, "
            + $"артикулов {await db.ProductVariants.CountAsync()}, баннеров {await db.Banners.CountAsync()}, "
            + $"точек {await db.Stores.CountAsync()}.";
    }

    // Пропорция 3:1, как у BannerShape. Надпись отступает от краёв: там лежат стрелки карусели.
    private static string BannerSvg(string color, string text) => $"""
        <svg xmlns="http://www.w3.org/2000/svg" width="1200" height="400" viewBox="0 0 1200 400">
          <rect width="1200" height="400" fill="{color}"/>
          <circle cx="1050" cy="60" r="220" fill="#ffffff" opacity="0.12"/>
          <text x="150" y="215" font-family="Arial, sans-serif" font-size="46" font-weight="700" fill="#ffffff">{text}</text>
        </svg>
        """;
}
