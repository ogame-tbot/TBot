using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class ServerData {
		public string Name { get; set; }
		public int Number { get; set; }
		public string Language { get; set; }
		public string Timezone { get; set; }
		public string TimezoneOffset { get; set; }
		public string Domain { get; set; }
		public string Version { get; set; }
		public int Speed { get; set; }
		public int SpeedFleet { get; set; }
		public int SpeedFleetPeaceful { get; set; }
		public int SpeedFleetWar { get; set; }
		public int SpeedFleetHolding { get; set; }
		public int Galaxies { get; set; }
		public int Systems { get; set; }
		public bool ACS { get; set; }
		public bool RapidFire { get; set; }
		public bool DefToTF { get; set; }
		public float DebrisFactor { get; set; }
		public float DebrisFactorDef { get; set; }
		public float RepairFactor { get; set; }
		public int NewbieProtectionLimit { get; set; }
		public int NewbieProtectionHigh { get; set; }
		public float TopScore { get; set; }
		public int BonusFields { get; set; }
		public bool DonutGalaxy { get; set; }
		public bool DonutSystem { get; set; }
		public bool WfEnabled { get; set; }
		public int WfMinimumRessLost { get; set; }
		public int WfMinimumLossPercentage { get; set; }
		public int WfBasicPercentageRepairable { get; set; }
		public float GlobalDeuteriumSaveFactor { get; set; }
		public int Bashlimit { get; set; }
		public int ProbeCargo { get; set; }
		public int ResearchDurationDivisor { get; set; }
		public int DarkMatterNewAcount { get; set; }
		public int CargoHyperspaceTechMultiplier { get; set; }
		public int SpeedResearch {
			get {
				return Speed * ResearchDurationDivisor;
			}
		}
	}

}
