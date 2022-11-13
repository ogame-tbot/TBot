using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Model {
	public enum IntervalType {
		LessThanASecond,
		LessThanFiveSeconds,
		AFewSeconds,
		SomeSeconds,
		AMinuteOrTwo,
		AboutFiveMinutes,
		AboutTenMinutes,
		AboutAQuarterHour,
		AboutHalfAnHour,
		AboutAnHour
	}
}
