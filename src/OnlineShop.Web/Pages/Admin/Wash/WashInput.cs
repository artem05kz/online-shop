using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;

namespace OnlineShop.Web.Pages.Admin.Wash;

/// <summary>
/// Поля записи на мойку в панели (CRM, 24.11) — общие для «Новой записи» и карточки записи:
/// когда, услуга, место, клиент, откуда пришёл, цена и оплата, заметка.
/// </summary>
public class WashInput
{
    [Display(Name = "День")] public string Date { get; set; } = "";
    [Display(Name = "Время")] public string Time { get; set; } = "";
    [Display(Name = "Услуга")] public string Service { get; set; } = "";
    /// <summary>0 — любое свободное место.</summary>
    [Display(Name = "Место")] public int Box { get; set; }

    [Display(Name = "Имя клиента")]
    [Required(ErrorMessage = "Напишите, как зовут клиента.")]
    [StringLength(100)]
    public string? Name { get; set; }

    [Display(Name = "Телефон")]
    [Required(ErrorMessage = "Нужен телефон клиента.")]
    [StringLength(30)]
    public string? Phone { get; set; }

    [Display(Name = "Машина")] [StringLength(100)] public string? Car { get; set; }
    [Display(Name = "Откуда клиент")] public string Source { get; set; } = WashSources.Phone;

    [Display(Name = "Цена, ₸")]
    [Range(0, 10_000_000, ErrorMessage = "Цена должна быть от 0.")]
    public decimal? Price { get; set; }

    [Display(Name = "Оплачено")] public bool IsPaid { get; set; }
    [Display(Name = "Способ оплаты")] public string PaymentMethod { get; set; } = "";
    [Display(Name = "Комментарий клиента")] [StringLength(500)] public string? Comment { get; set; }
    [Display(Name = "Заметка администратора")] [StringLength(500)] public string? ManagerNote { get; set; }

    public static WashInput From(WashBooking b) => new()
    {
        Date = b.StartsAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        Time = b.StartsAt.ToString("HH:mm", CultureInfo.InvariantCulture),
        Service = b.ServiceKey, Box = b.Box, Name = b.Name, Phone = PhoneNumbers.Display(b.Phone), Car = b.Car,
        Source = b.Source, Price = b.Price, IsPaid = b.IsPaid, PaymentMethod = b.PaymentMethod,
        Comment = b.Comment, ManagerNote = b.ManagerNote,
    };

    /// <summary>
    /// Проверяет поля и свободно ли место. Ошибки пишет в ModelState (ключи «Input.…»).
    /// Возвращает начало, услугу и место, если всё в порядке. exceptId — сама запись при правке.
    /// </summary>
    public async Task<(DateTime Start, WashService Service, int Box, string Phone)?> ValidateAsync(
        ModelStateDictionary state, WashSlots slots, CarWash wash, int exceptId = 0)
    {
        var service = CarWashes.FindService(Service);
        if (service is null)
        {
            state.AddModelError("Input.Service", "Выберите услугу.");
        }
        var phone = PhoneNumbers.Normalize(Phone);
        if (!string.IsNullOrWhiteSpace(Phone) && phone.Length == 0)
        {
            state.AddModelError("Input.Phone", "Похоже, в номере ошибка. Например: +7 701 123-45-67.");
        }
        if (!DateOnly.TryParseExact(Date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day))
        {
            state.AddModelError("Input.Date", "Выберите день.");
        }
        if (!TimeOnly.TryParseExact(Time, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var time))
        {
            state.AddModelError("Input.Time", "Выберите время.");
        }
        if (!state.IsValid || service is null)
        {
            return null;
        }
        if (time < wash.Opens || time.AddMinutes(service.Minutes) > wash.Closes || time.AddMinutes(service.Minutes) < time)
        {
            state.AddModelError("Input.Time", $"Мойка работает {wash.Opens:H:mm}–{wash.Closes:H:mm}: услуга «{service.Name}» должна закончиться до закрытия.");
            return null;
        }
        if (Box < 0 || Box > wash.Boxes)
        {
            Box = 0;
        }
        var start = day.ToDateTime(time);
        var box = await slots.FreeBoxAsync(wash, start, service.Minutes, Box, exceptId);
        if (box == 0)
        {
            state.AddModelError("Input.Time", "На это время все места заняты. Выберите другое время.");
            return null;
        }
        if (Box > 0 && box != Box)
        {
            state.AddModelError("Input.Box", $"Место {Box} в это время занято. Свободно место {box}.");
            return null;
        }
        return (start, service, box, phone);
    }

    /// <summary>Переносит поля в запись (кроме статуса).</summary>
    public void Apply(WashBooking b, CarWash wash, WashService service, DateTime start, int box, string phone)
    {
        b.WashKey = wash.Key;
        b.WashName = wash.Name;
        b.ServiceKey = service.Key;
        b.ServiceName = service.Name;
        b.Minutes = service.Minutes;
        b.StartsAt = start;
        b.Box = box;
        b.Name = (Name ?? "").Trim();
        b.Phone = phone;
        b.Car = Car?.Trim() ?? "";
        b.Comment = Comment?.Trim() ?? "";
        b.ManagerNote = ManagerNote?.Trim() ?? "";
        b.Source = WashSources.All.Any(s => s.Code == Source) ? Source : WashSources.Phone;
        b.Price = Price;
        b.IsPaid = IsPaid;
        b.PaymentMethod = IsPaid && WashPayments.All.Any(p => p.Code == PaymentMethod) ? PaymentMethod : "";
    }

    /// <summary>Время на сетке мойки с шагом 30 минут — для выбора кнопками.</summary>
    public static IEnumerable<TimeOnly> Times(CarWash wash)
    {
        for (var t = wash.Opens; t < wash.Closes; t = t.AddMinutes(CarWashes.SlotStep))
        {
            yield return t;
            if (t.AddMinutes(CarWashes.SlotStep) < t)
            {
                yield break;
            }
        }
    }
}
