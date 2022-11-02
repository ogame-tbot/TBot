using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TBot.Common {
	public class LoggerService<T> : ILoggerService<T> {
		public void Log(LogLevel level, LogSender source, string message) {
			LogToConsole(level, source, message);
		}

		private void LogToConsole(LogLevel level, LogSender sender, string message) {
			ConsoleColor consoleColor = sender switch {
				LogSender.Brain => ConsoleColor.Blue,
				LogSender.Defender => ConsoleColor.DarkGreen,
				LogSender.Expeditions => ConsoleColor.Cyan,
				LogSender.FleetScheduler => ConsoleColor.DarkMagenta,
				LogSender.Harvest => ConsoleColor.Green,
				LogSender.Colonize => ConsoleColor.DarkRed,
				LogSender.AutoFarm => ConsoleColor.DarkCyan,
				LogSender.SleepMode => ConsoleColor.DarkBlue,
				LogSender.Tbot => ConsoleColor.DarkYellow,
				LogSender.Main => ConsoleColor.Yellow,
				LogSender.OGameD => ConsoleColor.DarkCyan,
				_ => ConsoleColor.Gray
			};
			Console.ForegroundColor = level == LogLevel.Information
				? consoleColor
				: level switch {
					LogLevel.Error => ConsoleColor.Red,
					LogLevel.Warning => ConsoleColor.Yellow,
					LogLevel.Debug => ConsoleColor.White,
					_ => ConsoleColor.Gray
				};

			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}|{level.ToString()}|{sender.ToString()}] {message}");
			Console.ForegroundColor = ConsoleColor.Gray;
		}
	}
}
