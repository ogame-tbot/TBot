using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Enums {
	public enum SendFleetCode : int {
		GenericError = 0,
		AfterSleepTime = -1,
		NotEnoughSlots = -2,
		QuickerToWaitForProduction = -3,
		NotEnoughRessources = -4		
	}
}
