using OnlineShop.Web.Infrastructure;

namespace OnlineShop.Tests;

/// <summary>Статус «Открыто до … / Закрыто, откроется …» на странице адресов по тексту часов из панели.</summary>
public class StoreHoursTests
{
    // 23.11.2026 — понедельник: At(1, …) — понедельник, At(7, …) — воскресенье.
    private static DateTime At(int day, int hour, int minute = 0) => new(2026, 11, 22 + day, hour, minute, 0);

    [Fact]
    public void Weekdays_with_weekend_off()
    {
        var hours = StoreHours.Parse("Пн–Пт 9:00–16:00, Сб–Вс выходной")!;
        Assert.False(hours.EveryDay);
        Assert.Equal((true, "Открыто до 16:00"), hours.StatusAt(At(1, 10)));          // пн 10:00
        Assert.Equal((false, "Закрыто, откроется в 9:00"), hours.StatusAt(At(1, 7)));  // пн 7:00
        Assert.Equal((false, "Закрыто, откроется завтра в 9:00"), hours.StatusAt(At(1, 17)));
        Assert.Equal((false, "Закрыто, откроется в понедельник в 9:00"), hours.StatusAt(At(5, 17))); // пт вечер
        Assert.Equal((false, "Закрыто, откроется в понедельник в 9:00"), hours.StatusAt(At(6, 12))); // сб
    }

    [Fact]
    public void Every_day_and_lunch()
    {
        var hours = StoreHours.Parse("Ежедневно 11:00–18:00", "13:00-14:00")!;
        Assert.True(hours.EveryDay);
        Assert.Equal((false, "Обед до 14:00"), hours.StatusAt(At(6, 13, 30)));
        Assert.Equal((true, "Открыто до 18:00"), hours.StatusAt(At(7, 15)));
    }

    [Fact]
    public void Admin_example_and_unreadable_text()
    {
        var hours = StoreHours.Parse("Пн-Сб, 9:00-18:00")!;
        Assert.Equal((true, "Открыто до 18:00"), hours.StatusAt(At(6, 12)));
        Assert.Equal((false, "Закрыто, откроется завтра в 9:00"), hours.StatusAt(At(7, 12)));
        Assert.Null(StoreHours.Parse("по договорённости"));
        Assert.Null(StoreHours.Parse(""));
    }

    [Fact]
    public void Lines_split_by_day_groups()
    {
        Assert.Equal(["Пн–Пт 9:00–16:00", "Сб–Вс выходной"], StoreHours.Lines("Пн-Пт 9:00-16:00, Сб-Вс выходной"));
        Assert.Equal(["Ежедневно 11:00–18:00"], StoreHours.Lines("ежедневно 11:00-18:00"));
    }
}
