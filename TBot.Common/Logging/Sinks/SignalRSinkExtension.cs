using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Serilog.Configuration;
using Serilog.Events;
using Serilog.Sinks.AspNetCore.SignalR.Interfaces;
using Serilog;

namespace TBot.Common.Logging.Sinks {
	public static class SignalRSinkExtension {

		/// <summary>
		/// The SignalRSink Extension, which you need to add to the sinks collection.
		/// </summary>
		/// <param name="loggerConfiguration">The logger sink configuration.</param>
		/// <param name="logEventLevel">The minimum logLevel.</param>
		/// <param name="serviceProvider">The current serviceProvider.</param>
		/// <param name="formatProvider">The format provider to use.</param>
		/// <param name="groups">The groups to which the log events are sent.</param>
		/// <param name="userIds">The users to which the log events are sent.</param>
		/// <param name="excludedConnectionIds">The excluded ids from the dispatch.</param>
		/// <param name="sendAsString">A bool to decide as what the log should be send.</param>
		/// <typeparam name="THub">The type of the SignalR Hub.</typeparam>
		/// <typeparam name="T">The type of the SignalR typed interface.</typeparam>
		/// <returns>The instance of LoggerConfiguration.</returns>
		/// <exception cref="ArgumentNullException"></exception>
		public static LoggerConfiguration SignalRTBotSink<THub, T>(
			this LoggerSinkConfiguration loggerConfiguration,
			LogEventLevel logEventLevel,
			IServiceProvider serviceProvider = null,
			IFormatProvider formatProvider = null,
			string[] groups = null,
			string[] userIds = null,
			string[] excludedConnectionIds = null,
			bool sendAsString = false
		) where THub : Hub<T> where T : class, IHub {
			if (loggerConfiguration == null) {
				throw new ArgumentNullException(nameof(loggerConfiguration));
			}
			if (serviceProvider == null) {
				throw new ArgumentNullException(nameof(serviceProvider));

			}
			return loggerConfiguration.Sink(new SignalRTbotSink<THub, T>(formatProvider,
					sendAsString,
					serviceProvider,
					groups,
					userIds,
					excludedConnectionIds),
				logEventLevel);
		}
	}
}
