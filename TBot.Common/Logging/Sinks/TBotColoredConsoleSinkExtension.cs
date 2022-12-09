using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Formatting.Display;
using Serilog;

namespace TBot.Common.Logging.Sinks {
	public static class TBotColoredConsoleSinkExtension {
		public static LoggerConfiguration TBotColoredConsole(
			this LoggerSinkConfiguration loggerConfiguration,
			LogEventLevel minimumLevel = LogEventLevel.Verbose,
			string outputTemplate = "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}",
			IFormatProvider formatProvider = null) {
			return loggerConfiguration.Sink(new TBotColoredConsoleSink(new MessageTemplateTextFormatter(outputTemplate, formatProvider)), minimumLevel);
		}
	}
}
