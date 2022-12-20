using System.Dynamic;
using Microsoft.AspNetCore.Mvc;
using Tbot.Common.Settings;
using TBot.WebUI.Models;

namespace TBot.WebUI.Controllers {
	public class OgameController : Controller {

		private string GetCurrentDirectory() {
			return AppDomain.CurrentDomain.BaseDirectory;
		}

		private async Task<InstanceListModel> GetInstancesList() {
			var instancesList = new InstanceListModel();

			var settingsFile = await SettingsService.GetSettings(SettingsService.GlobalSettingsPath);

			if (!SettingsService.IsSettingSet(settingsFile, "Instances")) {
				var singleInstance = new InstanceModel() {
					Alias = "MAIN",
					OgameUrl = $"http://{settingsFile.General.Host}:{settingsFile.General.Port}/game/index.php"
				};
				instancesList.Instances.Add(singleInstance);
			}
			else {
				foreach (var instance in settingsFile.Instances) {
					var instanceSettingsFile = await SettingsService.GetSettings(Path.Combine(GetCurrentDirectory(), instance.Settings));
					var instanceModel = new InstanceModel() {
						Alias = instance.Alias,
						OgameUrl = $"http://{instanceSettingsFile.General.Host}:{instanceSettingsFile.General.Port}/game/index.php"
					};
					instancesList.Instances.Add(instanceModel);
				}
			}

			return instancesList;
		}

		public async Task<IActionResult> Index() {
			var instances = await GetInstancesList();
			return View(instances);
		}
	}
}
