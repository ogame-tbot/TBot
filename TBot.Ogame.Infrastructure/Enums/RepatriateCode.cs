using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Enums {
	public enum RepatriateCode : int {
		Success = 1,
		Stop = 0,
		Failure = -1,
		Delay = -2
	}
}
