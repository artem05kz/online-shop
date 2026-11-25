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

/// <summary>
/// Оформление заказа. Входить не нужно: гостю хватает имени и телефона, менеджер звонит сам.
/// Если покупатель вошёл, заказ попадает в его историю и подставляются сохранённые адреса.
/// </summary>
[Route("/orders/checkout")]
public class CheckoutController(ShopDbContext db, Cart cart, OrderAlerts alerts, AccountMail mail, StockReservations reservations, GuestOrders guestOrders) : Controller
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
            var user = userId is { } id ? await db.Users.FindAsync(id) : null;
            form.ContactName = user?.FullName ?? "";
            // Доставляем по городу магазина — подставляем его, покупателю остаётся вписать улицу.
            form.City = (await db.SiteContacts.Where(c => c.IsActive).Select(c => c.City).FirstOrDefaultAsync()) ?? "Актау";
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
        // Пока покупатель смотрел корзину, остаток могли заказать другие: больше доступного не продаём.
        foreach (var line in contents.Lines.Where(l => l.Quantity > l.Variant.AvailableQty))
        {
            var name = line.Variant.PackLabel.Length > 0 ? $"{line.Variant.Product.Name}, {line.Variant.PackLabel}" : line.Variant.Product.Name;
            ModelState.AddModelError("", line.Variant.AvailableQty == 0
                ? $"«{name}» закончился. Уберите его из корзины."
                : $"«{name}»: осталось {line.Variant.AvailableQty} шт. Уменьшите количество в корзине.");
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
        if (form.AcceptPrivacy && userId is { } buyerId && await db.Users.FindAsync(buyerId) is { PersonalDataConsentAt: null } user)
        {
            user.PersonalDataConsentAt = DateTime.UtcNow;
        }
        await db.SaveChangesAsync();
        await reservations.RecalculateForOrdersAsync([order.Id]);
        alerts.Enqueue(order.Id, $"{Request.Scheme}://{Request.Host}");
        await mail.NotifyOrderPlacedAsync(order.Id, $"{Request.Scheme}://{Request.Host}");

        await cart.ClearAsync();
        // «Заказ принят» показывается большой карточкой на странице заказа (Orders/Detail), а не плашкой вверху.
        TempData["OrderPlaced"] = true;
        if (userId is null)
        {
            // Если гость потом зарегистрируется в этом браузере, заказ перейдёт в его «Мои заказы».
            guestOrders.Remember(order);
        }
        // Гостю истории заказов нет, поэтому его заказ открывается по секретной ссылке.
        return userId is null
            ? Redirect(OrdersController.LinkPath(order))
            : RedirectToAction(nameof(OrdersController.Detail), "Orders", new { id = order.Id });
    }

    private IActionResult EmptyCart()
    {
        TempData.Flash("Корзина пуста.", FlashLevel.Info);
        return RedirectToAction(nameof(CatalogController.Home), "Catalog");
    }

    /// <summary>Номер покупателя, если он вошёл; null у гостя.</summary>
    private int? UserId() => User.Identity?.IsAuthenticated == true ? User.ShopUserId() : null;

    private Task<List<Store>> PickupStores() =>
        db.Stores.Where(s => s.Kind != StoreKind.Warehouse).OrderBy(s => s.SortOrder).ThenBy(s => s.Id).AsNoTracking().ToListAsync();

    // Гость соглашается на обработку данных при каждом заказе, покупатель с аккаунтом — один раз.
    private async Task<bool> NeedsConsent(int? userId) =>
        userId is not { } id || await db.Users.AnyAsync(u => u.Id == id && u.PersonalDataConsentAt == null);

    private async Task<List<Address>> UserAddresses(int? userId) =>
        userId is { } id ? await db.Addresses.Where(a => a.UserId == id).Ordered().AsNoTracking().ToListAsync() : [];
}
