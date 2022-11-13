using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog.Sinks.File;

namespace TBot.Common.Logging.Hooks {
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
