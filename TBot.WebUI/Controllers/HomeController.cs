using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using TBot.Common.Logging;
using TBot.WebUI.Models;

namespace TBot.WebUI.Controllers {
	public class HomeController : Controller {
		private readonly ILoggerService<HomeController> _logger;

		public HomeController(ILoggerService<HomeController> logger) {
			_logger = logger;
		}

		public IActionResult Index() {
			return RedirectToAction("Index", "Settings");
		}

		[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
		public IActionResult Error() {
			return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
		}
	}
}
