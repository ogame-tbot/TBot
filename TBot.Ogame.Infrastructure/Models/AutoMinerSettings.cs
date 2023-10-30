using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class AutoMinerSettings {
		public bool OptimizeForStart { get; set; }
		public bool PrioritizeRobotsAndNanites { get; set; }
		public float MaxDaysOfInvestmentReturn { get; set; }
		public int DepositHours { get; set; }
		public bool BuildDepositIfFull { get; set; }
		public int DeutToLeaveOnMoons { get; set; }
		public bool BuildSolarSatellites { get; set; }
		public AutoMinerSettings() {
			OptimizeForStart = true;
			PrioritizeRobotsAndNanites = false;
			MaxDaysOfInvestmentReturn = 36500;
			DepositHours = 6;
			BuildDepositIfFull = false;
			DeutToLeaveOnMoons = 1000000;
			BuildSolarSatellites = true;
		}
	}

}
