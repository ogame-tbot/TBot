using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog;
using TBot.Common.Logging.Hooks;
using TBot.Common.Logging.TextFormatters;
using TBot.Common.Logging.Sinks;
using System.Reflection;
using Serilog.Events;

namespace TBot.Common.Logging {
	public class LoggerService<T> : ILoggerService<T> {

		private Dictionary<LogSender, Dictionary<LogLevel, Serilog.ILogger>> _contextLoggers = new();

		private object syncObject = new object();
		private Serilog.ILogger GetLogger(LogLevel level, LogSender sender) {
			if (!_contextLoggers.ContainsKey(sender)) {
				lock (syncObject) {
					if (!_contextLoggers.ContainsKey(sender)) {
						_contextLoggers.Add(sender, new Dictionary<LogLevel, Serilog.ILogger>());
					}
				}
			}
			if (!_contextLoggers[sender].ContainsKey(level)) {
				lock (syncObject) {
					if (!_contextLoggers[sender].ContainsKey(level)) {
						var logger = Log.ForContext("LogSender", sender)
									.ForContext("LogSenderEmoji", EmojiFormatter.GetEmoji(sender.ToString()))
									.ForContext("LogLevel", level)
									.ForContext("LogLevelEmoji", EmojiFormatter.GetEmoji(level.ToString()));
						_contextLoggers[sender].Add(level, logger);
					}
				}
			}
			return _contextLoggers[sender][level];
		}

		public void WriteLog(LogLevel level, LogSender sender, string message) {
			var logger = GetLogger(level, sender);

			switch (level) {
				case LogLevel.Trace:
					logger.Verbose(message);
					break;
				case LogLevel.Debug:
					logger.Debug(message);
					break;
				case LogLevel.Information:
					logger.Information(message);
					break;

				case LogLevel.Warning:
					logger.Warning(message);
					break;
				case LogLevel.Error:
					logger.Error(message);
					break;
				case LogLevel.Critical:
					logger.Fatal(message);
					break;
			}
		}
	}
}
