using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CsvHelper;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Tbot.Common.Settings;
using TBot.WebUI.Models;

namespace TBot.WebUI.Controllers {
	public class LoggingController : Controller {
		private class LogEntry {
			public string type { get; set; }
			public string datetime { get; set; }
			public string message { get; set; }
			public string sender { get; set; }
		}
		private async Task<string> GetLogsFromCSV(DateTime logsDate) {
			try {
				var filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "log", $"TBot{logsDate.ToString("yyyyMMdd")}.csv");
				if (System.IO.File.Exists(filePath)) {
					using var fs = System.IO.File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					using var sr = new StreamReader(fs, Encoding.Default);
					using var csv = new CsvReader(sr, CultureInfo.InvariantCulture);
					var asyncResult = csv.GetRecordsAsync<LogEntry>();
					List<LogEntry> result = new List<LogEntry>();
					await foreach (var entry in asyncResult) {
						result.Add(entry);
					}
					return JsonConvert.SerializeObject(result);
				}
				return "[]";
			} catch {
				return "[]";
			}
		}
		private int GetMaxLogsToShow() {
			var settings = SettingsService.GetSettings(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json"));
			return (int) settings.WebUI.MaxLogsToShow;
		}

		public async Task<IActionResult> Index(string date) {
			var logsDate = DateTime.Now.Date;
			if (!string.IsNullOrEmpty(date))
				DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out logsDate);
			var jsonLogs = await GetLogsFromCSV(logsDate);
			var maxLogsToShow = GetMaxLogsToShow();
			return View(new LogJson() {
				Content = jsonLogs,
				MaxLogsToShow = maxLogsToShow,
				Date = logsDate.ToString("yyyy-MM-dd")
			});
		}
	}
}
