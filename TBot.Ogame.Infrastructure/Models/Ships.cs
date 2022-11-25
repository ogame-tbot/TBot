using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class Ships {
		public long LightFighter { get; set; }
		public long HeavyFighter { get; set; }
		public long Cruiser { get; set; }
		public long Battleship { get; set; }
		public long Battlecruiser { get; set; }
		public long Bomber { get; set; }
		public long Destroyer { get; set; }
		public long Deathstar { get; set; }
		public long SmallCargo { get; set; }
		public long LargeCargo { get; set; }
		public long ColonyShip { get; set; }
		public long Recycler { get; set; }
		public long EspionageProbe { get; set; }
		public long SolarSatellite { get; set; }
		public long Crawler { get; set; }
		public long Reaper { get; set; }
		public long Pathfinder { get; set; }

		public Ships(
			long lightFighter = 0,
			long heavyFighter = 0,
			long cruiser = 0,
			long battleship = 0,
			long battlecruiser = 0,
			long bomber = 0,
			long destroyer = 0,
			long deathstar = 0,
			long smallCargo = 0,
			long largeCargo = 0,
			long colonyShip = 0,
			long recycler = 0,
			long espionageProbe = 0,
			long solarSatellite = 0,
			long crawler = 0,
			long reaper = 0,
			long pathfinder = 0
		) {
			LightFighter = lightFighter;
			HeavyFighter = heavyFighter;
			Cruiser = cruiser;
			Battleship = battleship;
			Battlecruiser = battlecruiser;
			Bomber = bomber;
			Destroyer = destroyer;
			Deathstar = deathstar;
			SmallCargo = smallCargo;
			LargeCargo = largeCargo;
			ColonyShip = colonyShip;
			Recycler = recycler;
			EspionageProbe = espionageProbe;
			SolarSatellite = solarSatellite;
			Crawler = crawler;
			Reaper = reaper;
			Pathfinder = pathfinder;
		}

		public bool IsEmpty() {
			return LightFighter == 0
				&& HeavyFighter == 0
				&& Cruiser == 0
				&& Battleship == 0
				&& Battlecruiser == 0
				&& Bomber == 0
				&& Destroyer == 0
				&& Deathstar == 0
				&& SmallCargo == 0
				&& LargeCargo == 0
				&& ColonyShip == 0
				&& Recycler == 0
				&& EspionageProbe == 0
				&& SolarSatellite == 0
				&& Crawler == 0
				&& Reaper == 0
				&& Pathfinder == 0;
		}

		public bool IsOnlyProbes() {
			return LightFighter == 0
				&& HeavyFighter == 0
				&& Cruiser == 0
				&& Battleship == 0
				&& Battlecruiser == 0
				&& Bomber == 0
				&& Destroyer == 0
				&& Deathstar == 0
				&& SmallCargo == 0
				&& LargeCargo == 0
				&& ColonyShip == 0
				&& Recycler == 0
				&& SolarSatellite == 0
				&& Crawler == 0
				&& Reaper == 0
				&& Pathfinder == 0
				&& EspionageProbe != 0;
		}

		public long GetFleetPoints() {
			long output = 0;
			output += LightFighter * 4;
			output += HeavyFighter * 10;
			output += Cruiser * 29;
			output += Battleship * 60;
			output += Battlecruiser * 85;
			output += Bomber * 90;
			output += Destroyer * 125;
			output += Deathstar * 10000;
			output += SmallCargo * 4;
			output += LargeCargo * 12;
			output += ColonyShip * 40;
			output += Recycler * 18;
			output += EspionageProbe * 1;
			output += Reaper * 160;
			output += Pathfinder * 31;
			return output;
		}

		public bool HasMovableFleet() {
			return !IsEmpty();
		}

		public Ships GetMovableShips() {
			Ships tempShips = this;
			tempShips.SolarSatellite = 0;
			tempShips.Crawler = 0;
			return tempShips;
		}

		public Ships Add(Buildables buildable, long quantity) {
			foreach (PropertyInfo prop in this.GetType().GetProperties()) {
				if (prop.Name == buildable.ToString()) {
					prop.SetValue(this, (long) prop.GetValue(this) + quantity);
				}
			}
			return this;
		}

		public Ships Remove(Buildables buildable, int quantity) {
			foreach (PropertyInfo prop in this.GetType().GetProperties()) {
				if (prop.Name == buildable.ToString()) {
					long val = (long) prop.GetValue(this);
					if (val >= quantity)
						prop.SetValue(this, val);
					else
						prop.SetValue(this, 0);
				}
			}
			return this;
		}

		public long GetAmount(Buildables buildable) {
			foreach (PropertyInfo prop in this.GetType().GetProperties()) {
				if (prop.Name == buildable.ToString()) {
					return (long) prop.GetValue(this);
				}
			}
			return 0;
		}

		public void SetAmount(Buildables buildable, long number) {
			foreach (PropertyInfo prop in this.GetType().GetProperties()) {
				if (prop.Name == buildable.ToString()) {
					prop.SetValue(this, number);
					return;
				}
			}
		}

		public bool HasAtLeast(Ships ships, long times = 1) {
			foreach (PropertyInfo prop in this.GetType().GetProperties()) {
				if ((long) prop.GetValue(this) * times < (long) prop.GetValue(ships)) {
					return false;
				}
			}
			return true;
		}

		public override string ToString() {
			string output = "";
			foreach (PropertyInfo prop in this.GetType().GetProperties()) {
				if ((long) prop.GetValue(this) == 0)
					continue;
				output += $"{prop.Name}: {prop.GetValue(this)}; ";
			}
			return output;
		}
	}

}
