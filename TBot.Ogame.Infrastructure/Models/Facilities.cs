using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Facilities {
		public int RoboticsFactory { get; set; }
		public int Shipyard { get; set; }
		public int ResearchLab { get; set; }
		public int AllianceDepot { get; set; }
		public int MissileSilo { get; set; }
		public int NaniteFactory { get; set; }
		public int Terraformer { get; set; }
		public int SpaceDock { get; set; }
		public int LunarBase { get; set; }
		public int SensorPhalanx { get; set; }
		public int JumpGate { get; set; }

		public override string ToString() {
			return $"R: {RoboticsFactory.ToString()} S: {Shipyard.ToString()} L: {ResearchLab.ToString()} M: {MissileSilo.ToString("")} N: {NaniteFactory.ToString("")}";
		}
	}

}
