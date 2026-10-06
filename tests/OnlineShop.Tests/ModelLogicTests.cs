using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Tests;

/// <summary>Вычисляемые свойства моделей, перенесённые из @property Django-версии.</summary>
public class ModelLogicTests
{
    [Theory]
    [InlineData("Автошампунь Ifoam 22 кг!", "автошампунь-ifoam-22-кг")]
    [InlineData("  Active Foam -- Truck  ", "active-foam-truck")]
    [InlineData("Средство «Universal» (pH 7,5)", "средство-universal-ph-75")]
    [InlineData("___Ёлка___", "ёлка")]
    public void Slug_matches_django_slugify(string name, string expected)
    {
        Assert.Equal(expected, Slug.From(name));
    }

    [Fact]
    public void Slug_make_unique_adds_suffix_within_max_length()
    {
        var taken = new HashSet<string> { "abcdef", "abcdef-2" };
        Assert.Equal("abcdef-3", Slug.MakeUnique("abcdef", taken, 20));
        Assert.Equal("abcd-2", Slug.MakeUnique("abcdef", taken, 6));
        Assert.Equal("free", Slug.MakeUnique("free", taken, 20));
    }

    private static ProductVariant Variant(int id, decimal? price, int stock, bool active = true, decimal? volume = null) =>
        new() { Id = id, Article = $"A{id}", Price = price, StockQty = stock, IsActive = active, VolumeValue = volume };

    [Fact]
    public void Product_prices_and_stock_ignore_inactive_variants()
    {
        var product = new Product
        {
            Variants =
            [
                Variant(1, 5000m, 0, volume: 22m),
                Variant(2, 900m, 3, volume: 1m),
                Variant(3, 100m, 50, active: false, volume: 0.5m),
            ],
        };

        Assert.Equal(900m, product.MinPrice);
        Assert.True(product.HasSeveralPacks);
        Assert.True(product.InStock);
        Assert.Equal([2, 1], product.ActiveVariants.Select(v => v.Id));
    }

    [Fact]
    public void Packs_sort_by_volume_across_units()
    {
        var product = new Product
        {
            Variants =
            [
                new ProductVariant { Id = 1, Article = "21", VolumeValue = 21m, VolumeUnit = "л" },
                new ProductVariant { Id = 2, Article = "500", VolumeValue = 500m, VolumeUnit = "мл триггер" },
                new ProductVariant { Id = 3, Article = "1", VolumeValue = 1m, VolumeUnit = "л" },
            ],
        };
        Assert.Equal(["500", "1", "21"], product.ActiveVariants.Select(v => v.Article));
    }

    [Fact]
    public void Default_variant_prefers_cheapest_in_stock_then_cheapest_overall()
    {
        var product = new Product
        {
            Variants = [Variant(1, 500m, 0), Variant(2, 900m, 4), Variant(3, null, 10)],
        };
        Assert.Equal(2, product.DefaultVariant!.Id);

        product.Variants[1].StockQty = 0;
        Assert.Equal(1, product.DefaultVariant!.Id);

        Assert.Null(new Product { Variants = [Variant(1, null, 5)] }.DefaultVariant);
        Assert.Null(new Product().MinPrice);
    }

    [Fact]
    public void Primary_image_falls_back_to_first_by_order()
    {
        var product = new Product
        {
            Images =
            [
                new ProductImage { Id = 1, ImagePath = "b.jpg", SortOrder = 2 },
                new ProductImage { Id = 2, ImagePath = "a.jpg", SortOrder = 1 },
            ],
        };
        Assert.Equal("a.jpg", product.PrimaryImage!.ImagePath);

        product.Images[0].IsPrimary = true;
        Assert.Equal("b.jpg", product.PrimaryImage!.ImagePath);

        Assert.Null(new Product().PrimaryImage);
    }

    [Fact]
    public void Site_contact_builds_links_from_numbers_and_usernames()
    {
        var contact = new SiteContact
        {
            Phone = "+7 (701) 245-48-48",
            Whatsapp = "+7 701 245-48-48",
            Telegram = "@grass_aktau",
            Instagram = "https://instagram.com/grass.kz",
        };

        Assert.Equal("+77012454848", contact.PhoneLink);
        Assert.Equal("https://wa.me/77012454848", contact.WhatsappLink);
        Assert.Equal("https://t.me/grass_aktau", contact.TelegramLink);
        Assert.Equal("https://instagram.com/grass.kz", contact.InstagramLink);
        Assert.Equal("", new SiteContact().WhatsappLink);
        Assert.Equal("", new SiteContact { Whatsapp = "нет" }.WhatsappLink);
    }

    [Fact]
    public void Address_lines_skip_empty_parts()
    {
        var address = new Address { City = "Актау", Street = "14 микрорайон", House = "5", Floor = "3", Intercom = "12" };

        Assert.Equal("14 микрорайон, дом 5", address.StreetLine);
        Assert.Equal("этаж 3, домофон 12", address.DetailsLine);
        Assert.Equal("Актау, 14 микрорайон, дом 5, этаж 3, домофон 12", address.ShortText);
        Assert.Equal("Актау, 2 мкр", new Address { City = "Актау", Street = "2 мкр" }.ShortText);
    }

    [Fact]
    public void Store_hours_line_joins_work_and_lunch()
    {
        Assert.Equal("Пн-Сб, 9:00-18:00 | обед с 13:00 до 14:00",
            new Store { WorkingHours = "Пн-Сб, 9:00-18:00", LunchHours = "с 13:00 до 14:00" }.HoursLine);
        Assert.Equal("", new Store().HoursLine);
    }

    [Fact]
    public void Order_totals_and_progress_steps()
    {
        var order = new Order
        {
            Status = OrderStatus.Delivering,
            Items =
            [
                new OrderItem { Price = 1500.50m, Quantity = 2 },
                new OrderItem { Price = 300m, Quantity = 1 },
            ],
        };

        Assert.Equal(3301m, order.Total);
        Assert.Equal(3, order.TotalQuantity);
        Assert.Equal(
            [
                new OrderProgressStep("Новый", true, false),
                new OrderProgressStep("Подтверждён", true, false),
                new OrderProgressStep("Доставляется", false, true),
                new OrderProgressStep("Выполнен", false, false),
            ],
            order.ProgressSteps);

        order.Status = OrderStatus.Canceled;
        Assert.Empty(order.ProgressSteps);
        Assert.Equal("Заказ №0 (Отменён)", order.ToString());
    }
}
