using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;
using OnlineShop.Web.Services;
using OnlineShop.Web.ViewModels;

namespace OnlineShop.Web.Controllers;

[Authorize]
[Route("/orders/checkout")]
public class CheckoutController(ShopDbContext db, Cart cart, UserManager<ApplicationUser> userManager) : Controller
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
            var user = await userManager.FindByIdAsync(userId.ToString());
            form.ContactName = user?.FullName ?? "";
        }

        return View(new CheckoutViewModel { Form = form, Cart = contents, Addresses = addresses });
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
        if (!ModelState.IsValid)
        {
            return View(new CheckoutViewModel { Form = form, Cart = contents, Addresses = addresses });
        }

        // Название, фасовка, артикул и цена копируются в заказ: каталог потом может поменяться.
        var order = new Order
        {
            UserId = userId,
            ContactName = form.ContactName.Trim(),
            ContactPhone = form.ContactPhone.Trim(),
            City = form.City.Trim(),
            StreetAndDetails = form.StreetAndDetails.Trim(),
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
        await db.SaveChangesAsync();

        cart.Clear();
        TempData.Flash($"Заказ №{order.Id} оформлен. Оплата при получении.");
        return RedirectToAction(nameof(OrdersController.Detail), "Orders", new { id = order.Id });
    }

    private IActionResult EmptyCart()
    {
        TempData.Flash("Корзина пуста.", FlashLevel.Info);
        return RedirectToAction(nameof(CatalogController.Home), "Catalog");
    }

    private int UserId() => int.Parse(userManager.GetUserId(User)!);

    private Task<List<Address>> UserAddresses(int userId) =>
        db.Addresses.Where(a => a.UserId == userId).Ordered().AsNoTracking().ToListAsync();
}
