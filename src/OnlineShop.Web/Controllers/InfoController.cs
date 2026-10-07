using Microsoft.AspNetCore.Mvc;

namespace OnlineShop.Web.Controllers;

/// <summary>Информационные страницы сайта.</summary>
public class InfoController : Controller
{
    public const string PrivacyUrl = "/privacy";

    [HttpGet(PrivacyUrl)]
    public IActionResult Privacy() => View();
}
