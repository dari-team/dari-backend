using Microsoft.AspNetCore.Mvc;

namespace DARI_API.Controllers
{
    public class HomeController1 : Controller
    {
        public IActionResult Index()
        {
            return View();
        }
    }
}
