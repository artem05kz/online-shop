using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using OnlineShop.Web.Data;
using OnlineShop.Web.Infrastructure;
using OnlineShop.Web.Models;

namespace OnlineShop.Web.Pages.Admin.Categories;

public class EditModel(ShopDbContext db) : PageModel
{
    public class InputModel
    {
        [Display(Name = "Бренд")]
        [Required(ErrorMessage = "Выберите бренд.")]
        public int? BrandId { get; set; }

        [Display(Name = "Родительская категория")]
        public int? ParentId { get; set; }

        [Display(Name = "Название")]
        [Required(ErrorMessage = "Обязательное поле."), StringLength(200, ErrorMessage = "Не длиннее {1} символов.")]
        public string Name { get; set; } = "";

        [Display(Name = "Ярлык для ссылки")]
        [StringLength(220, ErrorMessage = "Не длиннее {1} символов.")]
        public string? Slug { get; set; }

        [Display(Name = "Очерёдность в списке категорий")]
        [Range(0, int.MaxValue, ErrorMessage = "Число не меньше 0.")]
        public int SortOrder { get; set; }

        [Display(Name = "Показывать на сайте")]
        public bool IsActive { get; set; } = true;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public int? Id { get; private set; }
    public IReadOnlyList<Brand> Brands { get; private set; } = [];
    public IReadOnlyList<Category> Parents { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        Id = id;
        if (id is not null)
        {
            var category = await db.Categories.FindAsync(id);
            if (category is null)
            {
                return NotFound();
            }
            Input = new InputModel
            {
                BrandId = category.BrandId, ParentId = category.ParentId, Name = category.Name,
                Slug = category.Slug, SortOrder = category.SortOrder, IsActive = category.IsActive,
            };
        }
        await LoadChoices();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        Id = id;
        var category = id is null ? new Category() : await db.Categories.FindAsync(id);
        if (category is null)
        {
            return NotFound();
        }

        var name = Input.Name.Trim();
        var slug = Slug.From(Input.Slug ?? "", 220);
        if (Input.ParentId is { } parentId)
        {
            var parent = await db.Categories.FindAsync(parentId);
            if (parent is null || parent.BrandId != Input.BrandId)
            {
                ModelState.AddModelError("Input.ParentId", "Родительская категория должна быть того же бренда.");
            }
            else if (category.Id != 0 && await IsSelfOrDescendant(parentId, category.Id))
            {
                ModelState.AddModelError("Input.ParentId", "Категория не может быть вложена сама в себя.");
            }
        }
        if (await db.Categories.AnyAsync(c => c.BrandId == Input.BrandId && c.ParentId == Input.ParentId
            && c.Name == name && c.Id != category.Id))
        {
            ModelState.AddModelError("Input.Name", "Такая категория у этого бренда уже есть.");
        }
        if (slug.Length > 0 && await db.Categories.AnyAsync(c => c.BrandId == Input.BrandId && c.Slug == slug && c.Id != category.Id))
        {
            ModelState.AddModelError("Input.Slug", "Такой ярлык уже занят другой категорией этого бренда.");
        }
        if (!ModelState.IsValid)
        {
            await LoadChoices();
            return Page();
        }

        category.BrandId = Input.BrandId!.Value;
        category.ParentId = Input.ParentId;
        category.Name = name;
        category.Slug = slug;
        category.SortOrder = Input.SortOrder;
        category.IsActive = Input.IsActive;
        if (category.Id == 0)
        {
            db.Categories.Add(category);
        }
        await db.SaveChangesAsync();
        TempData.Flash($"Категория «{category.Name}» сохранена.");
        return RedirectToPage("Index");
    }

    public async Task<IActionResult> OnPostDeleteAsync(int id)
    {
        var category = await db.Categories.FindAsync(id);
        if (category is null)
        {
            return NotFound();
        }
        // Вложенные категории удаляются вместе с этой, а товары нет: их надо сначала перенести.
        var ids = await SubtreeIds(id);
        var products = await db.Products.CountAsync(p => ids.Contains(p.CategoryId));
        if (products > 0)
        {
            TempData.Flash($"Нельзя удалить категорию «{category.Name}»: в ней или во вложенных категориях есть товары ({products}). "
                + "Сначала перенесите их в другую категорию или удалите.", FlashLevel.Error);
            return RedirectToPage(new { id });
        }
        db.Categories.Remove(category);
        await db.SaveChangesAsync();
        TempData.Flash($"Категория «{category.Name}» удалена.");
        return RedirectToPage("Index");
    }

    private async Task LoadChoices()
    {
        Brands = await db.Brands.Ordered().AsNoTracking().ToListAsync();
        Parents = await db.Categories.Include(c => c.Brand)
            .Where(c => c.Id != Id)
            .OrderBy(c => c.Brand.SortOrder).ThenBy(c => c.Brand.Name).ThenBy(c => c.SortOrder).ThenBy(c => c.Name)
            .AsNoTracking().ToListAsync();
    }

    private async Task<List<int>> SubtreeIds(int rootId)
    {
        var all = await db.Categories.Select(c => new { c.Id, c.ParentId }).ToListAsync();
        var result = new List<int> { rootId };
        for (var i = 0; i < result.Count; i++)
        {
            result.AddRange(all.Where(c => c.ParentId == result[i]).Select(c => c.Id));
        }
        return result;
    }

    private async Task<bool> IsSelfOrDescendant(int candidateParentId, int categoryId) =>
        (await SubtreeIds(categoryId)).Contains(candidateParentId);
}
