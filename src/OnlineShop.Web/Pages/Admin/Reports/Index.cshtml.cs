using System.Globalization;
using ClosedXML.Excel;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Reports;

/// <summary>
/// Продажи за период: заказы, выручка, средний чек, выручка по дням, самые продаваемые товары,
/// выгрузка в Excel. Отменённые заказы в выручку не входят. Дни считаются по времени Актау.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public record Bar(string Label, string Hint, decimal Revenue, int Orders);
    public record TopRow(string Article, string Name, string Pack, int Quantity, decimal Revenue);

    [BindProperty(SupportsGet = true)] public string Period { get; set; } = "30d";
    [BindProperty(SupportsGet = true)] public DateOnly? From { get; set; }
    [BindProperty(SupportsGet = true)] public DateOnly? To { get; set; }

    public DateOnly Start { get; private set; }
    public DateOnly End { get; private set; }
    public int OrderCount { get; private set; }
    public int CanceledCount { get; private set; }
    public decimal Revenue { get; private set; }
    public decimal AverageCheck => OrderCount == 0 ? 0 : Revenue / OrderCount;
    public int ItemsSold { get; private set; }
    public List<Bar> Bars { get; private set; } = [];
    public bool ByMonth => End.DayNumber - Start.DayNumber > 62;
    public List<TopRow> Top { get; private set; } = [];

    public static readonly (string Code, string Label)[] Periods =
        [("7d", "7 дней"), ("30d", "30 дней"), ("month", "Этот месяц"), ("prev", "Прошлый месяц")];

    public async Task OnGetAsync()
    {
        var orders = await LoadAsync();
        var sold = orders.Where(o => !o.IsCanceled).ToList();
        OrderCount = sold.Count;
        CanceledCount = orders.Count - sold.Count;
        Revenue = sold.Sum(o => o.Total);
        ItemsSold = sold.Sum(o => o.TotalQuantity);
        Bars = BuildBars(sold);
        Top = sold.SelectMany(o => o.Items)
            .GroupBy(i => (i.Article, i.ProductName, i.PackLabel))
            .Select(g => new TopRow(g.Key.Article, g.Key.ProductName, g.Key.PackLabel, g.Sum(i => i.Quantity), g.Sum(i => i.Subtotal)))
            .OrderByDescending(r => r.Revenue).ThenByDescending(r => r.Quantity)
            .Take(15)
            .ToList();
    }

    /// <summary>Заказы и проданные позиции за период в Excel: удобно сверить с 1С или посчитать что-то своё.</summary>
    public async Task<IActionResult> OnGetExcelAsync()
    {
        var orders = await LoadAsync();
        using var book = new XLWorkbook();

        var sheet = book.AddWorksheet("Заказы");
        string[] head = ["Номер", "Дата", "Статус", "Получатель", "Телефон", "Получение", "Адрес", "Позиций, шт.", "Сумма, ₸"];
        for (var c = 0; c < head.Length; c++)
        {
            sheet.Cell(1, c + 1).Value = head[c];
        }
        var row = 2;
        foreach (var order in orders)
        {
            sheet.Cell(row, 1).Value = order.Id;
            sheet.Cell(row, 2).Value = Format.ToLocal(order.CreatedAt);
            sheet.Cell(row, 2).Style.DateFormat.Format = "dd.MM.yyyy HH:mm";
            sheet.Cell(row, 3).Value = order.Status.Label();
            sheet.Cell(row, 4).Value = order.ContactName;
            sheet.Cell(row, 5).Value = order.ContactPhone;
            sheet.Cell(row, 6).Value = order.IsPickup ? "Самовывоз" : "Доставка";
            sheet.Cell(row, 7).Value = $"{order.City}, {order.StreetAndDetails}";
            sheet.Cell(row, 8).Value = order.TotalQuantity;
            sheet.Cell(row, 9).Value = order.Total;
            row++;
        }
        Style(sheet);

        var items = book.AddWorksheet("Товары");
        string[] itemsHead = ["Номер заказа", "Дата", "Статус", "Артикул", "Товар", "Фасовка", "Кол-во", "Цена, ₸", "Сумма, ₸"];
        for (var c = 0; c < itemsHead.Length; c++)
        {
            items.Cell(1, c + 1).Value = itemsHead[c];
        }
        row = 2;
        foreach (var order in orders)
        {
            foreach (var item in order.Items.OrderBy(i => i.Id))
            {
                items.Cell(row, 1).Value = order.Id;
                items.Cell(row, 2).Value = Format.ToLocal(order.CreatedAt);
                items.Cell(row, 2).Style.DateFormat.Format = "dd.MM.yyyy";
                items.Cell(row, 3).Value = order.Status.Label();
                items.Cell(row, 4).Value = item.Article;
                items.Cell(row, 5).Value = item.ProductName;
                items.Cell(row, 6).Value = item.PackLabel;
                items.Cell(row, 7).Value = item.Quantity;
                items.Cell(row, 8).Value = item.Price;
                items.Cell(row, 9).Value = item.Subtotal;
                row++;
            }
        }
        Style(items);

        using var stream = new MemoryStream();
        book.SaveAs(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"заказы {Start:dd.MM.yyyy}-{End:dd.MM.yyyy}.xlsx");
    }

    private static void Style(IXLWorksheet sheet)
    {
        sheet.Row(1).Style.Font.Bold = true;
        sheet.SheetView.FreezeRows(1);
        sheet.Columns().AdjustToContents(1, 200, 8, 60);
    }

    private async Task<List<Order>> LoadAsync()
    {
        (Start, End) = Range();
        var from = Format.FromLocal(Start.ToDateTime(TimeOnly.MinValue));
        var to = Format.FromLocal(End.AddDays(1).ToDateTime(TimeOnly.MinValue));
        // Заказов за месяц у магазина десятки и сотни: считаем в памяти, это проще и одинаково для SQLite и PostgreSQL.
        return await db.Orders.Where(o => o.CreatedAt >= from && o.CreatedAt < to)
            .Include(o => o.Items).OrderBy(o => o.CreatedAt).AsNoTracking().ToListAsync();
    }

    private (DateOnly Start, DateOnly End) Range()
    {
        var today = DateOnly.FromDateTime(Format.ToLocal(DateTime.UtcNow));
        if (From is { } from && To is { } to)
        {
            Period = "custom";
            return from <= to ? (from, to) : (to, from);
        }
        var monthStart = new DateOnly(today.Year, today.Month, 1);
        return Period switch
        {
            "7d" => (today.AddDays(-6), today),
            "month" => (monthStart, today),
            "prev" => (monthStart.AddMonths(-1), monthStart.AddDays(-1)),
            _ => (today.AddDays(-29), today),
        };
    }

    /// <summary>Столбики по дням, а для периода длиннее двух месяцев по месяцам: иначе их не разглядеть.</summary>
    private List<Bar> BuildBars(List<Order> sold)
    {
        var russian = CultureInfo.GetCultureInfo("ru-RU");
        var byMonth = ByMonth;
        var groups = sold.GroupBy(o =>
        {
            var day = DateOnly.FromDateTime(Format.ToLocal(o.CreatedAt));
            return byMonth ? new DateOnly(day.Year, day.Month, 1) : day;
        }).ToDictionary(g => g.Key, g => (Revenue: g.Sum(o => o.Total), Orders: g.Count()));

        var bars = new List<Bar>();
        for (var day = byMonth ? new DateOnly(Start.Year, Start.Month, 1) : Start; day <= End; day = byMonth ? day.AddMonths(1) : day.AddDays(1))
        {
            var (revenue, count) = groups.GetValueOrDefault(day);
            bars.Add(new Bar(
                byMonth ? day.ToString("MMM yy", russian) : day.ToString("dd.MM", russian),
                byMonth ? day.ToString("MMMM yyyy", russian) : day.ToString("dd.MM.yyyy, ddd", russian),
                revenue, count));
        }
        return bars;
    }
}
