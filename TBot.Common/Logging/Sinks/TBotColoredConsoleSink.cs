using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog.Configuration;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;

namespace TBot.Common.Logging.Sinks {
	public class TBotColoredConsoleSink : ILogEventSink {
		private readonly ConsoleColor _defaultForeground = Console.ForegroundColor;
		private readonly ConsoleColor _defaultBackground = Console.BackgroundColor;

		private readonly ITextFormatter _formatter;

		public TBotColoredConsoleSink(ITextFormatter formatter) {
			_formatter = formatter;
		}

		public LogSender GetLogSender(LogEvent logEvent) {
			if (logEvent.Properties.ContainsKey("LogSender")) {
				if (Enum.TryParse<LogSender>(logEvent.Properties["LogSender"].ToString(), out LogSender sender) == true) {
					return sender;
				}
			}
			return LogSender.Main;
		}

		public void Emit(LogEvent logEvent) {
			LogEventLevel level = logEvent.Level;
			LogSender sender = GetLogSender(logEvent);

			ConsoleColor consoleColor = sender switch {
				LogSender.Brain => ConsoleColor.Blue,
				LogSender.AutoCargo => ConsoleColor.Blue,
				LogSender.AutoMine => ConsoleColor.Blue,
				LogSender.AutoRepatriate => ConsoleColor.Blue,
				LogSender.AutoResearch => ConsoleColor.Blue,
				LogSender.BuyOfferOfTheDay => ConsoleColor.Blue,
				LogSender.LifeformsAutoMine => ConsoleColor.Blue,
				LogSender.LifeformsAutoResearch => ConsoleColor.Blue,
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
			Console.ForegroundColor = level == LogEventLevel.Information
				? consoleColor
				: level switch {
					LogEventLevel.Fatal => ConsoleColor.DarkRed,
					LogEventLevel.Error => ConsoleColor.Red,
					LogEventLevel.Warning => ConsoleColor.Yellow,
					LogEventLevel.Debug => ConsoleColor.White,
					_ => ConsoleColor.Gray
				};

			_formatter.Format(logEvent, Console.Out);
			Console.Out.Flush();

			Console.ForegroundColor = _defaultForeground;
			Console.BackgroundColor = _defaultBackground;
		}
	}
}
