using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TBot.Common
{
    public interface ILoggerService<T>
    {
		void Log(LogLevel level, LogSender source, string message);
    }
}
