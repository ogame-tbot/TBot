using System.Linq;
using Tbot.Includes;

namespace Tbot.Services {
	public static class CmdLineArgsService {
		public static void DoParse(string[] args) {
			int argLen = args.Length;

			for (int i = 0; i < argLen; i++) {
				string cArg = args.ElementAt(i);
				if(cArg == "--help") {
					printHelp = true;
				} else if (cArg.Contains("--settings")) {
					string userInput = GetUserValue(cArg);
					if (userInput.Length > 0) {
						settingsPath.Set(userInput);
					}
				} else if (cArg.Contains("--log")) {
					string userInput = GetUserValue(cArg);
					if (userInput.Length > 0) {
						logPath.Set(userInput);
					}
				}
			}
		}

		private static string GetUserValue(string argument) {
			string value = "";
			string[] splitted = argument.Split('=');
			if (splitted.Length == 2) {
				value = splitted[1];
			}
			return value;
		}

		public static bool printHelp = false;
		public static Optional<string> settingsPath = Optional<string>.Empty();
		public static Optional<string> logPath = Optional<string>.Empty();

		public static string helpStr = @"
			--help Prints this help
			--settings=<settings filepath>
			--log=<logpath>
		";
	}
}

