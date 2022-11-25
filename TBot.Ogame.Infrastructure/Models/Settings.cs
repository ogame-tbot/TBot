using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Settings {
		public bool AKS { get; set; }
		public int FleetSpeed { get; set; }
		public bool WreckField { get; set; }
		public string ServerLabel { get; set; }
		public int EconomySpeed { get; set; }
		public int PlanetFields { get; set; }
		public int UniverseSize { get; set; }
		public string ServerCategory { get; set; }
		public bool EspionageProbeRaids { get; set; }
		public int PremiumValidationGift { get; set; }
		public int DebrisFieldFactorShips { get; set; }
		public int DebrisFieldFactorDefence { get; set; }
	}

}
