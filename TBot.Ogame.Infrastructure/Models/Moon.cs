using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Moon : Celestial {
		public bool HasLunarFacilities(Facilities facilities) {
			return Facilities.LunarBase >= facilities.LunarBase
				&& Facilities.SensorPhalanx >= facilities.SensorPhalanx
				&& Facilities.JumpGate >= facilities.JumpGate
				&& Facilities.Shipyard >= facilities.Shipyard
				&& Facilities.RoboticsFactory >= facilities.RoboticsFactory;
		}
	}
}
