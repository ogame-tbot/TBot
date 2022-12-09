using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class AttackerFleet {
		public int ID { get; set; }
		public Missions MissionType { get; set; }
		public Coordinate Origin { get; set; }
		public Coordinate Destination { get; set; }
		public string DestinationName { get; set; }
		public DateTime ArrivalTime { get; set; }
		public int ArriveIn { get; set; }
		public string AttackerName { get; set; }
		public int AttackerID { get; set; }
		public int UnionID { get; set; }
		public int Missiles { get; set; }
		public Ships Ships { get; set; }

		public bool IsOnlyProbes() {
			if (Ships.EspionageProbe != 0) {
				return Ships.Battlecruiser == 0
					&& Ships.Battleship == 0
					&& Ships.Bomber == 0
					&& Ships.ColonyShip == 0
					&& Ships.Cruiser == 0
					&& Ships.Deathstar == 0
					&& Ships.Destroyer == 0
					&& Ships.HeavyFighter == 0
					&& Ships.LargeCargo == 0
					&& Ships.LightFighter == 0
					&& Ships.Pathfinder == 0
					&& Ships.Reaper == 0
					&& Ships.Recycler == 0
					&& Ships.SmallCargo == 0
					&& Ships.SolarSatellite == 0;
			} else
				return false;
		}
	}

}
