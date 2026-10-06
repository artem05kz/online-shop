using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services.Telegram;

namespace OnlineShop.Web.Pages.Admin.Telegram;

/// <summary>Список номеров, на которые бот присылает новые заказы, и состояние бота.</summary>
public class IndexModel(ShopDbContext db, TelegramOptions options, TelegramBot bot, TelegramNotifier notifier) : PageModel
{
    public class AddInput
    {
        [Display(Name = "Кто это")]
        [StringLength(100)]
        public string? Name { get; set; }

        [Display(Name = "Номер телефона")]
        [Required(ErrorMessage = "Напишите номер телефона.")]
        public string Phone { get; set; } = "";
    }

    [BindProperty] public AddInput Input { get; set; } = new();

    public bool IsConfigured => options.IsConfigured;
    public string? BotUsername => bot.Username;
    public string? Problem => bot.Problem;
    public List<TelegramRecipient> Recipients { get; private set; } = [];

    public async Task OnGetAsync() => await LoadAsync();

    public async Task<IActionResult> OnPostAddAsync()
    {
        var phone = PhoneNumbers.Normalize(Input.Phone);
        if (ModelState.IsValid && phone.Length == 0)
        {
            ModelState.AddModelError("Input.Phone", "Это не похоже на номер телефона. Пример: +7 701 123-45-67.");
        }
        if (ModelState.IsValid && await db.TelegramRecipients.AnyAsync(r => r.Phone == phone))
        {
            ModelState.AddModelError("Input.Phone", $"Номер {PhoneNumbers.Display(phone)} уже есть в списке.");
        }
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        db.TelegramRecipients.Add(new TelegramRecipient
        {
            Name = Input.Name?.Trim() ?? "",
            Phone = phone,
            CreatedAt = DateTime.UtcNow,
        });
        await db.SaveChangesAsync();
        TempData.Flash($"Номер {PhoneNumbers.Display(phone)} добавлен. Теперь на этом телефоне откройте бота и нажмите «Старт».");
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var recipient = await db.TelegramRecipients.FindAsync(id);
        if (recipient is not null)
        {
            db.TelegramRecipients.Remove(recipient);
            await db.SaveChangesAsync();
            TempData.Flash($"Номер {PhoneNumbers.Display(recipient.Phone)} удалён, заказы туда больше не приходят.");
        }
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostTestAsync(CancellationToken ct)
    {
        if (!options.IsConfigured)
        {
            return RedirectToPage();
        }
        var report = await notifier.SendToAllAsync(
            "<b>Пробное сообщение</b>\nТак будут приходить новые заказы магазина GRASS.", ct);
        if (report.Sent + report.Failed + report.Unlinked == 0)
        {
            TempData.Flash("Отправлять некуда: никто из списка ещё не подключил телефон.", FlashLevel.Error);
        }
        else if (report.Failed + report.Unlinked == 0)
        {
            TempData.Flash($"Пробное сообщение отправлено: {report.Sent} {Plural.Choose(report.Sent, "телефон", "телефона", "телефонов")}.");
        }
        else
        {
            TempData.Flash($"Отправлено: {report.Sent}. Не дошло: {report.Failed + report.Unlinked}. " +
                "Если человек удалил чат с ботом, ему нужно снова открыть бота и нажать «Старт».", FlashLevel.Error);
        }
        return RedirectToPage();
    }

    private async Task LoadAsync() =>
        Recipients = await db.TelegramRecipients.OrderBy(r => r.Name).ThenBy(r => r.Phone).AsNoTracking().ToListAsync();
}
