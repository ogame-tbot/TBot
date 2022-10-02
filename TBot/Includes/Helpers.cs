using Tbot.Model;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;

namespace Tbot.Includes {

	static class Helpers {
		public static void WriteLog(LogType type, LogSender sender, string message) {
			LogToConsole(type, sender, message);
			LogToFile(type, sender, message);
			LogToCSV(type, sender, message);
		}

		public static void LogToConsole(LogType type, LogSender sender, string message) {
			ConsoleColor consoleColor = sender switch {
				LogSender.Brain => ConsoleColor.Blue,
				LogSender.Defender => ConsoleColor.DarkGreen,
				LogSender.Expeditions => ConsoleColor.Cyan,
				LogSender.FleetScheduler => ConsoleColor.DarkMagenta,
				LogSender.Harvest => ConsoleColor.Green,
				LogSender.Colonize => ConsoleColor.DarkRed,
				LogSender.AutoFarm => ConsoleColor.DarkCyan,
				LogSender.SleepMode => ConsoleColor.DarkBlue,
				LogSender.Tbot => ConsoleColor.DarkYellow,
				LogSender.OGameD => ConsoleColor.DarkCyan,
				_ => ConsoleColor.Gray
			};
			Console.ForegroundColor = type == LogType.Info
				? consoleColor
				: type switch {
					LogType.Error => ConsoleColor.Red,
					LogType.Warning => ConsoleColor.Yellow,
					LogType.Debug => ConsoleColor.White,
					_ => ConsoleColor.Gray
				};

			Console.WriteLine($"[{DateTime.Now.ToLongTimeString()}|{type.ToString()}|{sender.ToString()}] {message}");
			Console.ForegroundColor = ConsoleColor.Gray;
		}

