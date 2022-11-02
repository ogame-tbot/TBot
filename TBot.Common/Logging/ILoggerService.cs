using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TBot.Common.Logging {
	public interface ILoggerService<T> {
		void WriteLog(LogLevel level, LogSender source, string message);
	}
}
