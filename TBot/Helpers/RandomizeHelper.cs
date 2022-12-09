using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Model;

namespace Tbot.Helpers {
	public static class RandomizeHelper {
		public static int CalcRandomInterval(IntervalType type) {
			var rand = new Random();
			return type switch {
				IntervalType.LessThanASecond => rand.Next(500, 1000),
				IntervalType.LessThanFiveSeconds => rand.Next(1000, 5000),
				IntervalType.AFewSeconds => rand.Next(5000, 15000),
				IntervalType.SomeSeconds => rand.Next(20000, 50000),
				IntervalType.AMinuteOrTwo => rand.Next(40000, 140000),
				IntervalType.AboutFiveMinutes => rand.Next(240000, 360000),
				IntervalType.AboutTenMinutes => rand.Next(540000, 720000),
				IntervalType.AboutAQuarterHour => rand.Next(840000, 960000),
				IntervalType.AboutHalfAnHour => rand.Next(1500000, 2100000),
				IntervalType.AboutAnHour => rand.Next(3000000, 42000000),
				_ => rand.Next(500, 1000),
			};
		}

		public static int CalcRandomInterval(int min, int max) {
			var rand = new Random();
			var minMillis = min * 60 * 1000;
			var maxMillis = max * 60 * 1000;
			return rand.Next(minMillis, maxMillis);
		}

	}
}
