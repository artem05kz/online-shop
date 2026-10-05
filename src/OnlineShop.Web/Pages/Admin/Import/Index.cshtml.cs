using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Services.Import;

namespace OnlineShop.Web.Pages.Admin.Import;

/// <summary>
/// Загрузка из 1С в браузере, то же, что консольная команда --import-1c.
/// Шаги: выбрать файлы, на предпросмотре снять галочки с лишних строк и поправить названия,
/// подтвердить (страница Confirm). До подтверждения в базу ничего не пишется.
/// </summary>
[RequestSizeLimit(100 * 1024 * 1024)]
public class IndexModel(ImportUploads uploads, ILogger<IndexModel> logger) : PageModel
{
    [BindProperty] public IFormFile? PriceList { get; set; }
    [BindProperty] public IFormFile? Stock { get; set; }

    public PriceListResult? PriceResult { get; private set; }
    public StockResult? StockResult { get; private set; }
    public string? PriceTempName { get; private set; }
    public string? StockTempName { get; private set; }

    public bool IsPreview => PriceResult is not null || StockResult is not null;

    public void OnGet()
    {
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var hasPrice = PriceList is { Length: > 0 };
        var hasStock = Stock is { Length: > 0 };
        if (!hasPrice && !hasStock)
        {
            TempData.Flash("Выберите хотя бы один файл: прайс-лист и/или остатки.", FlashLevel.Error);
            return RedirectToPage();
        }
        foreach (var file in new[] { PriceList, Stock }.Where(f => f is { Length: > 0 }))
        {
            if (!Path.GetExtension(file!.FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                TempData.Flash($"«{file.FileName}» не файл Excel (.xlsx). Сохраните отчёт 1С в формате Excel 2007 и новее.", FlashLevel.Error);
                return RedirectToPage();
            }
        }

        try
        {
            if (hasPrice)
            {
                PriceTempName = await uploads.SaveAsync(PriceList!);
                PriceResult = OneCParser.ParsePriceList(uploads.Find(PriceTempName)!);
            }
            if (hasStock)
            {
                StockTempName = await uploads.SaveAsync(Stock!);
                StockResult = OneCParser.ParseStock(uploads.Find(StockTempName)!);
            }
        }
        catch (Exception e)
        {
            // Файл пришёл от менеджера, и сломаться при чтении он может как угодно: битый архив, другой
            // формат под расширением .xlsx, пароль. Вместо страницы ошибки объясняем, что не так с файлом.
            uploads.Delete(uploads.Find(PriceTempName));
            uploads.Delete(uploads.Find(StockTempName));
            if (e is not ImportFormatException)
            {
                logger.LogWarning(e, "Не удалось прочитать файл выгрузки 1С");
            }
            var reason = e is ImportFormatException ? e.Message : "файл повреждён или это не Excel.";
            TempData.Flash($"Не смог разобрать файл: {reason}", FlashLevel.Error);
            return RedirectToPage();
        }
        return Page();
    }
}
