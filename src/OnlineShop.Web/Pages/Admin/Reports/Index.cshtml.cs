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
/// Продажи за период (редизайн 24.11): заказы, выручка, средний чек и штуки со сравнением с таким же
/// прошлым периодом; выручка по дням; доставка и самовывоз; категории; самые продаваемые товары;
/// записи на мойку. Выгрузка в Excel. Отменённые заказы в выручку не входят. Дни — по времени Актау.
/// </summary>
public class IndexModel(ShopDbContext db) : PageModel
{
    public record Bar(string Label, string Hint, decimal Revenue, int Orders);
    public record TopRow(string Article, string Name, string Pack, int Quantity, decimal Revenue, string? Photo = null);
    public record Share(string Label, int Count, decimal Revenue);

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

    /// <summary>Такой же по длине период прямо перед выбранным — для сравнения.</summary>
    public DateOnly PrevStart { get; private set; }
    public DateOnly PrevEnd { get; private set; }
    public int PrevOrderCount { get; private set; }
    public decimal PrevRevenue { get; private set; }
    public decimal PrevAverageCheck => PrevOrderCount == 0 ? 0 : PrevRevenue / PrevOrderCount;
    public int PrevItemsSold { get; private set; }

    /// <summary>Как получают: доставка по Актау, по области, самовывоз.</summary>
    public List<Share> Receive { get; private set; } = [];
    /// <summary>Выручка по категориям (верхнего уровня).</summary>
    public List<Share> Categories { get; private set; } = [];

    public int WashTotal { get; private set; }
    public int WashDone { get; private set; }
    public int WashCanceled { get; private set; }
    public int WashUpcoming { get; private set; }
    public List<Share> WashServices { get; private set; } = [];
    // Мойка, CRM шаг 2 (25.11): деньги, неявки, откуда клиенты и как платят.
    public int WashNoShow { get; private set; }
    public decimal WashRevenue { get; private set; }
    public int WashPaidCount { get; private set; }
    public decimal WashAverage => WashPaidCount == 0 ? 0 : WashRevenue / WashPaidCount;
    /// <summary>Доля неявок среди записей, время которых уже прошло (помыли + не приехал).</summary>
    public int WashNoShowPercent => WashDone + WashNoShow == 0 ? 0 : (int)Math.Round(WashNoShow * 100m / (WashDone + WashNoShow));
    public int WashNewClients { get; private set; }
    public List<Share> WashSourcesShare { get; private set; } = [];
    public List<Share> WashPaymentsShare { get; private set; } = [];

    /// <summary>Изменение в процентах к прошлому периоду; null — сравнивать не с чем.</summary>
    public static int? Change(decimal now, decimal before) =>
        before == 0 ? null : (int)Math.Round((now - before) / before * 100m);

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
            .Select(g => new TopRow(g.Key.Article, g.Key.ProductName, g.Key.PackLabel, g.Sum(i => i.Quantity), g.Sum(i => i.Subtotal),
                g.First().Variant is { } v ? Media.PhotoUrl(v.Product, Media.Small) : null))
            .OrderByDescending(r => r.Revenue).ThenByDescending(r => r.Quantity)
            .Take(10)
            .ToList();

        Receive =
        [
            Of("Доставка по Актау", sold.Where(o => !o.IsPickup && DeliveryPrices.IsCity(o.City))),
            Of("Доставка по области", sold.Where(o => !o.IsPickup && !DeliveryPrices.IsCity(o.City))),
            Of("Самовывоз", sold.Where(o => o.IsPickup)),
        ];
        Categories = sold.SelectMany(o => o.Items)
            .GroupBy(i => i.Variant?.Product.Category is { } c ? (c.Parent?.Name ?? c.Name) : "Без категории")
            .Select(g => new Share(g.Key, g.Sum(i => i.Quantity), g.Sum(i => i.Subtotal)))
            .OrderByDescending(c => c.Revenue).ToList();

        // Прошлый период той же длины.
        var days = End.DayNumber - Start.DayNumber + 1;
        PrevEnd = Start.AddDays(-1);
        PrevStart = PrevEnd.AddDays(-(days - 1));
        var prev = await db.Orders.Where(o => o.CreatedAt >= Utc(PrevStart) && o.CreatedAt < Utc(PrevEnd.AddDays(1)) && o.Status != OrderStatus.Canceled)
            .Include(o => o.Items).AsNoTracking().ToListAsync();
        PrevOrderCount = prev.Count;
        PrevRevenue = prev.Sum(o => o.Total);
        PrevItemsSold = prev.Sum(o => o.TotalQuantity);

