using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services.Import;

namespace OnlineShop.Web.Pages.Admin.Import;

/// <summary>
/// Второй шаг загрузки: файл разбирается заново, в базу идут только строки с галочкой
/// (снятая галочка просто не попадает в форму) вместе с правками с предпросмотра.
/// Поля правок называются «поле_ключ», ключ тот же, что у галочки.
/// </summary>
public class ConfirmModel(ImportUploads uploads, ImportApplier applier, ILogger<ConfirmModel> logger) : PageModel
{
    [BindProperty] public string? PriceTempName { get; set; }
    [BindProperty] public string? StockTempName { get; set; }
    [BindProperty] public List<string> SelectedProducts { get; set; } = [];
    [BindProperty] public List<string> SelectedStock { get; set; } = [];

    public ImportStats Stats { get; private set; } = new();

    public IActionResult OnGet() => RedirectToPage("Index");

    public async Task<IActionResult> OnPostAsync()
    {
        var pricePath = uploads.Find(PriceTempName);
        var stockPath = uploads.Find(StockTempName);
        if (pricePath is null && stockPath is null)
        {
            TempData.Flash("Файлы загрузки не найдены: возможно, их уже записали в базу. Загрузите заново.", FlashLevel.Error);
            return RedirectToPage("Index");
        }

        try
        {
            if (pricePath is not null)
            {
                var result = OneCParser.ParsePriceList(pricePath);
                var selected = SelectedProducts.ToHashSet();
                result.Products.RemoveAll(p => !selected.Contains(p.Key));
                foreach (var product in result.Products)
                {
                    product.Brand = Edited($"brand_{product.Key}", product.Brand);
                    product.CategoryPath = Edited($"category_{product.Key}", product.CategoryPath);
                    product.Name = Edited($"name_{product.Key}", product.Name);
                }
                await applier.ApplyPriceListAsync(result, Stats);
            }
            if (stockPath is not null)
            {
                var result = OneCParser.ParseStock(stockPath);
                var selected = SelectedStock.ToHashSet();
                var quantities = new Dictionary<string, int>();
                foreach (var (article, qty) in result.Quantities.Where(q => selected.Contains(q.Key)))
                {
                    quantities[article] = int.TryParse(Request.Form[$"qty_{article}"], out var edited) ? edited : qty;
                }
                await applier.ApplyStockAsync(result with { Quantities = quantities }, Stats);
            }
        }
        catch (ImportFormatException e)
        {
            TempData.Flash($"Не смог разобрать файл: {e.Message}", FlashLevel.Error);
            return RedirectToPage("Index");
        }
        finally
        {
            uploads.Delete(pricePath);
            uploads.Delete(stockPath);
        }

        logger.LogInformation("Загрузка из 1С пользователем {User}: {Stats}", User.Identity?.Name, Stats);
        return Page();
    }

    // Пустое поле правки оставляет значение из файла, как в Django-версии.
    private string Edited(string field, string original)
    {
        var value = Request.Form[field].ToString().Trim();
        return value.Length > 0 ? value : original;
    }
}
