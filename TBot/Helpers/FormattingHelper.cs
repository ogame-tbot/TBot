using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Tbot.Helpers {
	public static class FormattingHelper {

		public static long ParseDurationFromString(string timeString) {
			long duration = 0;
			string regExp = "^(\\d{1,2}[h|H])?(\\d{1,2}[m|M])?(\\d{1,2}[s|S])?";

			Regex re = new Regex(regExp);
			Match m = re.Match(timeString);

			if (m.Groups.Count == 4) {
				int hours = m.Groups[1].Success ? Int32.Parse(m.Groups[1].Value.Remove(m.Groups[1].Value.Length - 1)) : 0;
				int mins = m.Groups[2].Success ? Int32.Parse(m.Groups[2].Value.Remove(m.Groups[2].Value.Length - 1)) : 0;
				int secs = m.Groups[3].Success ? Int32.Parse(m.Groups[3].Value.Remove(m.Groups[3].Value.Length - 1)) : 0;

				duration = (hours * 60 * 60) + (mins * 60) + secs;
			} else {
				throw new Exception($"Invalid string {timeString}");
			}

			return duration;
		}

		public static string TimeSpanToString(TimeSpan delta) {

			return string.Format("{0} days {1:00}:{2:00}:{3:00}", delta.Days, delta.Hours, delta.Minutes, delta.Seconds);
		}
	}
}
