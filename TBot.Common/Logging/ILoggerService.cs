using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Events;

namespace TBot.Common.Logging {
	public interface ILoggerService<T> {
		void WriteLog(LogLevel level, LogSender source, string message);
		void ConfigureLogging(string logPath);
		void AddTelegramLogger(string botToken, string chatId);
		void RemoveTelegramLogger();
		bool IsTelegramLoggerEnabled();
		void SetTelegramLoggerLogLevel(LogEventLevel logLevel);
		LogEventLevel GetTelegramLoggerLevel();
	}
}
