using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using OnlineShop.Web.Services.Email;
using OnlineShop.Web.Services.Telegram;
using OnlineShop.Web.ViewModels;

namespace OnlineShop.Web.Controllers;

[Authorize]
[Route("/orders/checkout")]
public class CheckoutController(ShopDbContext db, Cart cart, OrderAlerts alerts, AccountMail mail) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var contents = await cart.LoadAsync();
        if (contents.IsEmpty)
        {
            return EmptyCart();
        }

        var userId = UserId();
        if (await NeedsEmail(userId) is { } redirect)
        {
            return redirect;
        }
        var addresses = await UserAddresses(userId);
        var form = new CheckoutForm();

        // Заранее подставляем адрес по умолчанию, а если его нет, хотя бы имя из профиля.
        if (addresses.FirstOrDefault(a => a.IsDefault) is { } address)
        {
            form.SavedAddressId = address.Id;
            form.ContactName = address.FullName;
            form.ContactPhone = address.Phone;
            form.City = address.City;
            form.StreetAndDetails = CheckoutForm.AddressLine(address);
        }
        else
        {
            var user = await db.Users.FindAsync(userId);
            form.ContactName = user?.FullName ?? "";
        }

        return View(new CheckoutViewModel { Form = form, Cart = contents, Addresses = addresses, NeedsConsent = await NeedsConsent(userId), PickupStores = await PickupStores() });
    }

    [HttpPost("")]
    public async Task<IActionResult> Index([Bind(Prefix = "Form")] CheckoutForm form)
    {
        var contents = await cart.LoadAsync();
        if (contents.IsEmpty)
        {
            return EmptyCart();
        }

        var userId = UserId();
        if (await NeedsEmail(userId) is { } redirect)
        {
            return redirect;
        }
        var addresses = await UserAddresses(userId);
        if (form.SavedAddressId is { } savedId && addresses.All(a => a.Id != savedId))
        {
            ModelState.AddModelError("Form.SavedAddressId", "Выберите адрес из списка.");
        }
        Store? pickupStore = null;
        if (form.IsPickup)
        {
            pickupStore = form.PickupStoreId is { } storeId ? (await PickupStores()).FirstOrDefault(s => s.Id == storeId) : null;
            if (pickupStore is null)
            {
                ModelState.AddModelError("Form.PickupStoreId", "Выберите магазин, где заберёте заказ.");
            }
        }
        else
        {
            if (string.IsNullOrWhiteSpace(form.City))
            {
                ModelState.AddModelError("Form.City", "Обязательное поле.");
            }
            if (string.IsNullOrWhiteSpace(form.StreetAndDetails))
            {
                ModelState.AddModelError("Form.StreetAndDetails", "Обязательное поле.");
            }
        }
        if (!form.AcceptPrivacy && await NeedsConsent(userId))
        {
            ModelState.AddModelError("Form.AcceptPrivacy", PrivacyConsent.Required);
        }
        if (!ModelState.IsValid)
        {
            return View(new CheckoutViewModel { Form = form, Cart = contents, Addresses = addresses, NeedsConsent = await NeedsConsent(userId), PickupStores = await PickupStores() });
        }

        // Название, фасовка, артикул и цена копируются в заказ: каталог потом может поменяться.
        var order = new Order
        {
            UserId = userId,
            ContactName = form.ContactName.Trim(),
            ContactPhone = form.ContactPhone.Trim(),
            City = pickupStore?.City ?? form.City!.Trim(),
            StreetAndDetails = pickupStore?.Address ?? form.StreetAndDetails!.Trim(),
            IsPickup = pickupStore is not null,
            PickupStoreId = pickupStore?.Id,
            Comment = form.Comment?.Trim() ?? "",
            Items = contents.Lines.Select(line => new OrderItem
            {
                VariantId = line.Variant.Id,
                ProductName = line.Variant.Product.Name,
                PackLabel = line.Variant.PackLabel,
                Article = line.Variant.Article,
                Price = line.Price,
                Quantity = line.Quantity,
            }).ToList(),
        };
        db.Orders.Add(order);
        if (form.AcceptPrivacy && await db.Users.FindAsync(userId) is { PersonalDataConsentAt: null } user)
        {
            user.PersonalDataConsentAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        alerts.Enqueue(order.Id, $"{Request.Scheme}://{Request.Host}");
        await mail.NotifyOrderPlacedAsync(order.Id, $"{Request.Scheme}://{Request.Host}");

        await cart.ClearAsync();
        TempData.Flash($"Заказ №{order.Id} оформлен. Оплата при получении.");
        return RedirectToAction(nameof(OrdersController.Detail), "Orders", new { id = order.Id });
    }

    private IActionResult EmptyCart()
    {
        TempData.Flash("Корзина пуста.", FlashLevel.Info);
        return RedirectToAction(nameof(CatalogController.Home), "Catalog");
    }

    private int UserId() => User.ShopUserId();

    /// <summary>
    /// Статусы заказа приходят на почту, поэтому без подтверждённой почты заказ не оформить:
    /// покупатель уходит на страницу почты и возвращается сюда после подтверждения.
    /// </summary>
    private async Task<IActionResult?> NeedsEmail(int userId)
    {
        if (await db.Users.AnyAsync(u => u.Id == userId && u.Email != null && u.EmailConfirmed))
        {
            return null;
        }
        TempData.Flash("Укажите и подтвердите почту: на неё придут статусы заказа.", FlashLevel.Info);
        return Redirect($"{OnlineShop.Web.Pages.Accounts.EmailModel.PageUrl}?returnUrl={Uri.EscapeDataString("/orders/checkout")}");
    }

    private Task<List<Store>> PickupStores() =>
        db.Stores.Where(s => s.Kind != StoreKind.Warehouse).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).AsNoTracking().ToListAsync();

    private Task<bool> NeedsConsent(int userId) =>
        db.Users.AnyAsync(u => u.Id == userId && u.PersonalDataConsentAt == null);

    private Task<List<Address>> UserAddresses(int userId) =>
        db.Addresses.Where(a => a.UserId == userId).Ordered().AsNoTracking().ToListAsync();
}
