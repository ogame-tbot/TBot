using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog.Core;
using Serilog.Events;

namespace TBot.Common.Logging.Enrichers {
	public class ThreadIdEnricher : ILogEventEnricher {
		public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory) {
			logEvent.AddPropertyIfAbsent(propertyFactory.CreateProperty(
					"ThreadId", Thread.CurrentThread.ManagedThreadId));
		}
	}
}
