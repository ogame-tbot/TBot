using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class FleetSchedule : FleetHypotesis {
		public Resources Payload { get; set; }
		public DateTime Departure { get; set; }
		public DateTime Arrival { get; set; }
		public DateTime Comeback { get; set; }
		public DateTime SendAt { get; set; }
		public DateTime RecallAt { get; set; }
		public DateTime ReturnAt { get; set; }
	}
}
