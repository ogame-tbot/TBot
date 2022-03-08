using Tbot;
using Tbot.Model;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Linq;
using System.IO;
using Tbot.Services;

namespace Tbot.Includes {

	static class Helpers {
		public static void WriteLog(LogType type, LogSender sender, string message) {
			LogToConsole(type, sender, message);
			LogToFile(type, sender, message);
			LogToCSV(type, sender, message);
		}

		public static void LogToConsole(LogType type, LogSender sender, string message) {
			Console.ForegroundColor = type == LogType.Info
				? sender switch {
					LogSender.Brain => ConsoleColor.Blue,
					LogSender.Defender => ConsoleColor.DarkGreen,
					LogSender.Expeditions => ConsoleColor.Cyan,
					LogSender.FleetScheduler => ConsoleColor.DarkMagenta,
					LogSender.Harvest => ConsoleColor.Green,
					LogSender.Colonize => ConsoleColor.DarkRed,
					LogSender.AutoFarm => ConsoleColor.DarkCyan,
					LogSender.SleepMode => ConsoleColor.DarkBlue,
					LogSender.Tbot => ConsoleColor.DarkYellow,
					_ => ConsoleColor.Gray
				}
				: type switch {
					LogType.Error => ConsoleColor.Red,
					LogType.Warning => ConsoleColor.Yellow,
					LogType.Debug => ConsoleColor.White,
					_ => ConsoleColor.Gray
				};

			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}|{type.ToString()}|{sender.ToString()}] {message}");
			Console.ForegroundColor = ConsoleColor.Gray;
		}

		public static void LogToFile(LogType type, LogSender sender, string message) {
			string path = $"{Directory.GetCurrentDirectory()}/log";
			DirectoryInfo dir = new(path);
			if (!dir.Exists)
				dir.Create();
			string fileName = $"{DateTime.Now.Year.ToString()}{DateTime.Now.Month.ToString()}{DateTime.Now.Day.ToString()}_TBot.log";
			try {
				StreamWriter file = new($"{path}/{fileName}", true);
				file.WriteLine($"[{type.ToString()}] [{sender.ToString()}] [{DateTime.Now.ToString()}] - {message}");
				file.Close();
			} catch (Exception) { }
		}

		public static void LogToCSV(LogType type, LogSender sender, string message) {
			string path = $"{Directory.GetCurrentDirectory()}/log";
			DirectoryInfo dir = new(path);
			if (!dir.Exists)
				dir.Create();
			string fileName = "TBot_log.csv";
			try {
				StreamWriter file = new($"{path}/{fileName}", true);
				file.WriteLine($"{type.ToString().EscapeForCSV()},{sender.ToString().EscapeForCSV()},{DateTime.Now.ToString().EscapeForCSV()},{message.EscapeForCSV()}");
				file.Close();
			} catch (Exception) { }
		}

		public static void SetTitle(string content = "") {
			AssemblyName exeInfo = Assembly.GetExecutingAssembly().GetName();
			string info = $"{exeInfo.Name} v{exeInfo.Version}";
			Console.Title = (content != "") ? $"{content} - {info}" : info;
			return;
		}

		public static void PlayAlarm() {
			Console.Beep();
			Thread.Sleep(1000);
			Console.Beep();
			Thread.Sleep(1000);
			Console.Beep();
			return;
		}

		public static int CalcRandomInterval(IntervalType type) {
			var rand = new Random();
			return type switch {
				IntervalType.LessThanASecond => rand.Next(500, 1000),
				IntervalType.AFewSeconds => rand.Next(5000, 15000),
				IntervalType.SomeSeconds => rand.Next(20000, 50000),
				IntervalType.AMinuteOrTwo => rand.Next(40000, 140000),
				IntervalType.AboutFiveMinutes => rand.Next(240000, 360000),
				IntervalType.AboutTenMinutes => rand.Next(540000, 720000),
				IntervalType.AboutAQuarterHour => rand.Next(840000, 960000),
				IntervalType.AboutHalfAnHour => rand.Next(1500000, 2100000),
				IntervalType.AboutAnHour => rand.Next(3000000, 42000000),
				_ => rand.Next(500, 1000),
			};
		}

		public static int CalcRandomInterval(int min, int max) {
			var rand = new Random();
			var minMillis = min * 60 * 1000;
			var maxMillis = max * 60 * 1000;
			return rand.Next(minMillis, maxMillis);
		}

		public static int CalcShipCapacity(Buildables buildable, int hyperspaceTech, CharacterClass playerClass, int probeCargo = 0) {
			int baseCargo;
			int bonus = (hyperspaceTech * 5);
			switch (buildable) {
				case Buildables.SmallCargo:
					baseCargo = 5000;
					if (playerClass == CharacterClass.Collector)
						bonus += 25;
					break;
				case Buildables.LargeCargo:
					baseCargo = 25000;
					if (playerClass == CharacterClass.Collector)
						bonus += 25;
					break;
				case Buildables.LightFighter:
					baseCargo = 50;
					break;
				case Buildables.HeavyFighter:
					baseCargo = 100;
					break;
				case Buildables.Cruiser:
					baseCargo = 800;
					break;
				case Buildables.Battleship:
					baseCargo = 1500;
					break;
				case Buildables.ColonyShip:
					baseCargo = 7500;
					break;
				case Buildables.Recycler:
					baseCargo = 20000;
					if (playerClass == CharacterClass.General)
						bonus += 25;
					break;
				case Buildables.EspionageProbe:
					baseCargo = probeCargo;
					break;
				case Buildables.Bomber:
					baseCargo = 750;
					break;
				case Buildables.Destroyer:
					baseCargo = 2000;
					break;
				case Buildables.Deathstar:
					baseCargo = 1000000;
					break;
				case Buildables.Battlecruiser:
					baseCargo = 750;
					break;
				case Buildables.Reaper:
					baseCargo = 7000;
					break;
				case Buildables.Pathfinder:
					baseCargo = 10000;
					if (playerClass == CharacterClass.General)
						bonus += 25;
					break;
				default:
					return 0;
			}
			return baseCargo * (bonus + 100) / 100;
		}

		public static int CalcShipFuelCapacity(Buildables buildable, int probeCargo = 0) {
			int baseCargo;
			switch (buildable) {
				case Buildables.SmallCargo:
					baseCargo = 5000;
					break;
				case Buildables.LargeCargo:
					baseCargo = 25000;
					break;
				case Buildables.LightFighter:
					baseCargo = 50;
					break;
				case Buildables.HeavyFighter:
					baseCargo = 100;
					break;
				case Buildables.Cruiser:
					baseCargo = 800;
					break;
				case Buildables.Battleship:
					baseCargo = 1500;
					break;
				case Buildables.ColonyShip:
					baseCargo = 7500;
					break;
				case Buildables.Recycler:
					baseCargo = 20000;
					break;
				case Buildables.EspionageProbe:
					baseCargo = probeCargo;
					break;
				case Buildables.Bomber:
					baseCargo = 750;
					break;
				case Buildables.Destroyer:
					baseCargo = 2000;
					break;
				case Buildables.Deathstar:
					baseCargo = 1000000;
					break;
				case Buildables.Battlecruiser:
					baseCargo = 750;
					break;
				case Buildables.Reaper:
					baseCargo = 7000;
					break;
				case Buildables.Pathfinder:
					baseCargo = 10000;
					break;
				default:
					return 0;
			}
			return baseCargo;
		}

		public static long CalcFleetCapacity(Ships fleet, int hyperspaceTech, CharacterClass playerClass, int probeCargo = 0) {
			long total = 0;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					int oneCargo = CalcShipCapacity(buildable, hyperspaceTech, playerClass, probeCargo);
					total += oneCargo * qty;
				}
			}
			return total;
		}

		public static long CalcFleetFuelCapacity(Ships fleet, int probeCargo = 0) {
			long total = 0;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					int oneCargo = CalcShipFuelCapacity(buildable, probeCargo);
					total += oneCargo * qty;
				}
			}
			return total;
		}

		public static int CalcShipSpeed(Buildables buildable, Researches researches, CharacterClass playerClass) {
			return CalcShipSpeed(buildable, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, playerClass);
		}

		public static int CalcShipSpeed(Buildables buildable, int combustionDrive, int impulseDrive, int hyperspaceDrive, CharacterClass playerClass) {
			int baseSpeed;
			int bonus = combustionDrive;
			switch (buildable) {
				case Buildables.SmallCargo:
					baseSpeed = 5000;
					if (impulseDrive >= 5) {
						baseSpeed = 10000;
						bonus = impulseDrive * 2;
					}
					if (playerClass == CharacterClass.Collector)
						bonus += 10;
					break;
				case Buildables.LargeCargo:
					baseSpeed = 7500;
					if (playerClass == CharacterClass.Collector)
						bonus += 10;
					break;
				case Buildables.LightFighter:
					baseSpeed = 12500;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.HeavyFighter:
					baseSpeed = 10000;
					bonus = impulseDrive * 2;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.Cruiser:
					baseSpeed = 15000;
					bonus = impulseDrive * 2;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.Battleship:
					baseSpeed = 10000;
					bonus = hyperspaceDrive * 3;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.ColonyShip:
					bonus = impulseDrive * 2;
					baseSpeed = 2500;
					break;
				case Buildables.Recycler:
					baseSpeed = 2000;
					if (impulseDrive >= 17) {
						baseSpeed = 4000;
						bonus = impulseDrive * 2;
					}
					if (hyperspaceDrive >= 15) {
						baseSpeed = 6000;
						bonus = hyperspaceDrive * 3;
					}
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.EspionageProbe:
					baseSpeed = 100000000;
					break;
				case Buildables.Bomber:
					baseSpeed = 4000;
					bonus = impulseDrive * 2;
					if (hyperspaceDrive >= 8) {
						baseSpeed = 5000;
						bonus = hyperspaceDrive * 3;
					}
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.Destroyer:
					baseSpeed = 5000;
					bonus = hyperspaceDrive * 3;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.Deathstar:
					baseSpeed = 100;
					bonus = hyperspaceDrive * 3;
					break;
				case Buildables.Battlecruiser:
					baseSpeed = 10000;
					bonus = hyperspaceDrive * 3;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.Reaper:
					baseSpeed = 10000;
					bonus = hyperspaceDrive * 3;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.Pathfinder:
					baseSpeed = 10000;
					bonus = hyperspaceDrive * 3;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				default:
					return 0;
			}
			return (int) Math.Round(((float) baseSpeed * ((float) bonus + 10) / 10), MidpointRounding.ToZero);
		}

		public static int CalcSlowestSpeed(Ships fleet, Researches researches, CharacterClass playerClass) {
			return CalcSlowestSpeed(fleet, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, playerClass);
		}

		public static int CalcSlowestSpeed(Ships fleet, int combustionDrive, int impulseDrive, int hyperspaceDrive, CharacterClass playerClass) {
			int lowest = int.MaxValue;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);

				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler)
						continue;
					int speed = CalcShipSpeed(buildable, combustionDrive, impulseDrive, hyperspaceDrive, playerClass);
					if (speed < lowest)
						lowest = speed;
				}
			}
			return lowest;
		}

		public static int CalcFleetSpeed(Ships fleet, Researches researches, CharacterClass playerClass) {
			return CalcFleetSpeed(fleet, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, playerClass);
		}

		public static int CalcFleetSpeed(Ships fleet, int combustionDrive, int impulseDrive, int hyperspaceDrive, CharacterClass playerClass) {
			int minSpeed = 0;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					int thisSpeed = CalcShipSpeed(buildable, combustionDrive, impulseDrive, hyperspaceDrive, playerClass);
					if (thisSpeed < minSpeed)
						minSpeed = thisSpeed;
				}
			}
			return minSpeed;
		}

		public static int CalcShipConsumption(Buildables buildable, Researches researches, ServerData serverData, CharacterClass playerClass) {
			return CalcShipConsumption(buildable, researches.ImpulseDrive, researches.HyperspaceDrive, serverData.GlobalDeuteriumSaveFactor, playerClass);
		}

		public static int CalcShipConsumption(Buildables buildable, int impulseDrive, int hyperspaceDrive, double deuteriumSaveFactor, CharacterClass playerClass) {
			int baseConsumption;
			switch (buildable) {
				case Buildables.SmallCargo:
					baseConsumption = 20;
					if (impulseDrive >= 5)
						baseConsumption *= 2;
					break;
				case Buildables.LargeCargo:
					baseConsumption = 50;
					break;
				case Buildables.LightFighter:
					baseConsumption = 20;
					break;
				case Buildables.HeavyFighter:
					baseConsumption = 75;
					break;
				case Buildables.Cruiser:
					baseConsumption = 300;
					break;
				case Buildables.Battleship:
					baseConsumption = 500;
					break;
				case Buildables.ColonyShip:
					baseConsumption = 1000;
					break;
				case Buildables.Recycler:
					baseConsumption = 2000;
					if (hyperspaceDrive >= 15)
						baseConsumption *= 3;
					else if (impulseDrive >= 17)
						baseConsumption *= 2;
					break;
				case Buildables.EspionageProbe:
					baseConsumption = 1;
					break;
				case Buildables.Bomber:
					baseConsumption = 700;
					if (hyperspaceDrive >= 8)
						baseConsumption *= 3 / 2;
					break;
				case Buildables.Destroyer:
					baseConsumption = 1000;
					break;
				case Buildables.Deathstar:
					baseConsumption = 1;
					break;
				case Buildables.Battlecruiser:
					baseConsumption = 250;
					break;
				case Buildables.Reaper:
					baseConsumption = 1100;
					break;
				case Buildables.Pathfinder:
					baseConsumption = 300;
					break;
				default:
					return 0;
			}
			float fuelConsumption = (float) (deuteriumSaveFactor * baseConsumption);
			if (playerClass == CharacterClass.General)
				fuelConsumption /= 2;
			return (int) Math.Round(fuelConsumption, MidpointRounding.ToZero);
		}

		public static long CalcFlightTime(Coordinate origin, Coordinate destination, Ships ships, Missions mission, decimal speed, Researches researches, ServerData serverData, CharacterClass playerClass) {
			var fleetSpeed = mission switch {
				Missions.Attack or Missions.FederalAttack or Missions.Destroy => serverData.SpeedFleetWar,
				Missions.FederalDefense => serverData.SpeedFleetHolding,
				_ => serverData.SpeedFleetPeaceful,
			};
			return CalcFlightTime(origin, destination, ships, speed, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem, fleetSpeed, playerClass);
		}

		public static long CalcFlightTime(Coordinate origin, Coordinate destination, Ships ships, decimal speed, int combustionDrive, int impulseDrive, int hyperspaceDrive, int numberOfGalaxies, int numberOfSystems, bool donutGalaxies, bool donutSystems, int fleetSpeed, CharacterClass playerClass) {
			int slowestShipSpeed = CalcSlowestSpeed(ships, combustionDrive, impulseDrive, hyperspaceDrive, playerClass);
			int distance = CalcDistance(origin, destination, numberOfGalaxies, numberOfSystems, donutGalaxies, donutSystems);
			return (long) Math.Round(((3500 / (double) speed * Math.Sqrt((double) distance * 10 / slowestShipSpeed)) + 10) / fleetSpeed, MidpointRounding.AwayFromZero);
		}

		public static long CalcFuelConsumption(Coordinate origin, Coordinate destination, Ships ships, Missions mission, long flightTime, Researches researches, ServerData serverData, CharacterClass playerClass) {
			var fleetSpeed = mission switch {
				Missions.Attack or Missions.FederalAttack or Missions.Destroy => serverData.SpeedFleetWar,
				Missions.FederalDefense => serverData.SpeedFleetHolding,
				_ => serverData.SpeedFleetPeaceful,
			};
			return CalcFuelConsumption(origin, destination, ships, flightTime, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem, fleetSpeed, serverData.GlobalDeuteriumSaveFactor, playerClass);
		}

		public static long CalcFuelConsumption(Coordinate origin, Coordinate destination, Ships ships, long flightTime, int combustionDrive, int impulseDrive, int hyperspaceDrive, int numberOfGalaxies, int numberOfSystems, bool donutGalaxies, bool donutSystems, int fleetSpeed, float deuteriumSaveFactor, CharacterClass playerClass) {
			int distance = CalcDistance(origin, destination, numberOfGalaxies, numberOfSystems, donutGalaxies, donutSystems);
			float tempFuel = 0.0F;
			foreach (PropertyInfo prop in ships.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(ships, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					float tempSpeed = 35000 / ((flightTime * fleetSpeed) - 10) * (float) Math.Sqrt(distance * 10 / CalcShipSpeed(buildable, combustionDrive, impulseDrive, hyperspaceDrive, playerClass));
					int shipConsumption = CalcShipConsumption(buildable, impulseDrive, hyperspaceDrive, deuteriumSaveFactor, playerClass);
					float thisFuel = (float) (shipConsumption * qty * distance) / 35000F * (float) Math.Pow(tempSpeed / 10 + 1, 2);
					tempFuel += thisFuel;
				}
			}
			return (long) (1 + Math.Round(tempFuel, MidpointRounding.AwayFromZero));
		}

		public static FleetPrediction CalcFleetPrediction(Coordinate origin, Coordinate destination, Ships ships, Missions mission, decimal speed, Researches researches, ServerData serverData, CharacterClass playerClass) {
			long time = CalcFlightTime(origin, destination, ships, mission, speed, researches, serverData, playerClass);
			long fuel = CalcFuelConsumption(origin, destination, ships, mission, time, researches, serverData, playerClass);
			return new() {
				Fuel = fuel,
				Time = time
			};
		}

		public static List<decimal> GetValidSpeedsForClass(CharacterClass playerClass) {
			var speeds = new List<decimal>();
			if (playerClass == CharacterClass.General) {
				speeds.Add(Speeds.HundredPercent);
				speeds.Add(Speeds.NinetyfivePercent);
				speeds.Add(Speeds.NinetyPercent);
				speeds.Add(Speeds.EightyfivePercent);
				speeds.Add(Speeds.EightyPercent);
				speeds.Add(Speeds.SeventyfivePercent);
				speeds.Add(Speeds.SeventyPercent);
				speeds.Add(Speeds.SixtyfivePercent);
				speeds.Add(Speeds.SixtyPercent);
				speeds.Add(Speeds.FiftyfivePercent);
				speeds.Add(Speeds.FiftyPercent);
				speeds.Add(Speeds.FourtyfivePercent);
				speeds.Add(Speeds.FourtyPercent);
				speeds.Add(Speeds.ThirtyfivePercent);
				speeds.Add(Speeds.ThirtyPercent);
				speeds.Add(Speeds.TwentyfivePercent);
				speeds.Add(Speeds.TwentyPercent);
				speeds.Add(Speeds.FifteenPercent);
				speeds.Add(Speeds.TenPercent);
				speeds.Add(Speeds.FivePercent);
			} else {
				speeds.Add(Speeds.HundredPercent);
				speeds.Add(Speeds.NinetyPercent);
				speeds.Add(Speeds.EightyPercent);
				speeds.Add(Speeds.SeventyPercent);
				speeds.Add(Speeds.SixtyPercent);
				speeds.Add(Speeds.FiftyPercent);
				speeds.Add(Speeds.FourtyPercent);
				speeds.Add(Speeds.ThirtyPercent);
				speeds.Add(Speeds.TwentyPercent);
				speeds.Add(Speeds.TenPercent);
			}
			return speeds;
		}

		public static decimal CalcOptimalFarmSpeed(OgamedService service, Celestial origin, Coordinate destination, Ships ships, Resources loot, decimal ratio, Researches researches, ServerData serverData, CharacterClass playerClass) {
			var speeds = GetValidSpeedsForClass(playerClass);
			var speedPredictions = new Dictionary<decimal, FleetPrediction>();
			var maxFuel = loot.ConvertedDeuterium * ratio;
			foreach (var speed in speeds) {
				speedPredictions.Add(speed, service.PredictFleet(origin, ships, destination, Missions.Attack, speed));
			}
			return speedPredictions
				.Where(p => p.Value.Fuel < maxFuel)
				.OrderByDescending(p => p.Key)
				.First()
				.Key;
		}

		public static Resources CalcMaxTransportableResources(Ships ships, Resources resources, int hyperspaceTech, CharacterClass playerClass, long deutToLeave = 0, int probeCargo = 0) {
			var capacity = CalcFleetCapacity(ships, hyperspaceTech, playerClass, probeCargo);
			if (resources.TotalResources <= capacity) {
				return new Resources { Deuterium = resources.Deuterium - deutToLeave, Crystal = resources.Crystal, Metal = resources.Metal };
			} else {
				if (resources.Deuterium - deutToLeave > capacity) {
					return new Resources { Deuterium = capacity };
				} else if (capacity >= resources.Deuterium - deutToLeave && capacity < (resources.Deuterium - deutToLeave + resources.Crystal)) {
					return new Resources { Deuterium = resources.Deuterium - deutToLeave, Crystal = (capacity - resources.Deuterium + deutToLeave) };
				} else if (capacity >= (resources.Deuterium - deutToLeave + resources.Crystal) && capacity < resources.TotalResources) {
					return new Resources { Deuterium = resources.Deuterium - deutToLeave, Crystal = resources.Crystal, Metal = (capacity - resources.Deuterium + deutToLeave - resources.Crystal) };
				} else
					return resources;
			}
		}

		public static long CalcShipNumberForPayload(Resources payload, Buildables buildable, int hyperspaceTech, CharacterClass playerClass, int probeCapacity = 0) {
			return (long) Math.Round(((float) payload.TotalResources / (float) CalcShipCapacity(buildable, hyperspaceTech, playerClass, probeCapacity)), MidpointRounding.ToPositiveInfinity);
		}

		public static Ships CalcIdealExpeditionShips(Buildables buildable, int ecoSpeed, long topOnePoints, int hyperspaceTech, CharacterClass playerClass, int probeCargo = 0) {
			var fleet = new Ships();

			int freightCap;
			if (topOnePoints < 10000)
				freightCap = 40000;
			else if (topOnePoints < 100000)
				freightCap = 500000;
			else if (topOnePoints < 1000000)
				freightCap = 1200000;
			else if (topOnePoints < 5000000)
				freightCap = 1800000;
			else if (topOnePoints < 25000000)
				freightCap = 2400000;
			else if (topOnePoints < 50000000)
				freightCap = 3000000;
			else if (topOnePoints < 75000000)
				freightCap = 3600000;
			else if (topOnePoints < 100000000)
				freightCap = 4200000;
			else
				freightCap = 5000000;

			if (playerClass == CharacterClass.Discoverer)
				freightCap = freightCap * ecoSpeed * 3;
			else
				freightCap *= 2;

			int oneCargoCapacity = CalcShipCapacity(buildable, hyperspaceTech, playerClass, probeCargo);
			int cargoNumber = (int) Math.Round((float) freightCap / (float) oneCargoCapacity, MidpointRounding.ToPositiveInfinity);

			fleet = fleet.Add(buildable, cargoNumber);

			return fleet;
		}

		public static Buildables CalcMilitaryShipForExpedition(Ships fleet, int expeditionsNumber) {
			if (fleet.Reaper >= expeditionsNumber)
				return Buildables.Reaper;
			else if (fleet.Destroyer >= expeditionsNumber)
				return Buildables.Destroyer;
			else if (fleet.Bomber >= expeditionsNumber)
				return Buildables.Bomber;
			else if (fleet.Battlecruiser >= expeditionsNumber)
				return Buildables.Battlecruiser;
			else if (fleet.Battleship >= expeditionsNumber)
				return Buildables.Battleship;
			else if (fleet.Pathfinder >= expeditionsNumber)
				return Buildables.Pathfinder;
			else if (fleet.Cruiser >= expeditionsNumber)
				return Buildables.Cruiser;
			else if (fleet.HeavyFighter >= expeditionsNumber)
				return Buildables.HeavyFighter;
			else if (fleet.LightFighter >= expeditionsNumber)
				return Buildables.LightFighter;
			else
				return Buildables.Null;
		}

		public static Ships CalcExpeditionShips(Ships fleet, Buildables primaryShip, int expeditionsNumber, int ecoSpeed, long topOnePoints, int hyperspaceTech, CharacterClass playerClass, int probeCargo = 0) {
			Ships ideal = CalcIdealExpeditionShips(primaryShip, ecoSpeed, topOnePoints, hyperspaceTech, playerClass, probeCargo);
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				if (prop.Name == primaryShip.ToString()) {
					long availableVal = (long) prop.GetValue(fleet);
					long idealVal = (long) prop.GetValue(ideal);
					if (availableVal < idealVal * expeditionsNumber) {
						long realVal = (long) Math.Round(((float) availableVal / (float) expeditionsNumber), MidpointRounding.AwayFromZero);
						prop.SetValue(ideal, realVal);
					}
				}
			}
			return ideal;
		}

		public static Ships CalcExpeditionShips(Ships fleet, Buildables primaryShip, int expeditionsNumber, ServerData serverdata, Researches researches, CharacterClass playerClass, int probeCargo = 0) {
			return CalcExpeditionShips(fleet, primaryShip, expeditionsNumber, serverdata.Speed, serverdata.TopScore, researches.HyperspaceTechnology, playerClass, probeCargo);
		}

		public static bool MayAddShipToExpedition(Ships fleet, Buildables buildable, int expeditionsNumber) {
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				if (prop.Name == buildable.ToString()) {
					long availableVal = (long) prop.GetValue(fleet);
					if (availableVal >= expeditionsNumber)
						return true;
				}
			}
			return false;
		}

		public static Ships CalcFullExpeditionShips(Ships fleet, Buildables primaryShip, int expeditionsNumber, ServerData serverdata, Researches researches, CharacterClass playerClass, int probeCargo = 0) {
			Ships oneExpeditionFleet = CalcExpeditionShips(fleet, primaryShip, expeditionsNumber, serverdata, researches, playerClass, probeCargo);

			if (MayAddShipToExpedition(fleet, Buildables.EspionageProbe, expeditionsNumber))
				oneExpeditionFleet.Add(Buildables.EspionageProbe, 1);

			Buildables militaryShip = CalcMilitaryShipForExpedition(fleet, expeditionsNumber);
			if (MayAddShipToExpedition(fleet, militaryShip, expeditionsNumber))
				oneExpeditionFleet.Add(militaryShip, 1);

			if (MayAddShipToExpedition(fleet, Buildables.Pathfinder, expeditionsNumber))
				if (oneExpeditionFleet.Pathfinder == 0)
					oneExpeditionFleet.Add(Buildables.Pathfinder, 1);

			return oneExpeditionFleet;
		}

		public static int CalcDistance(Coordinate origin, Coordinate destination, int galaxiesNumber, int systemsNumber = 499, bool donutGalaxy = true, bool donutSystem = true) {
			if (origin.Galaxy != destination.Galaxy)
				return CalcGalaxyDistance(origin, destination, galaxiesNumber, donutGalaxy);

			if (origin.System != destination.System)
				return CalcSystemDistance(origin, destination, systemsNumber, donutSystem);

			if (origin.Position != destination.Position)
				return CalcPlanetDistance(origin, destination);

			return 5;
		}

		public static int CalcDistance(Coordinate origin, Coordinate destination, ServerData serverData) {
			return CalcDistance(origin, destination, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem);
		}

		private static int CalcGalaxyDistance(Coordinate origin, Coordinate destination, int galaxiesNumber, bool donutGalaxy = true) {
			if (!donutGalaxy)
				return 20000 * Math.Abs(origin.Galaxy - destination.Galaxy);

			if (origin.Galaxy > destination.Galaxy)
				return 20000 * Math.Min((origin.Galaxy - destination.Galaxy), ((destination.Galaxy + galaxiesNumber) - origin.Galaxy));

			return 20000 * Math.Min((destination.Galaxy - origin.Galaxy), ((origin.Galaxy + galaxiesNumber) - destination.Galaxy));
		}

		private static int CalcSystemDistance(Coordinate origin, Coordinate destination, int systemsNumber, bool donutSystem = true) {
			if (!donutSystem)
				return 2700 + 95 * Math.Abs(origin.System - destination.System);

			if (origin.System > destination.System)
				return 2700 + 95 * Math.Min((origin.System - destination.System), ((destination.System + systemsNumber) - origin.System));

			return 2700 + 95 * Math.Min((destination.System - origin.System), ((origin.System + systemsNumber) - destination.System));

		}

		private static int CalcPlanetDistance(Coordinate origin, Coordinate destination) {
			return 1000 + 5 * Math.Abs(destination.Position - origin.Position);
		}

		public static long CalcMetalProduction(int level, int position, int speedFactor, float ratio = 1, int plasma = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			int baseProd = position switch {
				6 => (int) Math.Round(30 + (30 * 0.17)),
				7 => (int) Math.Round(30 + (30 * 0.23)),
				8 => (int) Math.Round(30 + (30 * 0.35)),
				9 => (int) Math.Round(30 + (30 * 0.23)),
				10 => (int) Math.Round(30 + (30 * 0.17)),
				_ => 30,
			};
			baseProd *= speedFactor;
			if (level == 0)
				return baseProd;
			int prod = (int) Math.Round((float) (baseProd * level * Math.Pow(1.1, level)));
			int plasmaProd = (int) Math.Round(prod * 0.01 * plasma);
			int geologistProd = 0;
			if (hasGeologist) {
				geologistProd = (int) Math.Round(prod * 0.1);
			}
			int staffProd = 0;
			if (hasStaff) {
				staffProd = (int) Math.Round(prod * 0.02);
			}
			int classProd = 0;
			if (playerClass == CharacterClass.Collector) {
				classProd = (int) Math.Round(prod * 0.25);
			}
			return (long) Math.Round((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio, 0);
		}

		public static long CalcMetalProduction(Buildings buildings, int position, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcMetalProduction(buildings.MetalMine, position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
		}

		public static long CalcMetalProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
		}

		public static long CalcCrystalProduction(int level, int position, int speedFactor, float ratio = 1, int plasma = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			int baseProd = position switch {
				1 => (int) Math.Round(20 + (20 * 0.3)),
				2 => (int) Math.Round(20 + (20 * 0.2)),
				3 => (int) Math.Round(20 + (20 * 0.1)),
				_ => 20,
			};
			baseProd *= speedFactor;
			if (level == 0)
				return baseProd;
			int prod = (int) Math.Round((float) (baseProd * level * Math.Pow(1.1, level)));
			int plasmaProd = (int) Math.Round(prod * 0.0066 * plasma);
			int geologistProd = 0;
			if (hasGeologist) {
				geologistProd = (int) Math.Round(prod * 0.1);
			}
			int staffProd = 0;
			if (hasStaff) {
				staffProd = (int) Math.Round(prod * 0.02);
			}
			int classProd = 0;
			if (playerClass == CharacterClass.Collector) {
				classProd = (int) Math.Round(prod * 0.25);
			}
			return (long) Math.Round((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio, 0);
		}

		public static long CalcCrystalProduction(Buildings buildings, int position, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcCrystalProduction(buildings.CrystalMine, position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
		}

		public static long CalcCrystalProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
		}

		public static long CalcDeuteriumProduction(int level, float temp, int speedFactor, float ratio = 1, int plasma = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (level == 0)
				return 0;
			int baseProd = 10 * speedFactor;
			int prod = (int) Math.Round((float) (baseProd * level * Math.Pow(1.1, level) * ((-0.004 * temp) + 1.36)));
			int plasmaProd = (int) Math.Round(prod * 0.0033 * plasma);
			int geologistProd = 0;
			if (hasGeologist) {
				geologistProd = (int) Math.Round(prod * 0.1);
			}
			int staffProd = 0;
			if (hasStaff) {
				staffProd = (int) Math.Round(prod * 0.02);
			}
			int classProd = 0;
			if (playerClass == CharacterClass.Collector) {
				classProd = (int) Math.Round(prod * 0.25);
			}
			return (long) Math.Round((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio, 0);
		}

		public static long CalcDeuteriumProduction(Buildings buildings, Temperature temp, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcDeuteriumProduction(buildings.CrystalMine, temp.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
		}

		public static long CalcDeuteriumProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcDeuteriumProduction(planet.Buildings.CrystalMine, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
		}

		public static Resources CalcPlanetHourlyProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			Resources hourlyProduction = new() {
				Metal = CalcMetalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff),
				Crystal = CalcCrystalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff),
				Deuterium = CalcDeuteriumProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff)
			};
			return hourlyProduction;
		}

		public static Resources CalcPrice(Buildables buildable, int level) {
			Resources output = new();

			switch (buildable) {
				case Buildables.MetalMine:
					output.Metal = (long) Math.Round(60 * Math.Pow(1.5, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					output.Crystal = (long) Math.Round(15 * Math.Pow(1.5, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					// MidpointRounding set to "ToNegativeInfinity" because in all cases that i try (metal 51 crystal 44) the result is always the lower integer
					// Formula: 10 * Mine Level * (1.1 ^ Mine Level)
					output.Energy = (long) Math.Round((10 * level * (Math.Pow(1.1, level))), 0, MidpointRounding.ToPositiveInfinity);
					break;
				case Buildables.CrystalMine:
					output.Metal = (long) Math.Round(48 * Math.Pow(1.6, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					output.Crystal = (long) Math.Round(24 * Math.Pow(1.6, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					// MidpointRounding set to "ToNegativeInfinity" because in all cases that i try (metal 51 crystal 44) the result is always the lower integer
					// Formula: 10 * Mine Level * (1.1 ^ Mine Level)
					output.Energy = (long) Math.Round((10 * level * (Math.Pow(1.1, level))), 0, MidpointRounding.ToPositiveInfinity);
					break;
				case Buildables.DeuteriumSynthesizer:
					output.Metal = (long) Math.Round(225 * Math.Pow(1.5, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					output.Crystal = (long) Math.Round(75 * Math.Pow(1.5, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					// MidpointRounding set to "ToNegativeInfinity" because in all cases that i try (metal 51 crystal 44) the result is always the lower integer
					// Formula: 20 * Mine Level * (1.1 ^ Mine Level)
					output.Energy = (long) Math.Round((20 * level * (Math.Pow(1.1, level))), 0, MidpointRounding.ToPositiveInfinity);
					break;
				case Buildables.SolarPlant:
					output.Metal = (long) Math.Round(75 * Math.Pow(1.5, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					output.Crystal = (long) Math.Round(30 * Math.Pow(1.5, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					break;
				case Buildables.FusionReactor:
					output.Metal = (long) Math.Round(900 * Math.Pow(1.8, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					output.Crystal = (long) Math.Round(360 * Math.Pow(1.8, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					output.Deuterium = (long) Math.Round(180 * Math.Pow(1.8, (level - 1)), 0, MidpointRounding.ToPositiveInfinity);
					break;
				case Buildables.MetalStorage:
					output.Metal = (long) (500 * Math.Pow(2, level));
					break;
				case Buildables.CrystalStorage:
					output.Metal = (long) (500 * Math.Pow(2, level));
					output.Crystal = (long) (250 * Math.Pow(2, level));
					break;
				case Buildables.DeuteriumTank:
					output.Metal = (long) (500 * Math.Pow(2, level));
					output.Crystal = (long) (500 * Math.Pow(2, level));
					break;
				case Buildables.ShieldedMetalDen:
					break;
				case Buildables.UndergroundCrystalDen:
					break;
				case Buildables.SeabedDeuteriumDen:
					break;
				case Buildables.AllianceDepot:
					output.Metal = (long) (20000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (40000 * Math.Pow(2, level - 1));
					break;
				case Buildables.RoboticsFactory:
					output.Metal = (long) (400 * Math.Pow(2, level - 1));
					output.Crystal = (long) (120 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (200 * Math.Pow(2, level - 1));
					break;
				case Buildables.Shipyard:
					output.Metal = (long) (400 * Math.Pow(2, level - 1));
					output.Crystal = (long) (200 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (100 * Math.Pow(2, level - 1));
					break;
				case Buildables.ResearchLab:
					output.Metal = (long) (200 * Math.Pow(2, level - 1));
					output.Crystal = (long) (400 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (200 * Math.Pow(2, level - 1));
					break;
				case Buildables.MissileSilo:
					output.Metal = (long) (20000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (20000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (1000 * Math.Pow(2, level - 1));
					break;
				case Buildables.NaniteFactory:
					output.Metal = (long) (1000000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (500000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (100000 * Math.Pow(2, level - 1));
					break;
				case Buildables.Terraformer:
					output.Crystal = (long) (50000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (100000 * Math.Pow(2, level - 1));
					output.Energy = (long) (1000 * Math.Pow(2, level - 1));
					break;
				case Buildables.SpaceDock:
					output.Metal = (long) (200 * Math.Pow(5, level - 1));
					output.Deuterium = (long) (50 * Math.Pow(5, level - 1));
					output.Energy = (long) Math.Round(50 * Math.Pow(2.5, level - 1), 0, MidpointRounding.ToPositiveInfinity);
					break;
				case Buildables.LunarBase:
					output.Metal = (long) (20000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (40000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (20000 * Math.Pow(2, level - 1));
					break;
				case Buildables.SensorPhalanx:
					output.Metal = (long) (20000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (40000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (20000 * Math.Pow(2, level - 1));
					break;
				case Buildables.JumpGate:
					output.Metal = (long) (2000000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (4000000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (2000000 * Math.Pow(2, level - 1));
					break;
				case Buildables.RocketLauncher:
					output.Metal = (long) (2000 * level);
					break;
				case Buildables.LightLaser:
					output.Metal = (long) (1500 * level);
					output.Crystal = (long) (500 * level);
					break;
				case Buildables.HeavyLaser:
					output.Metal = (long) (6000 * level);
					output.Crystal = (long) (2000 * level);
					break;
				case Buildables.GaussCannon:
					output.Metal = (long) (20000 * level);
					output.Crystal = (long) (15000 * level);
					output.Deuterium = (long) (2000 * level);
					break;
				case Buildables.IonCannon:
					output.Metal = (long) (5000 * level);
					output.Crystal = (long) (3000 * level);
					break;
				case Buildables.PlasmaTurret:
					output.Metal = (long) (50000 * level);
					output.Crystal = (long) (50000 * level);
					output.Deuterium = (long) (30000 * level);
					break;
				case Buildables.SmallShieldDome:
					output.Metal = (long) (10000 * level);
					output.Crystal = (long) (10000 * level);
					break;
				case Buildables.LargeShieldDome:
					output.Metal = (long) (50000 * level);
					output.Crystal = (long) (50000 * level);
					break;
				case Buildables.AntiBallisticMissiles:
					output.Metal = (long) (8000 * level);
					output.Deuterium = (long) (2000 * level);
					break;
				case Buildables.InterplanetaryMissiles:
					output.Metal = (long) (12500 * level);
					output.Crystal = (long) (2500 * level);
					output.Deuterium = (long) (10000 * level);
					break;
				case Buildables.SmallCargo:
					output.Metal = (long) (2000 * level);
					output.Crystal = (long) (2000 * level);
					break;
				case Buildables.LargeCargo:
					output.Metal = (long) (6000 * level);
					output.Crystal = (long) (6000 * level);
					break;
				case Buildables.LightFighter:
					output.Metal = (long) (3000 * level);
					output.Crystal = (long) (1000 * level);
					break;
				case Buildables.HeavyFighter:
					output.Metal = (long) (6000 * level);
					output.Crystal = (long) (4000 * level);
					break;
				case Buildables.Cruiser:
					output.Metal = (long) (20000 * level);
					output.Crystal = (long) (7000 * level);
					output.Deuterium = (long) (2000 * level);
					break;
				case Buildables.Battleship:
					output.Metal = (long) (35000 * level);
					output.Crystal = (long) (15000 * level);
					break;
				case Buildables.ColonyShip:
					output.Metal = (long) (10000 * level);
					output.Crystal = (long) (20000 * level);
					output.Deuterium = (long) (10000 * level);
					break;
				case Buildables.Recycler:
					output.Metal = (long) (10000 * level);
					output.Crystal = (long) (6000 * level);
					output.Deuterium = (long) (2000 * level);
					break;
				case Buildables.EspionageProbe:
					output.Crystal = (long) (1000 * level);
					break;
				case Buildables.Bomber:
					output.Metal = (long) (50000 * level);
					output.Crystal = (long) (25000 * level);
					output.Deuterium = (long) (15000 * level);
					break;
				case Buildables.SolarSatellite:
					output.Crystal = (long) (2000 * level);
					output.Deuterium = (long) (500 * level);
					break;
				case Buildables.Destroyer:
					output.Metal = (long) (60000 * level);
					output.Crystal = (long) (50000 * level);
					output.Deuterium = (long) (15000 * level);
					break;
				case Buildables.Deathstar:
					output.Metal = (long) (5000000 * level);
					output.Crystal = (long) (4000000 * level);
					output.Deuterium = (long) (1000000 * level);
					break;
				case Buildables.Battlecruiser:
					output.Metal = (long) (30000 * level);
					output.Crystal = (long) (40000 * level);
					output.Deuterium = (long) (15000 * level);
					break;
				case Buildables.Crawler:
					output.Metal = (long) (2000 * level);
					output.Crystal = (long) (2000 * level);
					output.Deuterium = (long) (1000 * level);
					break;
				case Buildables.Reaper:
					output.Metal = (long) (85000 * level);
					output.Crystal = (long) (55000 * level);
					output.Deuterium = (long) (20000 * level);
					break;
				case Buildables.Pathfinder:
					output.Metal = (long) (8000 * level);
					output.Crystal = (long) (15000 * level);
					output.Deuterium = (long) (8000 * level);
					break;
				case Buildables.EspionageTechnology:
					output.Metal = (long) (200 * Math.Pow(2, level - 1));
					output.Crystal = (long) (1000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (200 * Math.Pow(2, level - 1));
					break;
				case Buildables.ComputerTechnology:
					output.Crystal = (long) (400 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (600 * Math.Pow(2, level - 1));
					break;
				case Buildables.WeaponsTechnology:
					output.Metal = (long) (800 * Math.Pow(2, level - 1));
					output.Crystal = (long) (200 * Math.Pow(2, level - 1));
					break;
				case Buildables.ShieldingTechnology:
					output.Metal = (long) (200 * Math.Pow(2, level - 1));
					output.Crystal = (long) (600 * Math.Pow(2, level - 1));
					break;
				case Buildables.ArmourTechnology:
					output.Metal = (long) (1000 * Math.Pow(2, level - 1));
					break;
				case Buildables.EnergyTechnology:
					output.Crystal = (long) (800 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (400 * Math.Pow(2, level - 1));
					break;
				case Buildables.HyperspaceTechnology:
					output.Crystal = (long) (4000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (2000 * Math.Pow(2, level - 1));
					break;
				case Buildables.CombustionDrive:
					output.Metal = (long) (400 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (600 * Math.Pow(2, level - 1));
					break;
				case Buildables.ImpulseDrive:
					output.Metal = (long) (2000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (4000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (600 * Math.Pow(2, level - 1));
					break;
				case Buildables.HyperspaceDrive:
					output.Metal = (long) (10000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (20000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (6000 * Math.Pow(2, level - 1));
					break;
				case Buildables.LaserTechnology:
					output.Metal = (long) (200 * Math.Pow(2, level - 1));
					output.Crystal = (long) (100 * Math.Pow(2, level - 1));
					break;
				case Buildables.IonTechnology:
					output.Metal = (long) (1000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (300 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (100 * Math.Pow(2, level - 1));
					break;
				case Buildables.PlasmaTechnology:
					output.Metal = (long) (2000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (4000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (1000 * Math.Pow(2, level - 1));
					break;
				case Buildables.IntergalacticResearchNetwork:
					output.Metal = (long) (240000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (400000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (160000 * Math.Pow(2, level - 1));
					break;
				case Buildables.Astrophysics:
					output.Metal = (long) (4000 * Math.Pow(2, level - 1));
					output.Crystal = (long) (8000 * Math.Pow(2, level - 1));
					output.Deuterium = (long) (4000 * Math.Pow(2, level - 1));
					break;
				case Buildables.GravitonTechnology:
					output.Energy = (long) (300000 * Math.Pow(2, level - 1));
					break;
				case Buildables.Null:
				default:
					break;
			}

			return output;
		}

		public static int CalcCumulativeLabLevel(List<Celestial> celestials, Researches researches) {
			int output = 0;

			if (celestials == null || celestials.Any(c => c.Facilities == null)) {
				return 0;
			}

			output = celestials
				.Where(c => c.Coordinate.Type == Celestials.Planet)
				.OrderByDescending(c => c.Facilities.ResearchLab)
				.Take(researches.IntergalacticResearchNetwork + 1)
				.Sum(c => c.Facilities.ResearchLab);

			return output;
		}

		public static long CalcProductionTime(Buildables buildable, int level, ServerData serverData, Facilities facilities, int cumulativeLabLevel = 0) {
			double output = 1;
			long structuralIntegrity = CalcPrice(buildable, level).StructuralIntegrity;

			switch (buildable) {
				case Buildables.MetalMine:
				case Buildables.CrystalMine:
				case Buildables.DeuteriumSynthesizer:
				case Buildables.SolarPlant:
				case Buildables.FusionReactor:
				case Buildables.MetalStorage:
				case Buildables.CrystalStorage:
				case Buildables.DeuteriumTank:
				case Buildables.ShieldedMetalDen:
				case Buildables.UndergroundCrystalDen:
				case Buildables.SeabedDeuteriumDen:
				case Buildables.AllianceDepot:
				case Buildables.RoboticsFactory:
				case Buildables.Shipyard:
				case Buildables.ResearchLab:
				case Buildables.MissileSilo:
				case Buildables.NaniteFactory:
				case Buildables.Terraformer:
				case Buildables.SpaceDock:
				case Buildables.LunarBase:
				case Buildables.SensorPhalanx:
				case Buildables.JumpGate:
					output = structuralIntegrity / (2500 * (1 + facilities.RoboticsFactory) * serverData.Speed * (long) Math.Pow(2, facilities.NaniteFactory));
					break;

				case Buildables.RocketLauncher:
				case Buildables.LightLaser:
				case Buildables.HeavyLaser:
				case Buildables.GaussCannon:
				case Buildables.IonCannon:
				case Buildables.PlasmaTurret:
				case Buildables.SmallShieldDome:
				case Buildables.LargeShieldDome:
				case Buildables.AntiBallisticMissiles:
				case Buildables.InterplanetaryMissiles:
				case Buildables.SmallCargo:
				case Buildables.LargeCargo:
				case Buildables.LightFighter:
				case Buildables.HeavyFighter:
				case Buildables.Cruiser:
				case Buildables.Battleship:
				case Buildables.ColonyShip:
				case Buildables.Recycler:
				case Buildables.EspionageProbe:
				case Buildables.Bomber:
				case Buildables.SolarSatellite:
				case Buildables.Destroyer:
				case Buildables.Deathstar:
				case Buildables.Battlecruiser:
				case Buildables.Crawler:
				case Buildables.Reaper:
				case Buildables.Pathfinder:
					output = structuralIntegrity / (2500 * (1 + facilities.Shipyard) * serverData.Speed * (long) Math.Pow(2, facilities.NaniteFactory));
					break;

				case Buildables.EspionageTechnology:
				case Buildables.ComputerTechnology:
				case Buildables.WeaponsTechnology:
				case Buildables.ShieldingTechnology:
				case Buildables.ArmourTechnology:
				case Buildables.EnergyTechnology:
				case Buildables.HyperspaceTechnology:
				case Buildables.CombustionDrive:
				case Buildables.ImpulseDrive:
				case Buildables.HyperspaceDrive:
				case Buildables.LaserTechnology:
				case Buildables.IonTechnology:
				case Buildables.PlasmaTechnology:
				case Buildables.IntergalacticResearchNetwork:
				case Buildables.Astrophysics:
				case Buildables.GravitonTechnology:
					if (cumulativeLabLevel == 0) {
						cumulativeLabLevel = facilities.ResearchLab;
					}
					output = structuralIntegrity / (1000 * (1 + cumulativeLabLevel) * serverData.Speed);
					break;

				case Buildables.Null:
				default:
					break;
			}

			return (long) Math.Round(output * 3600, 0, MidpointRounding.ToPositiveInfinity);
		}

		public static long CalcMaxBuildableNumber(Buildables buildable, Resources resources) {
			long output;
			Resources oneItemCost = CalcPrice(buildable, 1);

			long maxPerMet = long.MaxValue;
			long maxPerCry = long.MaxValue;
			long maxPerDeut = long.MaxValue;

			if (oneItemCost.Metal > 0)
				maxPerMet = (long) Math.Round((float) resources.Metal / (float) oneItemCost.Metal, 0, MidpointRounding.ToZero);
			if (oneItemCost.Crystal > 0)
				maxPerCry = (long) Math.Round((float) resources.Crystal / (float) oneItemCost.Crystal, 0, MidpointRounding.ToZero);
			if (oneItemCost.Deuterium > 0)
				maxPerDeut = (long) Math.Round((float) resources.Deuterium / (float) oneItemCost.Deuterium, 0, MidpointRounding.ToZero);

			output = Math.Min(maxPerMet, Math.Min(maxPerCry, maxPerDeut));
			if (output == long.MaxValue)
				output = 0;

			return output;
		}

		public static long GetRequiredEnergyDelta(Buildables buildable, int level) {
			if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
				if (level > 1) {
					var prevLevelResources = CalcPrice(buildable, level - 1);
					var thisLevelResources = CalcPrice(buildable, level);
					return thisLevelResources.Energy - prevLevelResources.Energy;
				} else
					return CalcPrice(buildable, 1).Energy;
			} else
				return 0;
		}

		public static int GetNextLevel(Celestial planet, Buildables buildable, bool isCollector = false, bool hasEngineer = false, bool hasFullStaff = false) {
			int output = 0;
			if (buildable == Buildables.SolarSatellite)
				if (planet is Planet)
					output = CalcNeededSolarSatellites(planet as Planet, Math.Abs(planet.Resources.Energy), isCollector, hasEngineer, hasFullStaff);
			if (output == 0 && planet is Planet) {
				foreach (PropertyInfo prop in planet.Buildings.GetType().GetProperties()) {
					if (prop.Name == buildable.ToString()) {
						output = (int) prop.GetValue(planet.Buildings) + 1;
					}
				}
			}
			if (output == 0) {
				foreach (PropertyInfo prop in planet.Facilities.GetType().GetProperties()) {
					if (prop.Name == buildable.ToString()) {
						output = (int) prop.GetValue(planet.Facilities) + 1;
					}
				}
			}
			return output;
		}

		public static int GetNextLevel(Researches researches, Buildables buildable) {
			int output = 0;
			if (output == 0) {
				foreach (PropertyInfo prop in researches.GetType().GetProperties()) {
					if (prop.Name == buildable.ToString()) {
						output = (int) prop.GetValue(researches) + 1;
					}
				}
			}
			return output;
		}

		public static long CalcDepositCapacity(int level) {
			return 5000 * (long) (2.5 * Math.Pow(Math.E, (20 * level / 33)));
		}

		public static bool ShouldBuildMetalStorage(Planet planet, int maxLevel, int speedFactor, int hours = 12, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool forceIfFull = false) {
			long metalProduction = CalcMetalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff);
			long metalCapacity = CalcDepositCapacity(planet.Buildings.MetalStorage);
			if (forceIfFull && planet.Resources.Metal >= metalCapacity && GetNextLevel(planet, Buildables.MetalStorage) < maxLevel)
				return true;
			if (metalCapacity < hours * metalProduction && GetNextLevel(planet, Buildables.MetalStorage) < maxLevel)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildCrystalStorage(Planet planet, int maxLevel, int speedFactor, int hours = 12, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool forceIfFull = false) {
			long crystalProduction = CalcCrystalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff);
			long crystalCapacity = CalcDepositCapacity(planet.Buildings.CrystalStorage);
			if (forceIfFull && planet.Resources.Crystal >= crystalCapacity && GetNextLevel(planet, Buildables.CrystalStorage) < maxLevel)
				return true;
			if (crystalCapacity < hours * crystalProduction && GetNextLevel(planet, Buildables.CrystalStorage) < maxLevel)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildDeuteriumTank(Planet planet, int maxLevel, int speedFactor, int hours = 12, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool forceIfFull = false) {
			long deuteriumProduction = CalcDeuteriumProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff);
			long deuteriumCapacity = CalcDepositCapacity(planet.Buildings.DeuteriumTank);
			if (forceIfFull && planet.Resources.Deuterium >= deuteriumCapacity && GetNextLevel(planet, Buildables.DeuteriumTank) < maxLevel)
				return true;
			if (deuteriumCapacity < hours * deuteriumProduction && GetNextLevel(planet, Buildables.DeuteriumTank) < maxLevel)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildEnergySource(Planet planet) {
			if (planet.Resources.Energy < 0)
				return true;
			else
				return false;
		}

		public static Buildables GetNextEnergySourceToBuild(Planet planet, int maxSolarPlant, int maxFusionReactor) {
			if (planet.Buildings.SolarPlant < maxSolarPlant)
				return Buildables.SolarPlant;
			if (planet.Buildings.FusionReactor < maxFusionReactor)
				return Buildables.FusionReactor;
			return Buildables.SolarSatellite;
		}

		public static int GetSolarSatelliteOutput(Planet planet, bool isCollector = false, bool hasEngineer = false, bool hasFullStaff = false) {
			float production = (planet.Temperature.Average + 160) / 6;
			float collectorProd = 0;
			float engineerProd = 0;
			float staffProd = 0;
			if (isCollector)
				collectorProd = (float) 0.1 * production;
			if (hasEngineer)
				engineerProd = (float) 0.1 * production;
			if (hasFullStaff)
				staffProd = (float) 0.02 * production;
			return (int) Math.Round(production + collectorProd + engineerProd + staffProd);
		}

		public static int CalcNeededSolarSatellites(Planet planet, long requiredEnergy = 0, bool isCollector = false, bool hasEngineer = false, bool hasFullStaff = false) {
			if (requiredEnergy == 0) {
				if (planet.Resources.Energy > 0)
					return 0;
				return (int) Math.Round((float) (Math.Abs(planet.Resources.Energy) / GetSolarSatelliteOutput(planet, isCollector, hasEngineer, hasFullStaff)), MidpointRounding.ToPositiveInfinity);
			} else
				return (int) Math.Round((float) (requiredEnergy / GetSolarSatelliteOutput(planet, isCollector, hasEngineer, hasFullStaff)), MidpointRounding.ToPositiveInfinity);
		}

		public static Buildables GetNextMineToBuild(Planet planet, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, bool optimizeForStart = true) {
			if (optimizeForStart && (planet.Buildings.MetalMine < 10 || planet.Buildings.CrystalMine < 7 || planet.Buildings.DeuteriumSynthesizer < 5)) {
				if (planet.Buildings.MetalMine <= planet.Buildings.CrystalMine + 2)
					return Buildables.MetalMine;
				else if (planet.Buildings.CrystalMine <= planet.Buildings.DeuteriumSynthesizer + 2)
					return Buildables.CrystalMine;
				else
					return Buildables.DeuteriumSynthesizer;
			}

			var mines = new List<Buildables> { Buildables.MetalMine, Buildables.CrystalMine, Buildables.DeuteriumSynthesizer };
			Dictionary<Buildables, long> dic = new();
			foreach (var mine in mines) {
				if (mine == Buildables.MetalMine && GetNextLevel(planet, mine) > maxMetalMine)
					continue;
				if (mine == Buildables.CrystalMine && GetNextLevel(planet, mine) > maxCrystalMine)
					continue;
				if (mine == Buildables.DeuteriumSynthesizer && GetNextLevel(planet, mine) > maxDeuteriumSynthetizer)
					continue;

				dic.Add(mine, CalcPrice(mine, GetNextLevel(planet, mine)).ConvertedDeuterium);
			}
			if (dic.Count == 0)
				return Buildables.Null;

			dic = dic.OrderBy(m => m.Value)
				.ToDictionary(m => m.Key, m => m.Value);
			return dic.FirstOrDefault().Key;
		}

		public static Buildables GetNextMineToBuild(Planet planet, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, int maxDaysOfInvestmentReturn = 36500) {
			if (optimizeForStart && (planet.Buildings.MetalMine < 10 || planet.Buildings.CrystalMine < 7 || planet.Buildings.DeuteriumSynthesizer < 5)) {
				if (planet.Buildings.MetalMine <= planet.Buildings.CrystalMine + 2 && planet.Buildings.MetalMine < maxMetalMine)
					return Buildables.MetalMine;
				else if (planet.Buildings.CrystalMine <= planet.Buildings.DeuteriumSynthesizer + 2 && planet.Buildings.CrystalMine < maxCrystalMine)
					return Buildables.CrystalMine;
				else if (planet.Buildings.DeuteriumSynthesizer < maxDeuteriumSynthetizer)
					return Buildables.DeuteriumSynthesizer;
			}

			var mines = new List<Buildables> { Buildables.MetalMine, Buildables.CrystalMine, Buildables.DeuteriumSynthesizer };
			Dictionary<Buildables, float> dic = new();
			foreach (var mine in mines) {
				if (mine == Buildables.MetalMine && GetNextLevel(planet, mine) > maxMetalMine)
					continue;
				if (mine == Buildables.CrystalMine && GetNextLevel(planet, mine) > maxCrystalMine)
					continue;
				if (mine == Buildables.DeuteriumSynthesizer && GetNextLevel(planet, mine) > maxDeuteriumSynthetizer)
					continue;

				dic.Add(mine, CalcROI(planet, mine, researches, speedFactor, ratio, playerClass, hasGeologist, hasStaff));
			}
			if (dic.Count == 0)
				return Buildables.Null;

			dic = dic.OrderByDescending(m => m.Value)
				.ToDictionary(m => m.Key, m => m.Value);
			var bestMine = dic.FirstOrDefault().Key;

			if (maxDaysOfInvestmentReturn >= CalcDaysOfInvestmentReturn(planet, bestMine, researches, speedFactor, ratio, playerClass, hasGeologist, hasStaff))
				return bestMine;
			else
				return Buildables.Null;
		}

		public static float CalcROI(Planet planet, Buildables buildable, Researches researches = null, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			float currentProd;
			float nextLevelProd;
			float cost;
			switch (buildable) {
				case Buildables.MetalMine:
					currentProd = CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5;
					nextLevelProd = CalcMetalProduction(planet.Buildings.MetalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5;
					cost = CalcPrice(buildable, GetNextLevel(planet, buildable)).ConvertedDeuterium;
					break;
				case Buildables.CrystalMine:
					currentProd = CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5;
					nextLevelProd = CalcCrystalProduction(planet.Buildings.CrystalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5;
					cost = CalcPrice(buildable, GetNextLevel(planet, buildable)).ConvertedDeuterium;
					break;
				case Buildables.DeuteriumSynthesizer:
					currentProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
					nextLevelProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer + 1, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
					cost = CalcPrice(buildable, GetNextLevel(planet, buildable)).ConvertedDeuterium;
					break;
				default:
					return (float) 0;
			}

			float delta = nextLevelProd - currentProd;
			return delta / cost;
		}

		public static float CalcDaysOfInvestmentReturn(Planet planet, Buildables buildable, Researches researches = null, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
				float oneDayProd = 1;
				switch (buildable) {
					case Buildables.MetalMine:
						oneDayProd = CalcMetalProduction(planet.Buildings.MetalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
						break;
					case Buildables.CrystalMine:
						oneDayProd = CalcCrystalProduction(planet.Buildings.CrystalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
						break;
					case Buildables.DeuteriumSynthesizer:
						oneDayProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer + 1, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) * 24;
						break;
					default:

						break;
				}
				long cost = CalcPrice(buildable, GetNextLevel(planet, buildable)).ConvertedDeuterium;
				return cost / oneDayProd;
			} else
				return float.MaxValue;
		}

		public static float CalcNextDaysOfInvestmentReturn(Planet planet, Researches researches = null, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			var metalCost = CalcPrice(Buildables.MetalMine, GetNextLevel(planet, Buildables.MetalMine)).ConvertedDeuterium;
			var oneDayMetalProd = CalcMetalProduction(planet.Buildings.MetalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
			float metalDOIR = metalCost / oneDayMetalProd;
			var crystalCost = CalcPrice(Buildables.CrystalMine, GetNextLevel(planet, Buildables.CrystalMine)).ConvertedDeuterium;
			var oneDayCrystalProd = CalcCrystalProduction(planet.Buildings.CrystalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
			float crystalDOIR = crystalCost / oneDayCrystalProd;
			var deuteriumCost = CalcPrice(Buildables.DeuteriumSynthesizer, GetNextLevel(planet, Buildables.DeuteriumSynthesizer)).ConvertedDeuterium;
			var oneDayDeuteriumProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer + 1, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) * 24;
			float deuteriumDOIR = deuteriumCost / oneDayDeuteriumProd;

			return Math.Min(float.IsNaN(deuteriumDOIR) ? float.MaxValue : deuteriumDOIR, Math.Min(float.IsNaN(crystalDOIR) ? float.MaxValue : crystalDOIR, float.IsNaN(metalDOIR) ? float.MaxValue : metalDOIR));
		}

		public static Buildings GetMaxBuildings(int maxMetalMine, int maxCrystalMine, int maxDeuteriumSynthetizer, int maxSolarPlant, int maxFusionReactor, int maxMetalStorage, int maxCrystalStorage, int maxDeuteriumTank) {
			return new() {
				MetalMine = maxMetalMine,
				CrystalMine = maxCrystalMine,
				DeuteriumSynthesizer = maxDeuteriumSynthetizer,
				SolarPlant = maxSolarPlant,
				FusionReactor = maxFusionReactor,
				MetalStorage = maxMetalStorage,
				CrystalStorage = maxCrystalStorage,
				DeuteriumTank = maxDeuteriumTank
			};
		}
		public static Facilities GetMaxFacilities(int maxRoboticsFactory, int maxShipyard, int maxResearchLab, int maxMissileSilo, int maxNaniteFactory, int maxTerraformer, int maxSpaceDock) {
			return new() {
				RoboticsFactory = maxRoboticsFactory,
				Shipyard = maxShipyard,
				ResearchLab = maxResearchLab,
				MissileSilo = maxMissileSilo,
				NaniteFactory = maxNaniteFactory,
				Terraformer = maxTerraformer,
				SpaceDock = maxSpaceDock
			};
		}

		public static Facilities GetMaxLunarFacilities(int maxLunarBase, int maxLunarShipyard, int maxLunarRoboticsFactory, int maxSensorPhalanx, int maxJumpGate) {
			return new() {
				LunarBase = maxLunarBase,
				Shipyard = maxLunarShipyard,
				RoboticsFactory = maxLunarRoboticsFactory,
				SensorPhalanx = maxSensorPhalanx,
				JumpGate = maxJumpGate
			};
		}

		public static Buildables GetNextBuildingToBuild(Planet planet, Researches researches, Buildings maxBuildings, Facilities maxFacilities, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1) {
			Buildables buildableToBuild = Buildables.Null;
			if (ShouldBuildTerraformer(planet, researches, maxFacilities.Terraformer))
				buildableToBuild = Buildables.Terraformer;
			if (buildableToBuild == Buildables.Null && ShouldBuildEnergySource(planet))
				buildableToBuild = GetNextEnergySourceToBuild(planet, maxBuildings.SolarPlant, maxBuildings.FusionReactor);
			if (buildableToBuild == Buildables.Null)
				buildableToBuild = GetNextDepositToBuild(planet, researches, maxBuildings, playerClass, staff, serverData, settings, ratio);
			if (buildableToBuild == Buildables.Null)
				buildableToBuild = GetNextFacilityToBuild(planet, researches, maxBuildings, maxFacilities, playerClass, staff, serverData, settings, ratio);
			if (buildableToBuild == Buildables.Null)
				buildableToBuild = GetNextMineToBuild(planet, researches, maxBuildings, maxFacilities, playerClass, staff, serverData, settings, ratio);

			return buildableToBuild;
		}

		public static Buildables GetNextDepositToBuild(Planet planet, Researches researches, Buildings maxBuildings, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1) {
			Buildables depositToBuild = Buildables.Null;
			if (
				settings.OptimizeForStart &&
				planet.Buildings.MetalMine < 13 &&
				planet.Buildings.CrystalMine < 12 &&
				planet.Buildings.DeuteriumSynthesizer < 10 &&
				planet.Buildings.SolarPlant < 13 &&
				planet.Buildings.FusionReactor < 5 &&
				planet.Facilities.RoboticsFactory < 5 &&
				planet.Facilities.Shipyard < 5 &&
				planet.Facilities.ResearchLab < 5
			)
				return depositToBuild;
			if (depositToBuild == Buildables.Null && ShouldBuildDeuteriumTank(planet, maxBuildings.DeuteriumTank, serverData.Speed, settings.DepositHours, ratio, researches, playerClass, staff.Geologist, staff.IsFull, settings.BuildDepositIfFull))
				depositToBuild = Buildables.DeuteriumTank;
			if (depositToBuild == Buildables.Null && ShouldBuildCrystalStorage(planet, maxBuildings.CrystalStorage, serverData.Speed, settings.DepositHours, ratio, researches, playerClass, staff.Geologist, staff.IsFull, settings.BuildDepositIfFull))
				depositToBuild = Buildables.CrystalStorage;
			if (depositToBuild == Buildables.Null && ShouldBuildMetalStorage(planet, maxBuildings.MetalStorage, serverData.Speed, settings.DepositHours, ratio, researches, playerClass, staff.Geologist, staff.IsFull, settings.BuildDepositIfFull))
				depositToBuild = Buildables.MetalStorage;

			return depositToBuild;
		}
		public static Buildables GetNextFacilityToBuild(Planet planet, Researches researches, Buildings maxBuildings, Facilities maxFacilities, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1) {
			Buildables facilityToBuild = Buildables.Null;
			if (settings.PrioritizeRobotsAndNanites)
				if (planet.Facilities.RoboticsFactory < 10 && planet.Facilities.RoboticsFactory < maxFacilities.RoboticsFactory)
					facilityToBuild = Buildables.RoboticsFactory;
				else if (planet.Facilities.RoboticsFactory >= 10 && researches.ComputerTechnology >= 10 && planet.Facilities.NaniteFactory < maxFacilities.NaniteFactory && !planet.HasProduction())
					facilityToBuild = Buildables.NaniteFactory;
			if (facilityToBuild == Buildables.Null && ShouldBuildSpaceDock(planet, maxFacilities.SpaceDock, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull))
				facilityToBuild = Buildables.SpaceDock;
			if (facilityToBuild == Buildables.Null && ShouldBuildNanites(planet, maxFacilities.NaniteFactory, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull) && !planet.HasProduction())
				facilityToBuild = Buildables.NaniteFactory;
			if (facilityToBuild == Buildables.Null && ShouldBuildRoboticFactory(planet, maxFacilities.RoboticsFactory, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull))
				facilityToBuild = Buildables.RoboticsFactory;
			if (facilityToBuild == Buildables.Null && ShouldBuildShipyard(planet, maxFacilities.Shipyard, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull) && !planet.HasProduction())
				facilityToBuild = Buildables.Shipyard;
			if (facilityToBuild == Buildables.Null && ShouldBuildResearchLab(planet, maxFacilities.ResearchLab, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull) && planet.Constructions.ResearchID == 0)
				facilityToBuild = Buildables.ResearchLab;
			if (facilityToBuild == Buildables.Null && ShouldBuildMissileSilo(planet, maxFacilities.MissileSilo, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull))
				facilityToBuild = Buildables.MissileSilo;

			return facilityToBuild;
		}

		public static Buildables GetNextMineToBuild(Planet planet, Researches researches, Buildings maxBuildings, Facilities maxFacilities, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1) {
			return GetNextMineToBuild(planet, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, ratio, playerClass, staff.Geologist, staff.IsFull, settings.OptimizeForStart, settings.MaxDaysOfInvestmentReturn);
		}

		public static Buildables GetNextLunarFacilityToBuild(Moon moon, Researches researches, Facilities maxLunarFacilities) {
			return GetNextLunarFacilityToBuild(moon, researches, maxLunarFacilities.LunarBase, maxLunarFacilities.RoboticsFactory, maxLunarFacilities.SensorPhalanx, maxLunarFacilities.JumpGate, maxLunarFacilities.Shipyard);
		}

		public static Buildables GetNextLunarFacilityToBuild(Moon moon, Researches researches, int maxLunarBase = 8, int maxRoboticsFactory = 8, int maxSensorPhalanx = 6, int maxJumpGate = 1, int maxShipyard = 0) {
			Buildables lunarFacilityToBuild = Buildables.Null;
			if (ShouldBuildLunarBase(moon, maxLunarBase))
				lunarFacilityToBuild = Buildables.LunarBase;
			if (lunarFacilityToBuild == Buildables.Null && ShouldBuildRoboticFactory(moon, maxRoboticsFactory))
				lunarFacilityToBuild = Buildables.RoboticsFactory;
			if (lunarFacilityToBuild == Buildables.Null && ShouldBuildJumpGate(moon, maxJumpGate, researches))
				lunarFacilityToBuild = Buildables.JumpGate;
			if (lunarFacilityToBuild == Buildables.Null && ShouldBuildSensorPhalanx(moon, maxSensorPhalanx))
				lunarFacilityToBuild = Buildables.SensorPhalanx;
			if (lunarFacilityToBuild == Buildables.Null && ShouldBuildShipyard(moon, maxShipyard))
				lunarFacilityToBuild = Buildables.Shipyard;

			return lunarFacilityToBuild;
		}

		public static bool ShouldBuildRoboticFactory(Celestial celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, int maxDaysOfInvestmentReturn = 36500) {
			if (celestial is Planet) {
				var nextMine = GetNextMineToBuild(celestial as Planet, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
				var nextMineLevel = GetNextLevel(celestial, nextMine);
				var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

				var nextRobotsLevel = GetNextLevel(celestial, Buildables.RoboticsFactory);
				var nextRobotsPrice = CalcPrice(Buildables.RoboticsFactory, nextRobotsLevel);

				if (nextRobotsLevel <= maxLevel && nextMinePrice.ConvertedDeuterium > nextRobotsPrice.ConvertedDeuterium)
					return true;
				else
					return false;
			} else {
				var nextRobotsLevel = GetNextLevel(celestial, Buildables.RoboticsFactory);

				if (nextRobotsLevel <= maxLevel && celestial.Fields.Free > 1)
					return true;
				else
					return false;
			}
		}

		public static bool ShouldBuildShipyard(Celestial celestial, int maxLevel = 12, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, int maxDaysOfInvestmentReturn = 36500) {
			if (celestial is Planet) {
				var nextMine = GetNextMineToBuild(celestial as Planet, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
				var nextMineLevel = GetNextLevel(celestial, nextMine);
				var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

				var nextShipyardLevel = GetNextLevel(celestial as Planet, Buildables.Shipyard);
				var nextShipyardPrice = CalcPrice(Buildables.Shipyard, nextShipyardLevel);

				if (nextShipyardLevel <= maxLevel && nextMinePrice.ConvertedDeuterium > nextShipyardPrice.ConvertedDeuterium && celestial.Facilities.RoboticsFactory >= 2)
					return true;
				else
					return false;
			} else {
				var nextShipyardLevel = GetNextLevel(celestial, Buildables.Shipyard);

				if (nextShipyardLevel <= maxLevel && celestial.Fields.Free > 1)
					return true;
				else
					return false;
			}
		}

		public static bool ShouldBuildResearchLab(Planet celestial, int maxLevel = 12, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, int maxDaysOfInvestmentReturn = 36500) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

			var nextLabLevel = GetNextLevel(celestial, Buildables.ResearchLab);
			var nextLabPrice = CalcPrice(Buildables.ResearchLab, nextLabLevel);

			if (nextLabLevel <= maxLevel && nextMinePrice.ConvertedDeuterium > nextLabPrice.ConvertedDeuterium)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildMissileSilo(Planet celestial, int maxLevel = 6, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, int maxDaysOfInvestmentReturn = 36500) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

			var nextSiloLevel = GetNextLevel(celestial, Buildables.MissileSilo);
			var nextSiloPrice = CalcPrice(Buildables.MissileSilo, nextSiloLevel);

			if (nextSiloLevel <= maxLevel && nextMinePrice.ConvertedDeuterium > nextSiloPrice.ConvertedDeuterium && celestial.Facilities.Shipyard >= 1)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildNanites(Planet celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, int maxDaysOfInvestmentReturn = 36500) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

			var nextNanitesLevel = GetNextLevel(celestial, Buildables.NaniteFactory);
			var nextNanitesPrice = CalcPrice(Buildables.NaniteFactory, nextNanitesLevel);

			if (nextNanitesLevel <= maxLevel && nextMinePrice.ConvertedDeuterium > nextNanitesPrice.ConvertedDeuterium && celestial.Facilities.RoboticsFactory >= 10 && researches.ComputerTechnology >= 10)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildTerraformer(Planet celestial, Researches researches, int maxLevel = 10) {
			if (researches.EnergyTechnology < 12)
				return false;
			var nextLevel = GetNextLevel(celestial, Buildables.Terraformer);
			if (celestial.Fields.Free == 1 && nextLevel <= maxLevel)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildSpaceDock(Planet celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, int maxDaysOfInvestmentReturn = 36500) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

			var nextSpaceDockLevel = GetNextLevel(celestial, Buildables.SpaceDock);
			var nextSpaceDockPrice = CalcPrice(Buildables.SpaceDock, nextSpaceDockLevel);
			if (nextSpaceDockLevel <= maxLevel && nextMinePrice.ConvertedDeuterium > nextSpaceDockPrice.ConvertedDeuterium && celestial.ResourcesProduction.Energy.CurrentProduction >= nextSpaceDockPrice.Energy && celestial.Facilities.Shipyard >= 2)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildLunarBase(Moon moon, int maxLevel = 8) {
			var nextLunarBaseLevel = GetNextLevel(moon, Buildables.LunarBase);

			if (nextLunarBaseLevel <= maxLevel && moon.Fields.Free == 1)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildSensorPhalanx(Moon moon, int maxLevel = 7) {
			var nextSensorPhalanxLevel = GetNextLevel(moon, Buildables.SensorPhalanx);

			if (nextSensorPhalanxLevel <= maxLevel && moon.Facilities.LunarBase >= 1 && moon.Fields.Free > 1)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildJumpGate(Moon moon, int maxLevel = 1, Researches researches = null) {
			var nextJumpGateLevel = GetNextLevel(moon, Buildables.JumpGate);

			if (nextJumpGateLevel <= maxLevel && moon.Facilities.LunarBase >= 1 && researches.HyperspaceTechnology >= 7 && moon.Fields.Free > 1)
				return true;
			else
				return false;
		}

		public static Buildables GetNextResearchToBuild(Planet celestial, Researches researches, bool prioritizeRobotsAndNanitesOnNewPlanets = false, Slots slots = null, int maxEnergyTechnology = 20, int maxLaserTechnology = 12, int maxIonTechnology = 5, int maxHyperspaceTechnology = 20, int maxPlasmaTechnology = 20, int maxCombustionDrive = 19, int maxImpulseDrive = 17, int maxHyperspaceDrive = 15, int maxEspionageTechnology = 8, int maxComputerTechnology = 20, int maxAstrophysics = 23, int maxIntergalacticResearchNetwork = 12, int maxWeaponsTechnology = 25, int maxShieldingTechnology = 25, int maxArmourTechnology = 25, bool optimizeForStart = true, bool ensureExpoSlots = true) {
			if (optimizeForStart) {
				if (researches.EnergyTechnology == 0 && celestial.Facilities.ResearchLab > 0 && researches.EnergyTechnology < maxEnergyTechnology)
					return Buildables.EnergyTechnology;
				if (researches.CombustionDrive < 2 && celestial.Facilities.ResearchLab > 0 && researches.EnergyTechnology >= 1 && researches.CombustionDrive < maxCombustionDrive)
					return Buildables.CombustionDrive;
				if (researches.EspionageTechnology < 4 && celestial.Facilities.ResearchLab >= 3 && researches.EspionageTechnology < maxEspionageTechnology)
					return Buildables.EspionageTechnology;
				if (researches.ImpulseDrive < 3 && celestial.Facilities.ResearchLab >= 2 && researches.EnergyTechnology >= 1 && researches.ImpulseDrive < maxImpulseDrive)
					return Buildables.ImpulseDrive;
				if (researches.ComputerTechnology < 1 && celestial.Facilities.ResearchLab >= 1 && researches.ComputerTechnology < maxComputerTechnology)
					return Buildables.ComputerTechnology;
				if (researches.Astrophysics == 0 && celestial.Facilities.ResearchLab >= 3 && researches.EspionageTechnology >= 4 && researches.ImpulseDrive >= 3 && researches.Astrophysics < maxAstrophysics)
					return Buildables.Astrophysics;
				if (researches.EnergyTechnology >= 1 && researches.EnergyTechnology < 3 && celestial.Facilities.ResearchLab > 0 && researches.EnergyTechnology < maxEnergyTechnology)
					return Buildables.EnergyTechnology;
				if (researches.ShieldingTechnology < 2 && celestial.Facilities.ResearchLab > 5 && researches.EnergyTechnology >= 3 && researches.ShieldingTechnology < maxShieldingTechnology)
					return Buildables.ShieldingTechnology;
				if (researches.CombustionDrive >= 2 && researches.CombustionDrive < 6 && celestial.Facilities.ResearchLab > 0 && researches.EnergyTechnology >= 1 && researches.CombustionDrive < maxComputerTechnology)
					return Buildables.CombustionDrive;
				if (prioritizeRobotsAndNanitesOnNewPlanets && researches.ComputerTechnology < 10 && celestial.Facilities.ResearchLab >= 1 && researches.ComputerTechnology < maxComputerTechnology)
					return Buildables.ComputerTechnology;
			}

			if (ensureExpoSlots && slots != null && slots.ExpTotal + 1 > slots.Total && researches.ComputerTechnology < maxComputerTechnology)
				return Buildables.ComputerTechnology;

			List<Buildables> researchesList = new()
			{
				Buildables.EnergyTechnology,
				Buildables.LaserTechnology,
				Buildables.IonTechnology,
				Buildables.HyperspaceTechnology,
				Buildables.PlasmaTechnology,
				Buildables.CombustionDrive,
				Buildables.ImpulseDrive,
				Buildables.HyperspaceDrive,
				Buildables.EspionageTechnology,
				Buildables.ComputerTechnology,
				Buildables.Astrophysics,
				Buildables.IntergalacticResearchNetwork,
				Buildables.WeaponsTechnology,
				Buildables.ShieldingTechnology,
				Buildables.ArmourTechnology,
				Buildables.GravitonTechnology
			};

			Dictionary<Buildables, long> dic = new();
			foreach (Buildables research in researchesList) {
				switch (research) {
					case Buildables.EnergyTechnology:
						if (celestial.Facilities.ResearchLab < 1)
							continue;
						if (GetNextLevel(researches, research) > maxEnergyTechnology)
							continue;
						break;
					case Buildables.LaserTechnology:
						if (celestial.Facilities.ResearchLab < 1)
							continue;
						if (researches.EnergyTechnology < 2)
							continue;
						if (GetNextLevel(researches, research) > maxLaserTechnology)
							continue;
						break;
					case Buildables.IonTechnology:
						if (celestial.Facilities.ResearchLab < 4)
							continue;
						if (researches.EnergyTechnology < 4 || researches.LaserTechnology < 5)
							continue;
						if (GetNextLevel(researches, research) > maxIonTechnology)
							continue;
						break;
					case Buildables.HyperspaceTechnology:
						if (celestial.Facilities.ResearchLab < 7)
							continue;
						if (researches.EnergyTechnology < 5 || researches.ShieldingTechnology < 5)
							continue;
						if (GetNextLevel(researches, research) > maxHyperspaceTechnology)
							continue;
						break;
					case Buildables.PlasmaTechnology:
						if (celestial.Facilities.ResearchLab < 4)
							continue;
						if (researches.EnergyTechnology < 8 || researches.LaserTechnology < 10 || researches.IonTechnology < 5)
							continue;
						if (GetNextLevel(researches, research) > maxPlasmaTechnology)
							continue;
						break;
					case Buildables.CombustionDrive:
						if (celestial.Facilities.ResearchLab < 1)
							continue;
						if (researches.EnergyTechnology < 1)
							continue;
						if (GetNextLevel(researches, research) > maxCombustionDrive)
							continue;
						break;
					case Buildables.ImpulseDrive:
						if (celestial.Facilities.ResearchLab < 2)
							continue;
						if (researches.EnergyTechnology < 1)
							continue;
						if (GetNextLevel(researches, research) > maxImpulseDrive)
							continue;
						break;
					case Buildables.HyperspaceDrive:
						if (celestial.Facilities.ResearchLab < 7)
							continue;
						if (researches.HyperspaceTechnology < 3)
							continue;
						if (GetNextLevel(researches, research) > maxHyperspaceDrive)
							continue;
						break;
					case Buildables.EspionageTechnology:
						if (celestial.Facilities.ResearchLab < 3)
							continue;
						if (GetNextLevel(researches, research) > maxEspionageTechnology)
							continue;
						break;
					case Buildables.ComputerTechnology:
						if (celestial.Facilities.ResearchLab < 1)
							continue;
						if (GetNextLevel(researches, research) > maxComputerTechnology)
							continue;
						break;
					case Buildables.Astrophysics:
						if (celestial.Facilities.ResearchLab < 3)
							continue;
						if (researches.EspionageTechnology < 4 || researches.ImpulseDrive < 3)
							continue;
						if (GetNextLevel(researches, research) > maxAstrophysics)
							continue;
						break;
					case Buildables.IntergalacticResearchNetwork:
						if (celestial.Facilities.ResearchLab < 10)
							continue;
						if (researches.ComputerTechnology < 8 || researches.HyperspaceTechnology < 8)
							continue;
						if (GetNextLevel(researches, research) > maxIntergalacticResearchNetwork)
							continue;
						break;
					case Buildables.WeaponsTechnology:
						if (celestial.Facilities.ResearchLab < 4)
							continue;
						if (GetNextLevel(researches, research) > maxWeaponsTechnology)
							continue;
						break;
					case Buildables.ShieldingTechnology:
						if (celestial.Facilities.ResearchLab < 6)
							continue;
						if (researches.EnergyTechnology < 3)
							continue;
						if (GetNextLevel(researches, research) > maxShieldingTechnology)
							continue;
						break;
					case Buildables.ArmourTechnology:
						if (celestial.Facilities.ResearchLab < 2)
							continue;
						if (GetNextLevel(researches, research) > maxArmourTechnology)
							continue;
						break;
					case Buildables.GravitonTechnology:
						if (celestial.Facilities.ResearchLab < 12)
							continue;
						if (celestial.Resources.Energy < CalcPrice(Buildables.GravitonTechnology, GetNextLevel(researches, Buildables.GravitonTechnology)).Energy)
							continue;
						break;

				}

				dic.Add(research, CalcPrice(research, GetNextLevel(researches, research)).ConvertedDeuterium);
			}
			if (dic.Count == 0)
				return Buildables.Null;

			dic = dic.OrderBy(m => m.Value)
				.ToDictionary(m => m.Key, m => m.Value);
			return dic.FirstOrDefault().Key;
		}

		public static bool IsThereTransportTowardsCelestial(Celestial celestial, List<Fleet> fleets) {
			var transports = fleets
				.Where(f => f.Mission == Missions.Transport)
				.Where(f => f.Resources.TotalResources > 0)
				.Where(f => f.ReturnFlight == false)
				.Where(f => f.Destination.Galaxy == celestial.Coordinate.Galaxy)
				.Where(f => f.Destination.System == celestial.Coordinate.System)
				.Where(f => f.Destination.Position == celestial.Coordinate.Position)
				.Where(f => f.Destination.Type == celestial.Coordinate.Type)
				.Count();
			if (transports > 0)
				return true;
			else
				return false;
		}

		public static bool AreThereIncomingResources(Celestial celestial, List<Fleet> fleets) {
			return fleets
				.Where(f => f.Mission == Missions.Transport)
				.Where(f => f.Resources.TotalResources > 0)
				.Where(f => f.ReturnFlight == false)
				.Where(f => f.Destination.IsSame(celestial.Coordinate))
				.Any();
		}

		public static List<Fleet> GetIncomingFleets(Celestial celestial, List<Fleet> fleets) {
			List<Fleet> incomingFleets = new();
			incomingFleets.AddRange(fleets
				.Where(f => f.Destination.IsSame(celestial.Coordinate))
				.Where(f => (f.Mission == Missions.Transport || f.Mission == Missions.Deploy) && !f.ReturnFlight)
				.ToList());
			incomingFleets.AddRange(fleets
				.Where(f => f.Origin.IsSame(celestial.Coordinate))
				.Where(f => f.ReturnFlight == true)
				.ToList());
			return incomingFleets
				.OrderBy(f => (f.Mission == Missions.Transport || f.Mission == Missions.Deploy) && !f.ReturnFlight ? f.ArriveIn : f.BackIn)
				.ToList();
		}

		public static List<Fleet> GetIncomingFleetsWithResources(Celestial celestial, List<Fleet> fleets) {
			List<Fleet> incomingFleets = GetIncomingFleets(celestial, fleets);
			incomingFleets = incomingFleets
				.Where(f => f.Resources.TotalResources > 0)
				.ToList();
			return incomingFleets;
		}

		public static Fleet GetFirstReturningExpedition(Coordinate coord, List<Fleet> fleets) {
			var celestialExpos = fleets
				.Where(f => f.Origin.IsSame(coord))
				.Where(f => f.Mission == Missions.Expedition);
			if (celestialExpos.Any()) {
				return celestialExpos
					.OrderBy(fleet => fleet.BackIn).First();
			} else
				return null;
		}

		public static List<Fleet> GetMissionsInProgress(Missions mission, List<Fleet> fleets) {
			var inProgress = fleets.Where(f => f.Mission == mission);
			if (inProgress.Any()) {
				return inProgress.ToList();
			} else
				return new List<Fleet>();
		}

		public static List<Fleet> GetMissionsInProgress(Coordinate origin, Missions mission, List<Fleet> fleets) {
			var inProgress = fleets
				.Where(f => f.Origin.IsSame(origin))
				.Where(f => f.Mission == mission);
			if (inProgress.Any()) {
				return inProgress.ToList();
			} else
				return new List<Fleet>();
		}

		public static Fleet GetFirstReturningEspionage(List<Fleet> fleets) {
			var celestialEspionages = GetMissionsInProgress(Missions.Spy, fleets);
			if (celestialEspionages.Count > 0) {
				return celestialEspionages
					.OrderBy(fleet => fleet.BackIn).First();
			} else
				return null;
		}

		public static Fleet GetFirstReturningEspionage(Coordinate origin, List<Fleet> fleets) {
			var celestialEspionages = GetMissionsInProgress(origin, Missions.Spy, fleets);
			if (celestialEspionages != null) {
				return celestialEspionages
					.OrderBy(fleet => fleet.BackIn).First();
			} else
				return null;
		}

		public static List<Celestial> ParseCelestialsList(dynamic source, List<Celestial> currentCelestials) {
			List<Celestial> output = new();
			try {
				foreach (var celestialToParse in source) {
					Coordinate parsedCoords = new(
						(int) celestialToParse.Galaxy,
						(int) celestialToParse.System,
						(int) celestialToParse.Position,
						Enum.Parse<Celestials>(celestialToParse.Type.ToString())
					);

					Celestial parsedCelestial = currentCelestials
						.SingleOrDefault(cel => cel.HasCoords(parsedCoords)) ?? new Celestial() { ID = 0 };

					if (parsedCelestial.ID != 0)
						output.Add(parsedCelestial);
				}
			} catch {
				throw;
			}

			return output;
		}

		public static bool IsSettingSet(dynamic setting) {
			try {
				var x = setting;
				return true;
			} catch {
				return false;
			}
		}

		public static int CalcMaxPlanets(int astrophysics) {
			return (int) Math.Round((float) ((astrophysics + 3) / 2), 0, MidpointRounding.ToZero);
		}

		public static int CalcMaxPlanets(Researches researches) {
			return researches == null ? 1 : CalcMaxPlanets(researches.Astrophysics);
		}

		public static int CalcMaxCrawlers(Planet planet, CharacterClass userClass) {
			return 8 * (planet.Buildings.MetalMine + planet.Buildings.CrystalMine + planet.Buildings.DeuteriumSynthesizer);
		}
	}
}