		public static string logPath = Path.Combine(Directory.GetCurrentDirectory(), "log");
		public static void LogToFile(LogType type, LogSender sender, string message) {
			string path = logPath;
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
			string path = logPath;
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

		public static long ParseDurationFromString(string timeString) {
			long duration = 0;
			string regExp = "^(\\d{1,2}[h|H])?(\\d{1,2}[m|M])?(\\d{1,2}[s|S])?";

			Regex re = new Regex(regExp);
			Match m = re.Match(timeString);

			if (m.Groups.Count == 4) {
				int hours = m.Groups[1].Success ? Int32.Parse(m.Groups[1].Value.Remove(m.Groups[1].Value.Length - 1)) : 0;
				int mins = m.Groups[2].Success ? Int32.Parse(m.Groups[2].Value.Remove(m.Groups[2].Value.Length - 1)) : 0;
				int secs = m.Groups[3].Success ? Int32.Parse(m.Groups[3].Value.Remove(m.Groups[3].Value.Length - 1)) : 0;

				duration = (hours * 60 * 60) + (mins * 60) + secs;
			} else {
				throw new Exception($"Invalid string {timeString}");
			}

			return duration;
		}

		public static string TimeSpanToString(TimeSpan delta) {

			return string.Format("{0} days {1:00}:{2:00}:{3:00}", delta.Days, delta.Hours, delta.Minutes, delta.Seconds);
		}

		public static int CalcRandomInterval(IntervalType type) {
			var rand = new Random();
			return type switch {
				IntervalType.LessThanASecond => rand.Next(500, 1000),
				IntervalType.LessThanFiveSeconds => rand.Next(1000, 5000),
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

		public static bool ShouldSleep(DateTime time, DateTime goToSleep, DateTime wakeUp) {
			if (time >= goToSleep) {
				if (time >= wakeUp) {
					if (goToSleep >= wakeUp) {
						return true;
					} else {
						return false;
					}
				} else {
					return true;
				}
			} else {
				if (time >= wakeUp) {
					return false;
				} else {
					if (goToSleep >= wakeUp) {
						return true;
					} else {
						return false;
					}
				}
			}
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
						bonus += 20;
					break;
				case Buildables.EspionageProbe:
					baseCargo = probeCargo;
					break;
				case Buildables.Bomber:
					baseCargo = 500;
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
					baseCargo = 10000;
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

		public static int CalcShipFuelCapacity(Buildables buildable, int hyperspaceTech, CharacterClass playerClass, int probeCargo = 0) {
			return CalcShipCapacity(buildable, 0, CharacterClass.General, probeCargo);
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

		public static long CalcFleetFuelCapacity(Ships fleet, int hyperspaceTech, CharacterClass playerClass, int probeCargo = 0) {
			long total = 0;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					int oneCargo = CalcShipFuelCapacity(buildable, hyperspaceTech, playerClass, probeCargo);
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
			double fuelConsumption = (double) deuteriumSaveFactor * (double) baseConsumption;
			if (playerClass == CharacterClass.General)
				fuelConsumption /= 2;
			fuelConsumption = Math.Round(fuelConsumption);
			if (fuelConsumption < 1) {
				return 1;
			} else {
				return (int) fuelConsumption;
			}
		}

		public static long CalcFlightTime(Coordinate origin, Coordinate destination, Ships ships, Missions mission, decimal speed, Researches researches, ServerData serverData, CharacterClass playerClass) {
			var fleetSpeed = mission switch {
				Missions.Attack or Missions.FederalAttack or Missions.Destroy or Missions.Spy or Missions.Harvest => serverData.SpeedFleetWar,
				Missions.FederalDefense => serverData.SpeedFleetHolding,
				_ => serverData.SpeedFleetPeaceful,
			};
			return CalcFlightTime(origin, destination, ships, speed, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem, fleetSpeed, playerClass);
		}

		public static long CalcFlightTime(Coordinate origin, Coordinate destination, Ships ships, decimal speed, int combustionDrive, int impulseDrive, int hyperspaceDrive, int numberOfGalaxies, int numberOfSystems, bool donutGalaxies, bool donutSystems, int fleetSpeed, CharacterClass playerClass) {
			int slowestShipSpeed = CalcSlowestSpeed(ships, combustionDrive, impulseDrive, hyperspaceDrive, playerClass);
			int distance = CalcDistance(origin, destination, numberOfGalaxies, numberOfSystems, donutGalaxies, donutSystems);
			double s = (double) speed;
			double v = (double) slowestShipSpeed;
			double a = (double) fleetSpeed;
			double d = (double) distance;
			long output = (long) Math.Round((((double) 35000 / s) * Math.Sqrt(d * (double) 10 / v) + (double) 10) / a);
			return output;
		}

		public static long CalcFuelConsumption(Coordinate origin, Coordinate destination, Ships ships, Missions mission, long flightTime, Researches researches, ServerData serverData, CharacterClass playerClass) {
			var fleetSpeed = mission switch {
				Missions.Attack or Missions.FederalAttack or Missions.Destroy or Missions.Harvest or Missions.Spy => serverData.SpeedFleetWar,
				Missions.FederalDefense => serverData.SpeedFleetHolding,
				_ => serverData.SpeedFleetPeaceful,
			};
			return CalcFuelConsumption(origin, destination, ships, flightTime, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem, fleetSpeed, serverData.GlobalDeuteriumSaveFactor, playerClass);
		}

		public static long CalcFuelConsumption(Coordinate origin, Coordinate destination, Ships ships, long flightTime, int combustionDrive, int impulseDrive, int hyperspaceDrive, int numberOfGalaxies, int numberOfSystems, bool donutGalaxies, bool donutSystems, int fleetSpeed, float deuteriumSaveFactor, CharacterClass playerClass) {
			int distance = CalcDistance(origin, destination, numberOfGalaxies, numberOfSystems, donutGalaxies, donutSystems);
			double tempFuel = (double) 0;
			foreach (PropertyInfo prop in ships.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(ships, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					double tempSpeed = 35000 / (((double) flightTime * (double) fleetSpeed) - (double) 10) * (double) Math.Sqrt((double) distance * (double) 10 / (double) CalcShipSpeed(buildable, combustionDrive, impulseDrive, hyperspaceDrive, playerClass));
					int shipConsumption = CalcShipConsumption(buildable, impulseDrive, hyperspaceDrive, deuteriumSaveFactor, playerClass);
					double thisFuel = ((double) shipConsumption * (double) qty * (double) distance) / (double) 35000 * Math.Pow(((double) tempSpeed / (double) 10) + (double) 1, 2);
					tempFuel += thisFuel;
				}
			}
			long output = (long) (1 + Math.Round(tempFuel));
			return output;
		}

		public static FleetPrediction CalcFleetPrediction(Coordinate origin, Coordinate destination, Ships ships, Missions mission, decimal speed, Researches researches, ServerData serverData, CharacterClass playerClass) {
			long time = CalcFlightTime(origin, destination, ships, mission, speed, researches, serverData, playerClass);
			long fuel = CalcFuelConsumption(origin, destination, ships, mission, time, researches, serverData, playerClass);
			return new() {
				Fuel = fuel,
				Time = time
			};
		}

		public static FleetPrediction CalcFleetPrediction(Celestial origin, Coordinate destination, Ships ships, Missions mission, decimal speed, Researches researches, ServerData serverData, CharacterClass playerClass) {
			return CalcFleetPrediction(origin.Coordinate, destination, ships, mission, speed, researches, serverData, playerClass);
		}

		public static List<decimal> GetValidSpeedsForClass(CharacterClass playerClass) {
			var speeds = new List<decimal>();
			/* TODO: fix general speeds
			if (playerClass == CharacterClass.General*) {
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
			*/
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
			return speeds;
		}

		public static decimal CalcOptimalFarmSpeed(Coordinate origin, Coordinate destination, Ships ships, Resources loot, decimal ratio, long maxFlightTime, Researches researches, ServerData serverData, CharacterClass playerClass) {
			var speeds = GetValidSpeedsForClass(playerClass);
			var speedPredictions = new Dictionary<decimal, FleetPrediction>();
			var maxFuel = loot.ConvertedDeuterium * ratio;
			foreach (var speed in speeds) {
				speedPredictions.Add(speed, CalcFleetPrediction(origin, destination, ships, Missions.Attack, speed, researches, serverData, playerClass));
			}
			if (speedPredictions.Any(p => p.Value.Fuel < maxFuel && p.Value.Time < maxFlightTime)) {
				return speedPredictions
				.Where(p => p.Value.Fuel < maxFuel)
				.Where(p => p.Value.Time < maxFlightTime)
				.OrderByDescending(p => p.Key)
				.First()
				.Key;
			} else {
				return Speeds.HundredPercent;
			}
		}

		public static decimal CalcOptimalFarmSpeed(Celestial origin, Coordinate destination, Ships ships, Resources loot, decimal ratio, long maxFlightTime, Researches researches, ServerData serverData, CharacterClass playerClass) {
			return CalcOptimalFarmSpeed(origin.Coordinate, destination, ships, loot, ratio, maxFlightTime, researches, serverData, playerClass);
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

		public static Ships CalcIdealExpeditionShips(Buildables buildable, int ecoSpeed, float topOnePoints, int hyperspaceTech, CharacterClass playerClass, int probeCargo = 0) {
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

		public static Ships CalcExpeditionShips(Ships fleet, Buildables primaryShip, int expeditionsNumber, int ecoSpeed, float topOnePoints, int hyperspaceTech, CharacterClass playerClass, int probeCargo = 0) {
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

		public static long CalcEnergyProduction(Buildables buildable, int level, int energyTechnology = 0, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
			long prod = 0;
			if (buildable == Buildables.SolarPlant) {
				prod = (long) Math.Round(20 * level * Math.Pow(1.1, level) * ratio);
			} else if (buildable == Buildables.FusionReactor) {
				prod = (long) Math.Round(30 * level * Math.Pow(1.05 + (0.01 * energyTechnology), level) * ratio);
			}

			if (hasEngineer) {
				prod += (long) Math.Round(prod * 0.1);
			}
			if (hasStaff) {
				prod += (long) Math.Round(prod * 0.02);
			}
			if (playerClass == CharacterClass.Collector) {
				prod += (long) Math.Round(prod * 0.1);
			}

			return prod;
		}

		public static long CalcEnergyProduction(Buildables buildable, int level, Researches researches, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
			return CalcEnergyProduction(buildable, level, researches.EnergyTechnology, ratio, playerClass, hasEngineer, hasStaff);
		}

		public static long CalcMetalProduction(int level, int position, int speedFactor, float ratio = 1, int plasma = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
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
			int crawlerProd = 0;
			if (crawlers > 0) {
				crawlerProd = (int) Math.Round(prod * crawlers * 0.003);
			}
			return (long) Math.Round(((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio + crawlerProd * crawlerRatio), 0);
		}

		public static long CalcMetalProduction(Buildings buildings, int position, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcMetalProduction(buildings.MetalMine, position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public static long CalcMetalProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public static long CalcCrystalProduction(int level, int position, int speedFactor, float ratio = 1, int plasma = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
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
			int crawlerProd = 0;
			if (crawlers > 0) {
				crawlerProd = (int) Math.Round(prod * crawlers * 0.003);
			}
			return (long) Math.Round(((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio + crawlerProd * crawlerRatio), 0);
		}

		public static long CalcCrystalProduction(Buildings buildings, int position, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcCrystalProduction(buildings.CrystalMine, position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public static long CalcCrystalProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public static long CalcDeuteriumProduction(int level, float temp, int speedFactor, float ratio = 1, int plasma = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
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
			int crawlerProd = 0;
			if (crawlers > 0) {
				crawlerProd = (int) Math.Round(prod * crawlers * 0.003);
			}
			return (long) Math.Round(((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio + crawlerProd * crawlerRatio), 0);
		}

		public static long CalcDeuteriumProduction(Buildings buildings, Temperature temp, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcDeuteriumProduction(buildings.CrystalMine, temp.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public static long CalcDeuteriumProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			return CalcDeuteriumProduction(planet.Buildings.CrystalMine, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public static Resources CalcPlanetHourlyProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			Resources hourlyProduction = new() {
				Metal = CalcMetalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio),
				Crystal = CalcCrystalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio),
				Deuterium = CalcDeuteriumProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio)
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
					output.Metal = (long) (4000 * Math.Pow(1.75, level - 1));
					output.Crystal = (long) (8000 * Math.Pow(1.75, level - 1));
					output.Deuterium = (long) (4000 * Math.Pow(1.75, level - 1));
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

			if (celestials == null) {
				return 0;
			}

			output = celestials
				.Where(c => c.Coordinate.Type == Celestials.Planet)
				.Where(c => c.Facilities != null)
				.OrderByDescending(c => c.Facilities.ResearchLab)
				.Take(researches.IntergalacticResearchNetwork + 1)
				.Sum(c => c.Facilities.ResearchLab);

			return output;
		}

		public static long CalcProductionTime(Buildables buildable, int level, ServerData serverData, Facilities facilities, int cumulativeLabLevel = 0) {
			return CalcProductionTime(buildable, level, serverData.Speed, facilities, cumulativeLabLevel);
		}

		public static long CalcProductionTime(Buildables buildable, int level, int speed = 1, Facilities facilities = null, int cumulativeLabLevel = 0, bool isDiscoverer = false, bool hasTechnocrat = false) {
			if (facilities == null) {
				facilities = new() {
					RoboticsFactory = 0,
					NaniteFactory = 0,
					Shipyard = 1,
					ResearchLab = 1
				};
			}

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
					output = (double) structuralIntegrity / ((double) 2500 * ((double) 1 + (double) facilities.RoboticsFactory) * (double) speed * (double) Math.Pow(2, facilities.NaniteFactory));
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
					output = (double) structuralIntegrity / ((double) 2500 * ((double) 1 + (double) facilities.Shipyard) * (double) speed * (double) Math.Pow(2, facilities.NaniteFactory));
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
					output = (double) structuralIntegrity / ((double) 1000 * ((double) 1 + (double) cumulativeLabLevel) * (double) speed);
					if (isDiscoverer) {
						output = output * 3 / 4;
					}
					if (hasTechnocrat) {
						output = output * 3 / 4;
					}
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

		public static long GetProductionEnergyDelta(Buildables buildable, int level, int energyTechnology = 0, float ratio = 1, CharacterClass userClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
			if (buildable == Buildables.SolarSatellite || buildable == Buildables.SolarPlant || buildable == Buildables.FusionReactor) {
				if (level > 1) {
					var prevLevelEnergy = CalcEnergyProduction(buildable, level - 1, energyTechnology, ratio, userClass, hasEngineer, hasStaff);
					var thisLevelEnergy = CalcEnergyProduction(buildable, level, energyTechnology, ratio, userClass, hasEngineer, hasStaff);
					return thisLevelEnergy - prevLevelEnergy;
				} else
					return CalcEnergyProduction(buildable, 1, energyTechnology, ratio, userClass, hasEngineer, hasStaff);
			} else
				return 0;
		}

		public static int GetNextLevel(Celestial planet, Buildables buildable, bool isCollector = false, bool hasEngineer = false, bool hasFullStaff = false) {
			int output = 0;
			if (buildable == Buildables.SolarSatellite) {
				if (planet is Planet)
					output = CalcNeededSolarSatellites(planet as Planet, Math.Abs(planet.Resources.Energy), isCollector, hasEngineer, hasFullStaff);
			} else if (output == 0 && planet is Planet) {
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

		public static int GetNextLevel(Celestial planet, LFBuildables buildable) {
			int output = 0;
			if (planet is Planet) {
				foreach (PropertyInfo prop in planet.LFBuildings.GetType().GetProperties()) {
					if (prop.Name == buildable.ToString()) {
						output = (int) prop.GetValue(planet.LFBuildings) + 1;
					}
				}
			}
			return output;
		}

		public static int GetNextLevel(Celestial planet, LFTechno buildable) {
			int output = 0;
			if (planet is Planet) {
				foreach (PropertyInfo prop in planet.LFTechs.GetType().GetProperties()) {
					if (prop.Name == buildable.ToString()) {
						output = (int) prop.GetValue(planet.LFTechs) + 1;
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
			if (planet.ResourcesProduction.Energy.Available < 0)
				return true;
			else
				return false;
		}

		public static Buildables GetNextEnergySourceToBuild(Planet planet, int maxSolarPlant, int maxFusionReactor) {
			if (planet.Buildings.SolarPlant < maxSolarPlant)
				return Buildables.SolarPlant;
			if (planet.Buildings.DeuteriumSynthesizer >= 5 && planet.Buildings.FusionReactor < maxFusionReactor)
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
			if (requiredEnergy <= 0) {
				if (planet.Resources.Energy > 0)
					return 0;
				return (int) Math.Round((float) (Math.Abs(planet.Resources.Energy) / (float) GetSolarSatelliteOutput(planet, isCollector, hasEngineer, hasFullStaff)), MidpointRounding.ToPositiveInfinity);
			} else
				return (int) Math.Round((float) (Math.Abs(requiredEnergy) / (float) GetSolarSatelliteOutput(planet, isCollector, hasEngineer, hasFullStaff)), MidpointRounding.ToPositiveInfinity);
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

		public static Buildables GetNextMineToBuild(Planet planet, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500) {
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

				dic.Add(mine, CalcDaysOfInvestmentReturn(planet, mine, researches, speedFactor, ratio, playerClass, hasGeologist, hasStaff));
			}
			if (dic.Count == 0)
				return Buildables.Null;

			dic = dic.OrderBy(m => m.Value)
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
				float currentOneDayProd = 1;
				float nextOneDayProd = 1;
				switch (buildable) {
					case Buildables.MetalMine:
						currentOneDayProd = CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
						nextOneDayProd = CalcMetalProduction(planet.Buildings.MetalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
						break;
					case Buildables.CrystalMine:
						currentOneDayProd = CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
						nextOneDayProd = CalcCrystalProduction(planet.Buildings.CrystalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
						break;
					case Buildables.DeuteriumSynthesizer:
						currentOneDayProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) * 24;
						nextOneDayProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer + 1, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) * 24;
						break;
					default:

						break;
				}
				long cost = CalcPrice(buildable, GetNextLevel(planet, buildable)).ConvertedDeuterium;
				float delta = nextOneDayProd - currentOneDayProd;
				return cost / delta;
			} else
				return float.MaxValue;
		}

		public static float CalcNextDaysOfInvestmentReturn(Planet planet, Researches researches = null, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			var metalCost = CalcPrice(Buildables.MetalMine, GetNextLevel(planet, Buildables.MetalMine)).ConvertedDeuterium;
			var currentOneDayMetalProd = CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
			var nextOneDayMetalProd = CalcMetalProduction(planet.Buildings.MetalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
			float metalDOIR = metalCost / (float) (nextOneDayMetalProd - currentOneDayMetalProd);
			var crystalCost = CalcPrice(Buildables.CrystalMine, GetNextLevel(planet, Buildables.CrystalMine)).ConvertedDeuterium;
			var currentOneDayCrystalProd = CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
			var nextOneDayCrystalProd = CalcCrystalProduction(planet.Buildings.CrystalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
			float crystalDOIR = crystalCost / (float) (nextOneDayCrystalProd - currentOneDayCrystalProd);
			var deuteriumCost = CalcPrice(Buildables.DeuteriumSynthesizer, GetNextLevel(planet, Buildables.DeuteriumSynthesizer)).ConvertedDeuterium;
			var currentOneDayDeuteriumProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) * 24;
			var nextOneDayDeuteriumProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer + 1, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) * 24;
			float deuteriumDOIR = deuteriumCost / (float) (nextOneDayDeuteriumProd - currentOneDayDeuteriumProd);

			return Math.Min(float.IsNaN(deuteriumDOIR) ? float.MaxValue : deuteriumDOIR, Math.Min(float.IsNaN(crystalDOIR) ? float.MaxValue : crystalDOIR, float.IsNaN(metalDOIR) ? float.MaxValue : metalDOIR));
		}

		public static float CalcNextPlasmaTechDOIR(List<Planet> planets, Researches researches, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			var nextPlasmaLevel = researches.PlasmaTechnology + 1;
			var nextPlasmaCost = CalcPrice(Buildables.PlasmaTechnology, nextPlasmaLevel).ConvertedDeuterium;

			long currentProd = 0;
			long nextProd = 0;
			foreach (var planet in planets) {
				currentProd += (long) Math.Round(CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24, 0);
				currentProd += (long) Math.Round(CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24, 0);
				currentProd += CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) * 24;

				nextProd += (long) Math.Round(CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, nextPlasmaLevel, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24, 0);
				nextProd += (long) Math.Round(CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, nextPlasmaLevel, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24, 0);
				nextProd += CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, nextPlasmaLevel, playerClass, hasGeologist, hasStaff) * 24;
			}

			float delta = nextProd - currentProd;
			return nextPlasmaCost / delta;
		}

		public static float CalcNextAstroDOIR(List<Planet> planets, Researches researches, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			var nextAstroCost = CalcPrice(Buildables.Astrophysics, researches.Astrophysics + 1).ConvertedDeuterium;
			if (researches.Astrophysics % 2 != 0) {
				nextAstroCost += CalcPrice(Buildables.Astrophysics, researches.Astrophysics + 2).ConvertedDeuterium;
			}
			int averageMetal = (int) Math.Round(planets.Average(p => p.Buildings.MetalMine), 0);
			long metalCost = 0;
			for (int i = 1; i <= averageMetal; i++) {
				metalCost += CalcPrice(Buildables.MetalMine, i).ConvertedDeuterium;
			}
			int averageCrystal = (int) Math.Round(planets.Average(p => p.Buildings.CrystalMine), 0);
			long crystalCost = 0;
			for (int i = 1; i <= averageCrystal; i++) {
				crystalCost += CalcPrice(Buildables.CrystalMine, i).ConvertedDeuterium;
			}
			int averageDeuterium = (int) Math.Round(planets.Average(p => p.Buildings.DeuteriumSynthesizer), 0);
			long deuteriumCost = 0;
			for (int i = 1; i <= averageDeuterium; i++) {
				deuteriumCost += CalcPrice(Buildables.DeuteriumSynthesizer, i).ConvertedDeuterium;
			}
			long totalCost = nextAstroCost + metalCost + crystalCost + deuteriumCost;

			long dailyProd = 0;
			foreach (var planet in planets) {
				dailyProd += (long) Math.Round(CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24, 0);
				dailyProd += (long) Math.Round(CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24, 0);
				dailyProd += CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff) * 24;
			}
			long nextDailyProd = dailyProd + (long) Math.Round((float) dailyProd / (float) planets.Count, 0);

			float delta = nextDailyProd - dailyProd;
			return totalCost / delta;
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

		private static Dictionary<string, int> GetLFBuildingRequirements(LFBuildables buildable) {
			var rez = new Dictionary<string, int>();
			//Humans
			if (buildable == LFBuildables.ResearchCentre) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 12 }, { "BiosphereFarm", 13 } };
				return rez;
			} else if (buildable == LFBuildables.AcademyOfSciences) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 40 } };
				return rez;
			} else if (buildable == LFBuildables.NeuroCalibrationCentre) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 40 }, { "AcademyOfSciences", 1 }, { "FusionPoweredProduction", 1 }, { "Skyscraper", 5 } };
				return rez;
			} else if (buildable == LFBuildables.HighEnergySmelting) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 12 }, { "BiosphereFarm", 13 }, { "ResearchCentre", 5 } };
				return rez;
			} else if (buildable == LFBuildables.FoodSilo) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 12 }, { "BiosphereFarm", 13 }, { "ResearchCentre", 5 }, { "HighEnergySmelting", 3 } };
				return rez;
			} else if (buildable == LFBuildables.FusionPoweredProduction) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 40 }, { "AcademyOfSciences", 1 } };
				return rez;
			} else if (buildable == LFBuildables.Skyscraper) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 40 }, { "AcademyOfSciences", 1 }, { "FusionPoweredProduction", 1 } };
				return rez;
			} else if (buildable == LFBuildables.BiotechLab) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 40 }, { "AcademyOfSciences", 1 }, { "FusionPoweredProduction", 2 } };
				return rez;
			} else if (buildable == LFBuildables.Metropolis) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 40 }, { "AcademyOfSciences", 1 }, { "FusionPoweredProduction", 1 }, { "Skyscraper", 5 }, { "NeuroCalibrationCentre", 1 } };
				return rez;
			} else if (buildable == LFBuildables.PlanetaryShield) {
				rez = new Dictionary<string, int> { { "ResidentialSector", 40 }, { "BiosphereFarm", 13 }, { "ResearchCentre", 5 }, { "AcademyOfSciences", 1 }, { "FusionPoweredProduction", 5 }, { "Skyscraper", 5 }, { "HighEnergySmelting", 3 }, { "Metropolis", 5 }, { "FoodSilo", 4 }, { "NeuroCalibrationCentre", 5 } };
				return rez;
			}
			//Rocktal
			else if (buildable == LFBuildables.RuneTechnologium) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 21 }, { "CrystalFarm", 22 } };
				return rez;
			} else if (buildable == LFBuildables.RuneForge) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 41 }, { "CrystalFarm", 22 } };
				return rez;
			} else if (buildable == LFBuildables.Oriktorium) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 41 }, { "RuneForge", 1 }, { "Megalith", 1 }, { "CrystalRefinery", 5 } };
				return rez;
			} else if (buildable == LFBuildables.MagmaForge) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 21 }, { "CrystalFarm", 22 }, { "ResearchCentre", 5 }, { "RuneTechnologium", 5 } };
				return rez;
			} else if (buildable == LFBuildables.DisruptionChamber) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 21 }, { "CrystalFarm", 22 }, { "RuneTechnologium", 5 }, { "MagmaForge", 3 } };
				return rez;
			} else if (buildable == LFBuildables.Megalith) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 41 }, { "RuneForge", 1 } };
				return rez;
			} else if (buildable == LFBuildables.CrystalRefinery) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 41 }, { "RuneForge", 1 }, { "Megalith", 1 } };
				return rez;
			} else if (buildable == LFBuildables.DeuteriumSynthesiser) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 41 }, { "RuneForge", 1 }, { "FusionPoweredProduction", 1 }, { "Megalith", 2 } };
				return rez;
			} else if (buildable == LFBuildables.MineralResearchCentre) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 41 }, { "RuneForge", 1 }, { "Megalith", 1 }, { "CrystalRefinery", 6 }, { "Oriktorium", 1 } };
				return rez;
			} else if (buildable == LFBuildables.MetalRecyclingPlant) {
				rez = new Dictionary<string, int> { { "MeditationEnclave", 41 }, { "CrystalFarm", 22 }, { "RuneForge", 1 }, { "Megalith", 5 }, { "CrystalRefinery", 6 }, { "Oriktorium", 5 }, { "RuneTechnologium", 5 }, { "MagmaForge", 3 }, { "DisruptionChamber", 4 }, { "MineralResearchCentre", 5 } };
				return rez;
			}
			//Mechas
			else if (buildable == LFBuildables.RoboticsResearchCentre) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 20 }, { "FusionCellFactory", 17 } };
				return rez;
			} else if (buildable == LFBuildables.UpdateNetwork) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 41 } };
				return rez;
			} else if (buildable == LFBuildables.QuantumComputerCentre) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 41 }, { "UpdateNetwork", 1 }, { "MicrochipAssemblyLine", 1 }, { "ProductionAssemblyHall", 5 } };
				return rez;
			} else if (buildable == LFBuildables.AutomatisedAssemblyCentre) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 17 }, { "FusionCellFactory", 20 }, { "RoboticsResearchCentre", 5 } };
				return rez;
			} else if (buildable == LFBuildables.HighPerformanceTransformer) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 17 }, { "FusionCellFactory", 20 }, { "RoboticsResearchCentre", 5 }, { "AutomatisedAssemblyCentre", 3 } };
				return rez;
			} else if (buildable == LFBuildables.MicrochipAssemblyLine) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 41 }, { "UpdateNetwork", 1 } };
				return rez;
			} else if (buildable == LFBuildables.ProductionAssemblyHall) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 41 }, { "UpdateNetwork", 1 }, { "MicrochipAssemblyLine", 1 } };
				return rez;
			} else if (buildable == LFBuildables.HighPerformanceSynthesiser) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 41 }, { "UpdateNetwork", 1 }, { "MicrochipAssemblyLine", 1 } };
				return rez;
			} else if (buildable == LFBuildables.ChipMassProduction) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 41 }, { "UpdateNetwork", 1 }, { "MicrochipAssemblyLine", 1 }, { "ProductionAssemblyHall", 6 }, { "QuantumComputerCentre", 1 } };
				return rez;
			} else if (buildable == LFBuildables.NanoRepairBots) {
				rez = new Dictionary<string, int> { { "AssemblyLine", 41 }, { "FusionCellFactory", 20 }, { "RuneForge", 1 }, { "MicrochipAssemblyLine", 5 }, { "RoboticsResearchCentre", 5 }, { "HighPerformanceTransformer", 4 }, { "ProductionAssemblyHall", 6 }, { "QuantumComputerCentre", 5 }, { "ChipMassProduction", 11 } };
				return rez;
			}
			//Kaelesh
			else if (buildable == LFBuildables.VortexChamber) {
				rez = new Dictionary<string, int> { { "Sanctuary", 20 }, { "AntimatterCondenser", 21 } };
				return rez;
			} else if (buildable == LFBuildables.HallsOfRealisation) {
				rez = new Dictionary<string, int> { { "Sanctuary", 42 } };
				return rez;
			} else if (buildable == LFBuildables.ForumOfTranscendence) {
				rez = new Dictionary<string, int> { { "Sanctuary", 42 }, { "HallsOfRealisation", 1 }, { "ChrysalisAccelerator", 1 }, { "BioModifier", 5 } };
				return rez;
			} else if (buildable == LFBuildables.AntimatterConvector) {
				rez = new Dictionary<string, int> { { "Sanctuary", 20 }, { "AntimatterCondenser", 21 }, { "VortexChamber", 5 } };
				return rez;
			} else if (buildable == LFBuildables.CloningLaboratory) {
				rez = new Dictionary<string, int> { { "Sanctuary", 20 }, { "AntimatterCondenser", 21 }, { "VortexChamber", 5 }, { "AntimatterConvector", 3 } };
				return rez;
			} else if (buildable == LFBuildables.ChrysalisAccelerator) {
				rez = new Dictionary<string, int> { { "Sanctuary", 42 }, { "HallsOfRealisation", 1 } };
				return rez;
			} else if (buildable == LFBuildables.BioModifier) {
				rez = new Dictionary<string, int> { { "Sanctuary", 42 }, { "HallsOfRealisation", 1 }, { "ChrysalisAccelerator", 1 } };
				return rez;
			} else if (buildable == LFBuildables.PsionicModulator) {
				rez = new Dictionary<string, int> { { "Sanctuary", 42 }, { "HallsOfRealisation", 1 }, { "ChrysalisAccelerator", 1 } };
				return rez;
			} else if (buildable == LFBuildables.ShipManufacturingHall) {
				rez = new Dictionary<string, int> { { "Sanctuary", 42 }, { "HallsOfRealisation", 1 }, { "ChrysalisAccelerator", 1 }, { "BioModifier", 6 }, { "ForumOfTranscendence", 1 }, { "QuantumComputerCentre", 1 } };
				return rez;
			} else if (buildable == LFBuildables.SupraRefractor) {
				rez = new Dictionary<string, int> { { "Sanctuary", 42 }, { "AntimatterCondenser", 21 }, { "VortexChamber", 5 }, { "AntimatterConvector", 3 }, { "CloningLaboratory", 4 }, { "HallsOfRealisation", 1 }, { "ChrysalisAccelerator", 5 }, { "BioModifier", 6 }, { "ShipManufacturingHall", 5 }, { "ForumOfTranscendence", 5 } };
				return rez;
			} else {
				return new Dictionary<string, int>();
			}


		}

		public static bool isUnlocked(Celestial celestial, LFBuildables buildable) {
			var LFproperties = celestial.LFBuildings.GetType();
			var reqlist = GetLFBuildingRequirements(buildable);

			bool isEmpty;
			using (var dictionaryEnum = reqlist.GetEnumerator()) {
				isEmpty = !dictionaryEnum.MoveNext();
			}
			if (isEmpty)
				return true; //nextBuilding do not need requirement (base building)

			foreach (var item in reqlist) {
				var prop = LFproperties.GetProperty(item.Key);
				if ((int) prop.GetValue(celestial.LFBuildings) < item.Value)
					return false;
			}
			return true;
		}

		public static LFBuildables GetNextLFBuildingToBuild(Celestial planet, int maxPopuFactory = 100, int maxFoodFactory = 100, int maxTechFactory = 20) {
			LFBuildables nextLFbuild = LFBuildables.None;
			if (planet is Moon || planet.LFtype == LFTypes.None)
				return nextLFbuild;

			LFBuildables T2 = LFBuildables.None;
			LFBuildables T3 = LFBuildables.None;
			var T2lifeformNextlvl = 0;
			var T3lifeformNextlvl = 0;
			if (ShouldBuildLFBasics(planet, maxPopuFactory, maxFoodFactory)) {
				//if ((planet.ResourcesProduction.Population.LivingSpace < planet.ResourcesProduction.Population.Satisfied) || planet.ResourcesProduction.Food.Overproduction > 0) {
				if ((planet.ResourcesProduction.Population.LivingSpace / planet.ResourcesProduction.Population.Satisfied < 0.86)) {
					if (planet.LFtype == LFTypes.Humans) {
						nextLFbuild = LFBuildables.ResidentialSector;
					} else if (planet.LFtype == LFTypes.Rocktal) {
						nextLFbuild = LFBuildables.MeditationEnclave;
					} else if (planet.LFtype == LFTypes.Mechas) {
						nextLFbuild = LFBuildables.AssemblyLine;
					} else if (planet.LFtype == LFTypes.Kaelesh) {
						nextLFbuild = LFBuildables.Sanctuary;
					}
					//Force building population if max population reached
					if (planet.ResourcesProduction.Population.Available == planet.ResourcesProduction.Population.LivingSpace) {
						return nextLFbuild;
					}

				} else if ((planet.ResourcesProduction.Population.LivingSpace / planet.ResourcesProduction.Population.Satisfied > 0.86) || planet.ResourcesProduction.Population.Hungry > 0) {
					if (planet.LFtype == LFTypes.Humans) {
						nextLFbuild = LFBuildables.BiosphereFarm;
					} else if (planet.LFtype == LFTypes.Rocktal) {
						nextLFbuild = LFBuildables.CrystalFarm;
					} else if (planet.LFtype == LFTypes.Mechas) {
						nextLFbuild = LFBuildables.FusionCellFactory;
					} else if (planet.LFtype == LFTypes.Kaelesh) {
						nextLFbuild = LFBuildables.AntimatterCondenser;
					}
					//Forced build food if people are dying or livingspace higher than food (people gonna die)
					if ((planet.ResourcesProduction.Population.Hungry > 0 || planet.ResourcesProduction.Population.LivingSpace > planet.ResourcesProduction.Population.Satisfied)) {
						return nextLFbuild;
					}
				}
			} else {
				Helpers.WriteLog(LogType.Debug, LogSender.Brain, $"Careful! Celestial {planet.ToString()} reached max basics building level speicified in settings, Skipping..");
			}
			
			if (nextLFbuild != LFBuildables.None) {
				var nextLFbuildLvl = Helpers.GetNextLevel(planet, nextLFbuild);
				Resources nextLFbuildcost = Tbot.Program.ogamedService.GetPrice(nextLFbuild, nextLFbuildLvl);
				//Check if less expensive building found (allow build all LF building once basic building are high lvl, instead of checkin them one by one for each lifeform)
				LFBuildables LessExpensiveLFbuild = GetLessExpensiveLFBuilding(planet, planet.LFtype, nextLFbuildcost, maxTechFactory);
				// Prevent chosing food building because less expensive whereas it is not needed
				if (LessExpensiveLFbuild != LFBuildables.None && LessExpensiveLFbuild != LFBuildables.BiosphereFarm && LessExpensiveLFbuild != LFBuildables.CrystalFarm && LessExpensiveLFbuild != LFBuildables.FusionCellFactory && LessExpensiveLFbuild != LFBuildables.AntimatterCondenser){
					nextLFbuild = LessExpensiveLFbuild;
				}
			}

			//Check if can build T2 or T3 Lifeorms
			if (planet.LFtype == LFTypes.Humans) {
				T2 = LFBuildables.AcademyOfSciences;
				T3 = LFBuildables.NeuroCalibrationCentre;
			} else if (planet.LFtype == LFTypes.Rocktal) {
				T2 = LFBuildables.RuneForge;
				T3 = LFBuildables.Oriktorium;
			} else if (planet.LFtype == LFTypes.Mechas) {
				T2 = LFBuildables.UpdateNetwork;
				T3 = LFBuildables.QuantumComputerCentre;
			} else if (planet.LFtype == LFTypes.Kaelesh) {
				T2 = LFBuildables.HallsOfRealisation;
				T3 = LFBuildables.ForumOfTranscendence;
			}

			if (T2 != LFBuildables.None && isUnlocked(planet, T2)) {
				T2lifeformNextlvl = Helpers.GetNextLevel(planet, T2);
				Resources T2cost = Tbot.Program.ogamedService.GetPrice(T2, T2lifeformNextlvl);
				if ((int) planet.ResourcesProduction.Population.Available >= (int) T2cost.Population) {
					nextLFbuild = T2;
				}
			}

			if (T3 != LFBuildables.None && isUnlocked(planet, T3)) {
				T3lifeformNextlvl = Helpers.GetNextLevel(planet, T3);
				Resources T3cost = Tbot.Program.ogamedService.GetPrice(T3, T3lifeformNextlvl);
				if ((int) planet.ResourcesProduction.Population.Available >= (int) T3cost.Population) {
					nextLFbuild = T3;
				}
			}

			return nextLFbuild;
		}

		private static LFBuildables GetLessExpensiveLFBuilding(Celestial planet, LFTypes lftype, Resources Currentlfbuildingcost, int maxTechFactory = 20) {
			if (lftype == LFTypes.Humans) {
				foreach (HumansBuildables nextbuildable in Enum.GetValues<HumansBuildables>()) {
					if ((LFBuildables) nextbuildable == LFBuildables.ResearchCentre && planet.LFBuildings.ResearchCentre >= maxTechFactory)
						continue;
					if (isUnlocked(planet, (LFBuildables) nextbuildable)) {
						var nextLFbuildlvl = Helpers.GetNextLevel(planet, (LFBuildables) nextbuildable);
						if (Tbot.Program.ogamedService.GetPrice((LFBuildables) nextbuildable, nextLFbuildlvl).TotalResources < Currentlfbuildingcost.TotalResources)
							return (LFBuildables) nextbuildable;
					}
				}
			} else if (lftype == LFTypes.Rocktal) {
				foreach (RocktalBuildables nextbuildable in Enum.GetValues<RocktalBuildables>()) {
					if ((LFBuildables) nextbuildable == LFBuildables.RuneTechnologium && planet.LFBuildings.RuneTechnologium >= maxTechFactory)
						continue;
					if (isUnlocked(planet, (LFBuildables) nextbuildable)) {
						var nextLFbuildlvl = Helpers.GetNextLevel(planet, (LFBuildables) nextbuildable);
						if (Tbot.Program.ogamedService.GetPrice((LFBuildables) nextbuildable, nextLFbuildlvl).TotalResources < Currentlfbuildingcost.TotalResources)
							return (LFBuildables) nextbuildable;
					}
				}
			} else if (lftype == LFTypes.Mechas) {
				foreach (MechasBuildables nextbuildable in Enum.GetValues<MechasBuildables>()) {
					if ((LFBuildables) nextbuildable == LFBuildables.RoboticsResearchCentre && planet.LFBuildings.RoboticsResearchCentre >= maxTechFactory)
						continue;
					if (isUnlocked(planet, (LFBuildables) nextbuildable)) {
						var nextLFbuildlvl = Helpers.GetNextLevel(planet, (LFBuildables) nextbuildable);
						if (Tbot.Program.ogamedService.GetPrice((LFBuildables) nextbuildable, nextLFbuildlvl).TotalResources < Currentlfbuildingcost.TotalResources)
							return (LFBuildables) nextbuildable;
					}
				}
			} else if (lftype == LFTypes.Kaelesh) {
				foreach (KaeleshBuildables nextbuildable in Enum.GetValues<KaeleshBuildables>()) {
					if ((LFBuildables) nextbuildable == LFBuildables.VortexChamber && planet.LFBuildings.VortexChamber >= maxTechFactory)
						continue;
					if (isUnlocked(planet, (LFBuildables) nextbuildable)) {
						var nextLFbuildlvl = Helpers.GetNextLevel(planet, (LFBuildables) nextbuildable);
						if (Tbot.Program.ogamedService.GetPrice((LFBuildables) nextbuildable, nextLFbuildlvl).TotalResources < Currentlfbuildingcost.TotalResources)
							return (LFBuildables) nextbuildable;
					}
				}
			}
			return LFBuildables.None;
		}

		public static LFTechno GetNextLFTechToBuild(Celestial celestial, int MaxReasearchLevel) {
			//TODO
			//As planets can have any lifeform techs, its complicated to find which techs are existing on a planet if the techs are not at least level 1
			//Therefore, for the moment, up only techs that are minimum level 1, its a way to also allows player to chose which research to up
			foreach (PropertyInfo prop in celestial.LFTechs.GetType().GetProperties()) {
				foreach (LFTechno nextLFTech in Enum.GetValues<LFTechno>()) {
					//skip intergalactic envoys tech cuz we dont care
					if (prop.Name == "IntergalacticEnvoys" || nextLFTech == LFTechno.IntergalacticEnvoys)
						continue;
					if ((int) prop.GetValue(celestial.LFTechs) > 0 && (int) prop.GetValue(celestial.LFTechs) < MaxReasearchLevel && prop.Name == nextLFTech.ToString()) {
						return nextLFTech;
					}

				}

			}
			return LFTechno.None;
		}

		public static LFTechno GetLessExpensiveLFTechToBuild(Celestial celestial, Resources currentcost) {
			LFTechno nextLFtech = LFTechno.None;
			foreach (PropertyInfo prop in celestial.LFTechs.GetType().GetProperties()) {
				foreach (LFTechno next in Enum.GetValues<LFTechno>()) {
					if (prop.Name == "IntergalacticEnvoys" || next == LFTechno.IntergalacticEnvoys)
						continue;
					if ((int) prop.GetValue(celestial.LFTechs) > 0 && prop.Name == next.ToString()) {
						nextLFtech = next;
						var nextLFtechlvl = Helpers.GetNextLevel(celestial, nextLFtech);
						Resources newcost = Tbot.Program.ogamedService.GetPrice(nextLFtech, nextLFtechlvl);
						if (newcost.TotalResources < currentcost.TotalResources) {
							return nextLFtech;
						}
					}

				}

			}
			return LFTechno.None;
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
			if (buildableToBuild == Buildables.Null)
				buildableToBuild = GetNextFacilityToBuild(planet, researches, maxBuildings, maxFacilities, playerClass, staff, serverData, settings, ratio, true);

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
		public static Buildables GetNextFacilityToBuild(Planet planet, Researches researches, Buildings maxBuildings, Facilities maxFacilities, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1, bool force = false) {
			Buildables facilityToBuild = Buildables.Null;
			if (settings.PrioritizeRobotsAndNanites)
				if (planet.Facilities.RoboticsFactory < 10 && planet.Facilities.RoboticsFactory < maxFacilities.RoboticsFactory)
					facilityToBuild = Buildables.RoboticsFactory;
				else if (planet.Facilities.RoboticsFactory >= 10 && researches.ComputerTechnology >= 10 && planet.Facilities.NaniteFactory < maxFacilities.NaniteFactory && !planet.HasProduction())
					facilityToBuild = Buildables.NaniteFactory;
			if (facilityToBuild == Buildables.Null && ShouldBuildSpaceDock(planet, maxFacilities.SpaceDock, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull, settings.OptimizeForStart, settings.MaxDaysOfInvestmentReturn, force))
				facilityToBuild = Buildables.SpaceDock;
			if (facilityToBuild == Buildables.Null && planet.Productions.Count == 0 && ShouldBuildNanites(planet, maxFacilities.NaniteFactory, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull, settings.OptimizeForStart, settings.MaxDaysOfInvestmentReturn, force) && !planet.HasProduction())
				facilityToBuild = Buildables.NaniteFactory;
			if (facilityToBuild == Buildables.Null && planet.Productions.Count == 0 && ShouldBuildRoboticFactory(planet, maxFacilities.RoboticsFactory, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull, settings.OptimizeForStart, settings.MaxDaysOfInvestmentReturn, force))
				facilityToBuild = Buildables.RoboticsFactory;
			if (facilityToBuild == Buildables.Null && planet.Productions.Count == 0 && ShouldBuildShipyard(planet, maxFacilities.Shipyard, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull, settings.OptimizeForStart, settings.MaxDaysOfInvestmentReturn, force) && !planet.HasProduction())
				facilityToBuild = Buildables.Shipyard;
			if (facilityToBuild == Buildables.Null && planet.Constructions.ResearchID == 0 && ShouldBuildResearchLab(planet, maxFacilities.ResearchLab, researches, serverData.Speed, serverData.ResearchDurationDivisor, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull, settings.OptimizeForStart, settings.MaxDaysOfInvestmentReturn, force) && planet.Constructions.ResearchID == 0)
				facilityToBuild = Buildables.ResearchLab;
			if (facilityToBuild == Buildables.Null && ShouldBuildMissileSilo(planet, maxFacilities.MissileSilo, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, playerClass, staff.Geologist, staff.IsFull, settings.OptimizeForStart, settings.MaxDaysOfInvestmentReturn, force))
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

		public static bool ShouldBuildRoboticFactory(Celestial celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			if (celestial is Planet) {
				var nextMine = GetNextMineToBuild(celestial as Planet, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
				var nextMineLevel = GetNextLevel(celestial, nextMine);
				var nextMinePrice = CalcPrice(nextMine, nextMineLevel);
				var nextMineTime = CalcProductionTime(nextMine, nextMineLevel, speedFactor, celestial.Facilities);

				var nextRobotsLevel = GetNextLevel(celestial, Buildables.RoboticsFactory);
				var nextRobotsPrice = CalcPrice(Buildables.RoboticsFactory, nextRobotsLevel);
				var nextRobotsTime = CalcProductionTime(Buildables.RoboticsFactory, nextRobotsLevel, speedFactor, celestial.Facilities);

				if (
					nextRobotsLevel <= maxLevel &&
					(nextMinePrice.ConvertedDeuterium > nextRobotsPrice.ConvertedDeuterium || force)
				)
					return true;
				else
					return false;
			} else {
				var nextRobotsLevel = GetNextLevel(celestial, Buildables.RoboticsFactory);

				if (
					nextRobotsLevel <= maxLevel &&
					celestial.Fields.Free > 1
				)
					return true;
				else
					return false;
			}
		}

		public static bool ShouldBuildShipyard(Celestial celestial, int maxLevel = 12, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			if (celestial is Planet) {
				var nextMine = GetNextMineToBuild(celestial as Planet, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
				var nextMineLevel = GetNextLevel(celestial, nextMine);
				var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

				var nextShipyardLevel = GetNextLevel(celestial as Planet, Buildables.Shipyard);
				var nextShipyardPrice = CalcPrice(Buildables.Shipyard, nextShipyardLevel);

				if (
					nextShipyardLevel <= maxLevel &&
					(nextMinePrice.ConvertedDeuterium > nextShipyardPrice.ConvertedDeuterium || force) &&
					celestial.Facilities.RoboticsFactory >= 2
				)
					return true;
				else
					return false;
			} else {
				var nextShipyardLevel = GetNextLevel(celestial, Buildables.Shipyard);

				if (
					nextShipyardLevel <= maxLevel &&
					celestial.Fields.Free > 1
				)
					return true;
				else
					return false;
			}
		}

		public static bool ShouldBuildResearchLab(Planet celestial, int maxLevel = 12, Researches researches = null, int speedFactor = 1, float researchDurationDivisor = 2, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

			var nextLabLevel = GetNextLevel(celestial, Buildables.ResearchLab);
			var nextLabPrice = CalcPrice(Buildables.ResearchLab, nextLabLevel);

			var nextResearch = GetNextResearchToBuild(celestial, researches);
			var nextResearchLevel = GetNextLevel(researches, nextResearch);
			var nextResearchTime = CalcProductionTime(nextResearch, nextResearchLevel, (int) Math.Round(speedFactor * researchDurationDivisor), celestial.Facilities);
			var nextLabTime = CalcProductionTime(Buildables.ResearchLab, nextLabLevel, speedFactor, celestial.Facilities);

			if (
				nextLabLevel <= maxLevel &&
				(nextMinePrice.ConvertedDeuterium > nextLabPrice.ConvertedDeuterium || force)
			)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildMissileSilo(Planet celestial, int maxLevel = 6, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

			var nextSiloLevel = GetNextLevel(celestial, Buildables.MissileSilo);
			var nextSiloPrice = CalcPrice(Buildables.MissileSilo, nextSiloLevel);

			if (
				nextSiloLevel <= maxLevel &&
				(nextMinePrice.ConvertedDeuterium > nextSiloPrice.ConvertedDeuterium || force) &&
				celestial.Facilities.Shipyard >= 1
			)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildNanites(Planet celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);
			var nextMineTime = CalcProductionTime(nextMine, nextMineLevel, speedFactor, celestial.Facilities);

			var nextNanitesLevel = GetNextLevel(celestial, Buildables.NaniteFactory);
			var nextNanitesPrice = CalcPrice(Buildables.NaniteFactory, nextNanitesLevel);
			var nextNanitesTime = CalcProductionTime(Buildables.NaniteFactory, nextNanitesLevel, speedFactor, celestial.Facilities);

			if (
				nextNanitesLevel <= maxLevel &&
				(nextMinePrice.ConvertedDeuterium > nextNanitesPrice.ConvertedDeuterium || nextNanitesTime < nextMineTime || force) &&
				celestial.Facilities.RoboticsFactory >= 10 &&
				researches.ComputerTechnology >= 10
			)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildTerraformer(Planet celestial, Researches researches, int maxLevel = 10) {
			if (researches.EnergyTechnology < 12)
				return false;
			var nextLevel = GetNextLevel(celestial, Buildables.Terraformer);
			if (
				celestial.Fields.Free == 1 &&
				nextLevel <= maxLevel
			)
				return true;
			else
				return false;
		}

		private static bool ShouldBuildLFBasics(Celestial celestial, int maxPopuFactory = 100, int maxFoodFactory = 100) {
			if (celestial.LFtype == LFTypes.Humans) {
				if ((int) celestial.LFBuildings.ResidentialSector >= maxPopuFactory && (int) celestial.LFBuildings.BiosphereFarm >= maxFoodFactory) {
					return false;
				}
			} else if (celestial.LFtype == LFTypes.Rocktal) {
				if ((int) celestial.LFBuildings.MeditationEnclave >= maxPopuFactory && (int) celestial.LFBuildings.CrystalFarm >= (int) maxFoodFactory) {
					return false;
				}
			} else if (celestial.LFtype == LFTypes.Mechas) {
				if ((int) celestial.LFBuildings.AssemblyLine >= maxPopuFactory && (int) celestial.LFBuildings.FusionCellFactory >= (int) maxFoodFactory) {
					return false;
				}
			} else if (celestial.LFtype == LFTypes.Kaelesh) {
				if ((int) celestial.LFBuildings.Sanctuary >= maxPopuFactory && (int) celestial.LFBuildings.AntimatterCondenser >= (int) maxFoodFactory) {
					return false;
				}
			}
			return true;
		}

		public static bool ShouldBuildSpaceDock(Planet celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

			var nextSpaceDockLevel = GetNextLevel(celestial, Buildables.SpaceDock);
			var nextSpaceDockPrice = CalcPrice(Buildables.SpaceDock, nextSpaceDockLevel);
			if (
				nextSpaceDockLevel <= maxLevel &&
				(nextMinePrice.ConvertedDeuterium > nextSpaceDockPrice.ConvertedDeuterium || force) &&
				celestial.ResourcesProduction.Energy.CurrentProduction >= nextSpaceDockPrice.Energy &&
				celestial.Facilities.Shipyard >= 2
			)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildLunarBase(Moon moon, int maxLevel = 8) {
			var nextLunarBaseLevel = GetNextLevel(moon, Buildables.LunarBase);

			if (
				nextLunarBaseLevel <= maxLevel &&
				moon.Fields.Free == 1
			)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildSensorPhalanx(Moon moon, int maxLevel = 7) {
			var nextSensorPhalanxLevel = GetNextLevel(moon, Buildables.SensorPhalanx);

			if (
				nextSensorPhalanxLevel <= maxLevel &&
				moon.Facilities.LunarBase >= 1 &&
				moon.Fields.Free > 1
			)
				return true;
			else
				return false;
		}

		public static bool ShouldBuildJumpGate(Moon moon, int maxLevel = 1, Researches researches = null) {
			var nextJumpGateLevel = GetNextLevel(moon, Buildables.JumpGate);

			if (
				nextJumpGateLevel <= maxLevel &&
				moon.Facilities.LunarBase >= 1 &&
				researches.HyperspaceTechnology >= 7 &&
				moon.Fields.Free > 1
			)
				return true;
			else
				return false;
		}

		public static bool ShouldResearchEnergyTech(List<Planet> planets, int energyTech, int maxEnergyTech = 25, CharacterClass playerClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
			if (energyTech >= maxEnergyTech)
				return false;
			if (!planets.Any(p => p.Buildings.FusionReactor > 0))
				return false;

			var avgFusion = (int) Math.Round(planets.Where(p => p.Buildings.FusionReactor > 0).Average(p => p.Buildings.FusionReactor));
			var energyProd = (long) Math.Round(planets.Where(p => p.Buildings.FusionReactor > 0).Average(p => CalcEnergyProduction(Buildables.FusionReactor, p.Buildings.FusionReactor, energyTech, 1, playerClass, hasEngineer, hasStaff)));
			var avgEnergyProd = CalcEnergyProduction(Buildables.FusionReactor, avgFusion, energyTech, 1, playerClass, hasEngineer, hasStaff);

			var fusionCost = (long) CalcPrice(Buildables.FusionReactor, avgFusion + 1).ConvertedDeuterium * planets.Where(p => p.Buildings.FusionReactor > 0).Count();
			var fusionEnergy = CalcEnergyProduction(Buildables.FusionReactor, avgFusion + 1, energyTech, 1, playerClass, hasEngineer, hasStaff);
			float fusionRatio = (float) fusionEnergy / (float) fusionCost;
			var energyTechCost = (long) CalcPrice(Buildables.EnergyTechnology, energyTech + 1).ConvertedDeuterium;
			var energyTechEnergy = CalcEnergyProduction(Buildables.FusionReactor, avgFusion, energyTech + 1, 1, playerClass, hasEngineer, hasStaff);
			float energyTechRatio = (float) energyTechEnergy / (float) energyTechCost;

			return energyTechRatio >= fusionRatio;
		}

		public static bool ShouldResearchEnergyTech(List<Planet> planets, Researches researches, int maxEnergyTech = 25, CharacterClass playerClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
			return ShouldResearchEnergyTech(planets, researches.EnergyTechnology, maxEnergyTech, playerClass, hasEngineer, hasStaff);
		}

		public static Buildables GetNextResearchToBuild(Planet celestial, Researches researches, bool prioritizeRobotsAndNanitesOnNewPlanets = false, Slots slots = null, int maxEnergyTechnology = 20, int maxLaserTechnology = 12, int maxIonTechnology = 5, int maxHyperspaceTechnology = 20, int maxPlasmaTechnology = 20, int maxCombustionDrive = 19, int maxImpulseDrive = 17, int maxHyperspaceDrive = 15, int maxEspionageTechnology = 8, int maxComputerTechnology = 20, int maxAstrophysics = 23, int maxIntergalacticResearchNetwork = 12, int maxWeaponsTechnology = 25, int maxShieldingTechnology = 25, int maxArmourTechnology = 25, bool optimizeForStart = true, bool ensureExpoSlots = true, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasAdmiral = false) {
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

			if (ensureExpoSlots && slots != null && CalcMaxExpeditions(researches.Astrophysics, playerClass == CharacterClass.Discoverer, hasAdmiral) + 1 > slots.Total && researches.ComputerTechnology < maxComputerTechnology)
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
						if (celestial.ResourcesProduction.Energy.CurrentProduction < CalcPrice(Buildables.GravitonTechnology, GetNextLevel(researches, Buildables.GravitonTechnology)).Energy)
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
			var inProgress = (fleets ?? new List<Fleet>()).Where(f => f.Mission == mission);
			if (inProgress.Any()) {
				return inProgress.ToList();
			} else
				return new List<Fleet>();
		}

		public static List<Fleet> GetMissionsInProgress(Coordinate origin, Missions mission, List<Fleet> fleets) {
			var inProgress = (fleets ?? new List<Fleet>())
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

		public static int CalcMaxExpeditions(Researches researches, CharacterClass playerClass, Staff staff) {
			return CalcMaxExpeditions(researches.Astrophysics, playerClass == CharacterClass.Discoverer, staff.Geologist);
		}
		public static int CalcMaxExpeditions(int astrophysics, bool isDiscoverer, bool hasAdmiral) {
			int slots = (int) Math.Round(Math.Sqrt(astrophysics), 0, MidpointRounding.ToZero);
			if (isDiscoverer)
				slots += 2;
			if (hasAdmiral)
				slots += 1;
			return slots;
		}

		public static int CalcMaxPlanets(int astrophysics) {
			return (int) Math.Round((float) ((astrophysics + 3) / 2), 0, MidpointRounding.ToZero);
		}

		public static int CalcMaxPlanets(Researches researches) {
			return researches == null ? 1 : CalcMaxPlanets(researches.Astrophysics);
		}

		public static int CalcMaxCrawlers(Planet planet, CharacterClass userClass, bool hasGeologist) {
			if (userClass == CharacterClass.Collector && hasGeologist) {
				return (int) Math.Round(8.8 * (planet.Buildings.MetalMine + planet.Buildings.CrystalMine + planet.Buildings.DeuteriumSynthesizer));
			}
			return 8 * (planet.Buildings.MetalMine + planet.Buildings.CrystalMine + planet.Buildings.DeuteriumSynthesizer);
		}

		public static int CalcOptimalCrawlers(Planet planet, CharacterClass userClass, Staff staff, Researches researches, ServerData serverData) {
			int maxCrawlers = CalcMaxCrawlers(planet, userClass, staff.Geologist);
			if (planet.Ships.Crawler >= maxCrawlers) {
				return 0;
			}

			var dic = new Dictionary<int, float>();
			for (int i = (int) planet.Ships.Crawler; i <= maxCrawlers; i++) {
				long currentProd = CalcPlanetHourlyProduction(planet, serverData.Speed, 1, researches, userClass, staff.Geologist, staff.IsFull, (int) planet.Ships.Crawler, 1.5F).ConvertedDeuterium * 24;
				long nextProd = CalcPlanetHourlyProduction(planet, serverData.Speed, 1, researches, userClass, staff.Geologist, staff.IsFull, i, 1.5F).ConvertedDeuterium * 24;
				if (nextProd > currentProd) {
					long cost = CalcPrice(Buildables.Crawler, i - (int) planet.Ships.Crawler).ConvertedDeuterium;
					dic.Add(i, (float) cost / (float) (nextProd - currentProd));
				}
			}
			var nextDOIR = CalcNextDaysOfInvestmentReturn(planet, researches, serverData.Speed, 1, userClass, staff.Geologist, staff.IsFull);
			var dic2 = dic.Where(e => e.Value <= nextDOIR);
			if (dic2.Count() == 0) {
				return 0;
			}

			return dic2.OrderBy(e => e.Value).FirstOrDefault().Key - (int) planet.Ships.Crawler;
		}
	}


	public class Optional<T> {
		private T value;
		public bool IsPresent { get; private set; } = false;

		private Optional() { }

		public static Optional<T> Empty() {
			return new Optional<T>();
		}

		public static Optional<T> Of(T value) {
			Optional<T> obj = new Optional<T>();
			obj.Set(value);
			return obj;
		}

		public void Set(T value) {
			this.value = value;
			IsPresent = true;
		}

		public T Get() {
			return value;
		}
	}
}
