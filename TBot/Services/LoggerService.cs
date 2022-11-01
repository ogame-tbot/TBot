using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Serilog.Configuration;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting;
using Serilog.Formatting.Display;
using Serilog.Sinks.File;
using Tbot.Model;
using static Tbot.Services.LoggerService.FileHooks;
using static Tbot.Services.LoggerService.Sinks;
using static Tbot.Services.LoggerService.TextFormatters;

namespace Tbot.Services {
	public class LoggerService {

		public class Enrichers
		{
			// ThreadID
			public class ThreadIdEnricher : ILogEventEnricher {
				public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
					logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
							"ThreadId", Thread.CurrentThread.ManagedThreadId));
				}
			}
		}

		public class TextFormatters
		{
			// CSV
			public class SerilogCSVTextFormatter : ITextFormatter {
				public void Format(LogEvent logEvent, TextWriter output) {
					string message;
					// TYPE;Sender;DateTime;Message
					if (logEvent.Exception != null) {
						// Log exception
						message = $"EXCEPTION:{logEvent.Exception.ToString()}. {logEvent.MessageTemplate.ToString()}";
					} else {
						message = $"{logEvent.MessageTemplate.ToString()}";
					}
					output.Write("{0},{1},{2},{3}{4}",
						EscapeForCSV(logEvent.Level.ToString()),
						EscapeForCSV(logEvent.Properties["LogSender"].ToString()),
						EscapeForCSV(DateTime.Now.ToString()),
						EscapeForCSV(message),
						output.NewLine);
				}

				public static string EscapeForCSV(string str) {
					// Taken from https://stackoverflow.com/questions/6377454/escaping-tricky-string-to-csv-format
					bool mustQuote = (str.Contains(",") || str.Contains("\"") || str.Contains("\r") || str.Contains("\n"));
					if (mustQuote) {
						StringBuilder sb = new StringBuilder();
						sb.Append("\"");
						foreach (char nextChar in str) {
							sb.Append(nextChar);
							if (nextChar == '"')
								sb.Append("\"");
						}
						sb.Append("\"");
						return sb.ToString();
					}

					return str;
				}
			}
		}

		public class FileHooks
		{
			// CSV Header
			public class SerilogCSVHeaderHooks : FileLifecycleHooks {
				public override Stream OnFileOpened(string path, Stream underlyingStream, Encoding encoding) {
					// Write header only if length == 0
					if (underlyingStream.Length == 0) {
						using (var writer = new StreamWriter(underlyingStream, encoding, 1024, true)) {
							writer.WriteLine("type,sender,datetime,message");
							writer.Flush();
							underlyingStream.Flush();
						}
					}

					return base.OnFileOpened(underlyingStream, encoding);
				}
			}
		}

		public class Sinks {
			// https://github.com/serilog/serilog-sinks-console/issues/35
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

		public class Logger {

			private static Dictionary<LogSender, Serilog.ILogger> _contextLoggers = new();

			public Logger(string logPath) {
				string outTemplate = "[{Timestamp:HH:mm:ss.fff zzz} {ThreadId} {Level:u3} {LogSender}] {Message:lj}{NewLine}{Exception}";
				long maxFileSize = 1 * 1024 * 1024 * 10;
				Log.Logger = new LoggerConfiguration()
				.MinimumLevel.Debug()
				.Enrich.With(new Enrichers.ThreadIdEnricher())
				// Console
				.WriteTo.TBotColoredConsole(
					outputTemplate: outTemplate
				)
				// Log file
				.WriteTo.File(
					path: Path.Combine(logPath, "TBot.log"),
					buffered: false,
					flushToDiskInterval: TimeSpan.FromHours(1),
					rollOnFileSizeLimit: true,
					fileSizeLimitBytes: maxFileSize,
					retainedFileCountLimit: 10,
					rollingInterval: RollingInterval.Day)
				// CSV
				.WriteTo.File(
					path: Path.Combine(logPath, "TBot.csv"),
					buffered: false,
					hooks: new SerilogCSVHeaderHooks(),
					formatter: new SerilogCSVTextFormatter(),
					flushToDiskInterval: TimeSpan.FromHours(1),
					rollOnFileSizeLimit: true,
					fileSizeLimitBytes: maxFileSize,
					rollingInterval: RollingInterval.Day)
				.CreateLogger();
			}

			~Logger() {

			}

			public static void WriteLog(LogType type, LogSender sender, string message) {
				if (!_contextLoggers.TryGetValue(sender, out ILogger logger)) {
					logger = Log.ForContext("LogSender", sender);
					_contextLoggers.Add(sender, logger);
				}

				switch (type) {
					case LogType.Debug:
						logger.Debug(message);
						break;

					case LogType.Info:
						logger.Information(message);
						break;

					case LogType.Warning:
						logger.Warning(message);
						break;
					case LogType.Error:
						logger.Error(message);
						break;
				}
			}
		}
	}

	// Extension method for Serilog
	public static class ColoredConsoleSinkExtensions {
		public static LoggerConfiguration TBotColoredConsole(
			this LoggerSinkConfiguration loggerConfiguration,
			LogEventLevel minimumLevel = LogEventLevel.Verbose,
			string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
			IFormatProvider formatProvider = null) {
			return loggerConfiguration.Sink(new TBotColoredConsoleSink(new MessageTemplateTextFormatter(outputTemplate, formatProvider)), minimumLevel);
		}
	}
}
