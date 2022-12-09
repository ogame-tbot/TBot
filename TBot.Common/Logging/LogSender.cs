using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Common.Logging {
	public enum LogSender {
		Main,
		Tbot,
		OGameD,
		Defender,
		Brain,
		//Brain Features
		AutoCargo,
		AutoMine,
		AutoRepatriate,
		AutoResearch,
		BuyOfferOfTheDay,
		LifeformsAutoMine,
		LifeformsAutoResearch,
		//End Brain Features
		Expeditions,
		Harvest,
		FleetScheduler,
		SleepMode,
		Colonize,
		AutoFarm,
		Telegram
	}
}
