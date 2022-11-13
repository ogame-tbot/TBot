using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Enums {
	public static class Features {
		public static readonly List<Feature> AllFeatures = new List<Feature>() {
						Feature.Defender,
						Feature.Brain,
						Feature.BrainAutobuildCargo,
						Feature.BrainAutoRepatriate,
						Feature.BrainAutoMine,
						Feature.BrainLifeformAutoMine,
						Feature.BrainLifeformAutoResearch,
						Feature.BrainOfferOfTheDay,
						Feature.BrainAutoResearch,
						Feature.AutoFarm,
						Feature.Expeditions,
						Feature.Colonize,
						Feature.Harvest,
						Feature.FleetScheduler,
						Feature.SleepMode,
					};
	}
}
