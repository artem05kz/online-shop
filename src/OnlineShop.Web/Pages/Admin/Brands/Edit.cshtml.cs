using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Brands;

public class EditModel(ShopDbContext db) : PageModel
{
    public class InputModel
    {
        [Display(Name = "Название")]
        [Required(ErrorMessage = "Обязательное поле."), StringLength(100, ErrorMessage = "Не длиннее {1} символов.")]
        public string Name { get; set; } = "";

        [Display(Name = "Ярлык для ссылки")]
        [StringLength(110, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Slug { get; set; }

        [Display(Name = "Очерёдность на главной странице")]
        [Range(0, int.MaxValue, ErrorMessage = "Число не меньше 0.")]
        public int SortOrder { get; set; }

        [Display(Name = "Показывать на сайте")]
        public bool IsActive { get; set; } = true;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public int? Id { get; private set; }

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is null)
        {
            return Page();
        }
        var brand = await db.Brands.FindAsync(id);
        if (brand is null)
        {
            return NotFound();
        }
        Input = new InputModel { Name = brand.Name, Slug = brand.Slug, SortOrder = brand.SortOrder, IsActive = brand.IsActive };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        var brand = id is null ? new Brand() : await db.Brands.FindAsync(id);
        if (brand is null)
        {
            return NotFound();
        }

        var name = Input.Name.Trim();
        var slug = Slug.From(Input.Slug ?? "", 110);
        if (await db.Brands.AnyAsync(b => b.Name == name && b.Id != brand.Id))
        {
            ModelState.AddModelError("Input.Name", "Бренд с таким названием уже есть.");
        }
        if (slug.Length > 0 && await db.Brands.AnyAsync(b => b.Slug == slug && b.Id != brand.Id))
        {
            ModelState.AddModelError("Input.Slug", "Такой ярлык уже занят другим брендом.");
        }
        if (!ModelState.IsValid)
        {
            return Page();
        }

        brand.Name = name;
        // Пустой ярлык заполнится из названия при сохранении.
        brand.Slug = slug;
        brand.SortOrder = Input.SortOrder;
        brand.IsActive = Input.IsActive;
        if (brand.Id == 0)
        {
            db.Brands.Add(brand);
        }
        await db.SaveChangesAsync();
        TempData.Flash($"Бренд «{brand.Name}» сохранён.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var brand = await db.Brands.FindAsync(id);
        if (brand is null)
        {
            return NotFound();
        }
        // Категории бренда удаляются вместе с ним, а товары нет: их сначала надо перенести или удалить.
        var products = await db.Products.CountAsync(p => p.Category.BrandId == id);
        if (products > 0)
        {
            TempData.Flash($"Нельзя удалить бренд «{brand.Name}»: в его категориях есть товары ({products}). "
                + "Сначала перенесите их в другую категорию или удалите.", FlashLevel.Error);
            return RedirectToPage(new { id });
        }
        db.Brands.Remove(brand);
        await db.SaveChangesAsync();
        TempData.Flash($"Бренд «{brand.Name}» удалён.");
        return RedirectToPage("Index");
    }
}
