using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Serilog.Events;
using Serilog.Formatting;

namespace TBot.Common.Logging.TextFormatters {
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
