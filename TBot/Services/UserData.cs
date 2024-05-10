using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Model;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Services {

	// Data required by TBotMain instances
	public class UserData {
		public Server serverInfo = new();
		public ServerData serverData;
		public UserInfo userInfo;
		public AllianceClass allianceClass;
		public List<Celestial> celestials;
		public List<Fleet> fleets;
		public List<AttackerFleet> attacks;
		public Slots slots;
		public Researches researches;

		public List<FleetSchedule> scheduledFleets;
		public List<FarmTarget> farmTargets;
		public Dictionary<Coordinate, DateTime> discoveryBlackList;
		public float lastDOIR;
		public float nextDOIR;
		public Staff staff;
		public bool isSleeping = false;
	}

	// Data used by TelegramMessenger binded to TBotMain
	public class TelegramUserData {
		public Celestial CurrentCelestial;			// Willingly left to null
		public Celestial CurrentCelestialToSave;	// Willingly left to null
		public Missions Mission = Missions.None;
	}
}
