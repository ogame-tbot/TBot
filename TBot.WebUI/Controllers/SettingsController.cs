using Microsoft.AspNetCore.Mvc;
using TBot.WebUI.Models;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using System.Dynamic;
using Tbot.Common.Settings;
using Newtonsoft.Json.Linq;

namespace TBot.WebUI.Controllers {
	public class SettingsController : Controller {
		private string GetCurrentDirectory() {
			return AppDomain.CurrentDomain.BaseDirectory;
		}
		public static bool DoesPropertyExist(dynamic settings, string name) {
			if (settings is ExpandoObject)
				return ((IDictionary<string, object>) settings).ContainsKey(name);

			return settings.GetType().GetProperty(name) != null;
		}
		private async Task<List<string>> GetFileNames() {
			var fileNames = new List<string>() { new FileInfo(SettingsService.GlobalSettingsPath).Name };
			var settingsFile = SettingsService.GetSettings(SettingsService.GlobalSettingsPath);

			if (!SettingsService.IsSettingSet(settingsFile, "Instances")) {
				return fileNames;
			}

			foreach(var instance in settingsFile.Instances) {
				fileNames.Add(instance.Settings);
			}

			return fileNames;
		}

		[HttpGet]
		public async Task<IActionResult> Index() {
			var settingsModel = new SettingsModel();
			settingsModel.SettingsFiles = await GetFileNames();
			return View(settingsModel);
		}

		[HttpGet]
		public async Task<IActionResult> GetFileContent(string fileName) {
			var fileContents = await System.IO.File.ReadAllTextAsync(Path.Combine(GetCurrentDirectory(), fileName));
			return Json(new { data = fileContents });
		}

		[HttpPost]
		public async Task<IActionResult> SaveFileContent(string fileName, string content) {
			try {
				//Validate Json, if incorrect, exception is thrown and message will be sent to the user.
				string jsonFormatted = JValue.Parse(content).ToString(Formatting.Indented);
				await System.IO.File.WriteAllTextAsync(Path.Combine(GetCurrentDirectory(), fileName), jsonFormatted);
				return Json(new { success = true });
			}
			catch (Exception ex) {
				return Json(new { success = false, error = ex.Message });
			}
		}
	}
}
