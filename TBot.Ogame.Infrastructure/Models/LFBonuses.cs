using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBonuses {
		public LFBonusesProduction Production { get; set; }
		public LFBonusesExpeditions Expeditions { get; set; }
		public LFBonusesDens Dens { get; set; }
		public LFBonusesMoons Moons { get; set; }
		public LFBonusesCrawlers Crawlers { get; set; }
		public Dictionary<int, LFBonusesShip> Ships { get; set; }
		public Dictionary<int, LFBonusesShip> Defenses { get; set; }
		public Dictionary<int, LFBonusesBase> Buildings { get; set; }
		public Dictionary<int, LFBonusesBase> Researches { get; set; }
		public Dictionary<int, LFBonusesBase> LfBuildings { get; set; }
		public Dictionary<int, LFBonusesBase> LfResearches { get; set; }

		public float PhalanxRange { get; set; }
		public float RecallRefund { get; set; }
		public float FleetSlots { get; set; }
		public float Explorations { get; set; }
		public float SpaceDock { get; set; }
		public float PlanetSize { get; set; }
		public float InactivesLoot { get; set; }

	}
}
