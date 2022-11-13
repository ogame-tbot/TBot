using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Constructions {
		public int BuildingID { get; set; }
		public int BuildingCountdown { get; set; }
		public int ResearchID { get; set; }
		public int ResearchCountdown { get; set; }
		public int LFBuildingID { get; set; }
		public int LFBuildingCountdown { get; set; }
		public int LFResearchID { get; set; }
		public int LFResearchCountdown { get; set; }
	}
}