        // Мойка: время записи хранится по Актау.
        var washFrom = Start.ToDateTime(TimeOnly.MinValue);
        var washTo = End.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var wash = await db.WashBookings.Where(b => b.StartsAt >= washFrom && b.StartsAt < washTo).AsNoTracking().ToListAsync();
        WashTotal = wash.Count;
        WashDone = wash.Count(b => b.Status == WashBookingStatus.Done);
        WashCanceled = wash.Count(b => b.Status == WashBookingStatus.Cancelled);
        WashUpcoming = wash.Count(b => b.Status is WashBookingStatus.New or WashBookingStatus.Confirmed or WashBookingStatus.Arrived);
        WashServices = wash.Where(b => b.Status != WashBookingStatus.Cancelled)
            .GroupBy(b => b.ServiceName).Select(g => new Share(g.Key, g.Count(), g.Where(b => b.IsPaid).Sum(b => b.Price ?? 0m)))
            .OrderByDescending(x => x.Count).ToList();
        WashNoShow = wash.Count(b => b.Status == WashBookingStatus.NoShow);
        var paid = wash.Where(b => b.IsPaid && b.Price is not null).ToList();
        WashPaidCount = paid.Count;
        WashRevenue = paid.Sum(b => b.Price!.Value);
        WashSourcesShare = wash.Where(b => b.Status != WashBookingStatus.Cancelled)
            .GroupBy(b => WashSources.Text(b.Source)).Select(g => new Share(g.Key, g.Count(), 0m))
            .OrderByDescending(x => x.Count).ToList();
        WashPaymentsShare = paid.GroupBy(b => WashPayments.Text(b.PaymentMethod) is { Length: > 0 } t ? t : "Не указано")
            .Select(g => new Share(g.Key, g.Count(), g.Sum(b => b.Price!.Value)))
            .OrderByDescending(x => x.Revenue).ToList();
        // Новые клиенты — те, чья самая первая запись попала в этот период.
        var phones = wash.Select(b => b.Phone).Distinct().ToList();
        var firstSeen = await db.WashBookings.Where(b => phones.Contains(b.Phone))
            .GroupBy(b => b.Phone).Select(g => g.Min(b => b.StartsAt)).ToListAsync();
        WashNewClients = firstSeen.Count(t => t >= washFrom);
    }

    private static Share Of(string label, IEnumerable<Order> orders)
    {
        var list = orders.ToList();
        return new Share(label, list.Count, list.Sum(o => o.Total));
    }

    private static DateTime Utc(DateOnly day) => Format.FromLocal(day.ToDateTime(TimeOnly.MinValue));

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
        RemoveEmptyNumberFormat(stream);
        return File(stream.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            $"заказы {Start:dd.MM.yyyy}-{End:dd.MM.yyyy}.xlsx");
    }

    /// <summary>
    /// ClosedXML 0.105 всегда пишет в styles.xml пустой формат числа <c>numFmtId="0" formatCode=""</c>.
    /// По стандарту так нельзя (формат 0 встроенный и не объявляется), старые версии Excel на этом
    /// могут спотыкаться. Убираем эту строку из готового файла.
    /// </summary>
    private static void RemoveEmptyNumberFormat(MemoryStream stream)
    {
        using var zip = new System.IO.Compression.ZipArchive(stream, System.IO.Compression.ZipArchiveMode.Update, leaveOpen: true);
        var entry = zip.GetEntry("xl/styles.xml");
        if (entry is null)
        {
            return;
        }
        string xml;
        using (var reader = new StreamReader(entry.Open()))
        {
            xml = reader.ReadToEnd();
        }
        const string empty = "<x:numFmt numFmtId=\"0\" formatCode=\"\" />";
        if (!xml.Contains(empty))
        {
            return;
        }
        xml = xml.Replace(empty, "");
        xml = System.Text.RegularExpressions.Regex.Replace(xml, @"<x:numFmts count=""(\d+)"">",
            m => $"<x:numFmts count=\"{int.Parse(m.Groups[1].Value) - 1}\">");
        // Форматов не осталось — пустой список тоже убираем.
        xml = xml.Replace("<x:numFmts count=\"0\"></x:numFmts>", "");
        entry.Delete();
        var fresh = zip.CreateEntry("xl/styles.xml");
        using var writer = new StreamWriter(fresh.Open(), new System.Text.UTF8Encoding(false));
        writer.Write(xml);
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
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product).ThenInclude(p => p.Images)
            .Include(o => o.Items).ThenInclude(i => i.Variant).ThenInclude(v => v!.Product).ThenInclude(p => p.Category).ThenInclude(c => c.Parent)
            .OrderBy(o => o.CreatedAt).AsSplitQuery().AsNoTracking().ToListAsync();
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
