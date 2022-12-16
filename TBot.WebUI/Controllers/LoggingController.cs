using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using CsvHelper;
using CsvHelper.Configuration;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Tbot.Common.Settings;
using TBot.Common.Logging;
using TBot.WebUI.Models;

namespace TBot.WebUI.Controllers {
	public class LoggingController : Controller {
		private readonly ILoggerService<LoggingController> _loggerService;

		public LoggingController(ILoggerService<LoggingController> loggerService) {
			_loggerService= loggerService;
		}

		private class LogEntry {
			public string type { get; set; }
			public string datetime { get; set; }
			public string message { get; set; }
			public string sender { get; set; }
			public int position { get; set; }
			[JsonIgnore]
			public DateTime DateTimeParsed {
				get {
					var result = DateTime.MinValue;
					DateTime.TryParse(datetime, CultureInfo.InvariantCulture, DateTimeStyles.None, out result);
					return result;
				}
			}
		}

		private sealed class LogEntryMap : ClassMap<LogEntry> {
			public LogEntryMap() {
				AutoMap(CultureInfo.InvariantCulture);
				Map(m => m.position).Ignore();
			}
		}
		private async Task<List<LogEntry>> GetLogsFromCSV(DateTime logsDate) {
			List<LogEntry> result = new List<LogEntry>();
			try {
				var filePath = Path.Combine(SettingsService.LogsPath, $"TBot{logsDate.ToString("yyyyMMdd")}.csv");
				if (System.IO.File.Exists(filePath)) {
					using var fs = System.IO.File.Open(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
					using var sr = new StreamReader(fs, Encoding.Default);
					using var csv = new CsvReader(sr, CultureInfo.InvariantCulture);
					csv.Context.RegisterClassMap<LogEntryMap>();
					var asyncResult = csv.GetRecordsAsync<LogEntry>();
					result = new List<LogEntry>();
					await foreach (var entry in asyncResult) {
						result.Add(entry);
					}
					int i = 0;
					result.ForEach(c => c.position = i++);
					return result;
				}
				return result;
			} catch {
				return result;
			}
		}

		public async Task<IActionResult> Index(string date) {
			var logsDate = DateTime.Now.Date;
			if (!string.IsNullOrEmpty(date))
				DateTime.TryParseExact(date, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out logsDate);
			return View(new LogJson() {
				Date = logsDate.ToString("yyyy-MM-dd")
			});
		}

		private int GetLogLevelNum(string logLevel) {
			switch (logLevel) {
				case "Trace":
					return 0;
				case "Debug":
					return 1;
				case "Information":
					return 2;
				case "Warning":
					return 3;
				case "Error":
					return 4;
				default:
					return 0;
			}
		}

		public async Task<IActionResult> GetLogs(string date, string lastTime, string search, string logSender, string logLevel) {
			var logsDate = DateTime.Now.Date;
			if (!string.IsNullOrEmpty(date))
				DateTime.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out logsDate);

			var jsonLogs = await GetLogsFromCSV(logsDate);
			var jsonLogsQuery = jsonLogs.AsQueryable();

			if (!string.IsNullOrEmpty(search)) {
				jsonLogsQuery = jsonLogsQuery.Where(c => c.message.Contains(search, StringComparison.InvariantCultureIgnoreCase));
			}

			if (!string.IsNullOrEmpty(logLevel)) {
				var selectedLogLevel = GetLogLevelNum(logLevel);
				jsonLogsQuery = jsonLogsQuery.Where(c => GetLogLevelNum(c.type) >= selectedLogLevel);
			}

			if (!string.IsNullOrEmpty(logSender) && logSender != "All") {
				jsonLogsQuery = jsonLogsQuery.Where(c => string.Equals(c.sender, logSender, StringComparison.InvariantCultureIgnoreCase));
			}

			if (!string.IsNullOrEmpty(lastTime)) {
				logsDate = DateTime.ParseExact(lastTime, "dd/MM/yyyy, H:mm:ss", CultureInfo.InvariantCulture);
				jsonLogsQuery = jsonLogsQuery.Where(c => c.DateTimeParsed < logsDate);
			}

			jsonLogs = jsonLogsQuery.OrderByDescending(c => c.position).ToList();

			var result = jsonLogs
				.Take(100).ToList();

			if (result.Any()) {
				var last = result.Last();
				var sameTimeToAdd = jsonLogs.Where(c => c.datetime == last.datetime && !result.Contains(c));
				result.AddRange(sameTimeToAdd);
			}

			return Json(new {
				Content = result,
				HasMoreData = result.Count != jsonLogs.Count
			});
		}
	}
}
