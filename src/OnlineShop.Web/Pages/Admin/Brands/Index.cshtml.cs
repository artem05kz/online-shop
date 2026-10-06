using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace OnlineShop.Web.Pages.Admin.Brands;

/// <summary>Бренды теперь в общем дереве «Бренды и категории», старый адрес ведёт туда.</summary>
public class IndexModel : PageModel
{
    public IActionResult OnGet() => Redirect("/admin/categories");
}
