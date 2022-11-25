using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class FleetHypotesis {
		public Celestial Origin { get; set; }
		public Coordinate Destination { get; set; }
		public Ships Ships { get; set; }
		public Missions Mission { get; set; }
		public decimal Speed { get; set; }
		public long Duration { get; set; }
		public long Fuel { get; set; }
	}
}
