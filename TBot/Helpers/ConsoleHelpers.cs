using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Tbot.Helpers {
	public static class ConsoleHelpers {
		public static void SetTitle(string content = "") {
			AssemblyName exeInfo = Assembly.GetExecutingAssembly().GetName();
			string info = $"{exeInfo.Name} v{exeInfo.Version}";
			Console.Title = (content != "") ? $"{content} - {info}" : info;
			return;
		}

		public static void PlayAlarm() {
			Console.Beep();
			Thread.Sleep(1000);
			Console.Beep();
			Thread.Sleep(1000);
			Console.Beep();
			return;
		}
	}
}
