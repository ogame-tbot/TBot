using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Linq;
using System.IO;
using System.Text.RegularExpressions;
using System.Numerics;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using TBot.Model;
using Microsoft.CodeAnalysis.Text;
using Microsoft.CodeAnalysis.Operations;
using System.Reflection.Emit;
using System.Data;

namespace Tbot.Includes {

	public class CalculationService : ICalculationService {
		private readonly ILoggerService<CalculationService> _logger;
		private readonly IOgameService _ogameService;

		public CalculationService(ILoggerService<CalculationService> logger,
			IOgameService ogameService) {
			_logger = logger;
			_ogameService = ogameService;
		}

		public int CalcShipCapacity(Buildables buildable, int hyperspaceTech, ServerData serverData, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			int baseCargo;
			int bonus = (hyperspaceTech * serverData.CargoHyperspaceTechMultiplier);
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

		public int CalcShipCapacity(Buildables buildable, int hyperspaceTech, ServerData serverData, float buildableCargoBonus = 0, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			int baseCargo;
			int bonus = (hyperspaceTech * serverData.CargoHyperspaceTechMultiplier);
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
			double totalBonus = Math.Round(Math.Round((double) bonus / 100, 2, MidpointRounding.ToZero) + Math.Round((double) buildableCargoBonus / 100, 2, MidpointRounding.ToZero), 2, MidpointRounding.ToZero) - 0.01;
			int output = (int) Math.Floor((double) baseCargo + (double) (baseCargo * totalBonus));
			return output;
		}

		public int CalcShipFuelCapacity(Buildables buildable, ServerData serverData, int hyperspaceTech = 0, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			return CalcShipCapacity(buildable, hyperspaceTech, serverData, playerClass, probeCargo);
		}

		public long CalcFleetCapacity(Ships fleet, ServerData serverData, int hyperspaceTech = 0, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			long total = 0;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					float bonusCargo = 0;
					if (lfBonuses != null) {
						bonusCargo = lfBonuses.GetShipCargoBonus(buildable);
					}
					int oneCargo = CalcShipCapacity(buildable, hyperspaceTech, serverData, bonusCargo, playerClass, probeCargo);
					total += oneCargo * qty;
				}
			}
			return total;
		}

		public long CalcFleetFuelCapacity(Ships fleet, ServerData serverData, int hyperspaceTech = 0, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			long total = 0;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					int oneCargo = CalcShipFuelCapacity(buildable, serverData, hyperspaceTech, playerClass, probeCargo);
					total += oneCargo * qty;
				}
			}
			return total;
		}

		public int CalcShipSpeed(Buildables buildable, Researches researches, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			if (lfBonuses == null)
				lfBonuses = new() {
					Ships = new()
				};
			float lfBonus = lfBonuses.GetShipSpeedBonus(buildable);
			return CalcShipSpeed(buildable, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, lfBonus, playerClass, allyClass);
		}

		public int CalcShipSpeed(Buildables buildable, int combustionDrive, int impulseDrive, int hyperspaceDrive, float lfBonus = 0, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
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
					if (allyClass == AllianceClass.Trader)
						bonus += 1;
					break;
				case Buildables.LargeCargo:
					baseSpeed = 7500;
					if (playerClass == CharacterClass.Collector)
						bonus += 10;
					if (allyClass == AllianceClass.Trader)
						bonus += 1;
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
					baseSpeed = 7000;
					bonus = hyperspaceDrive * 3;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				case Buildables.Pathfinder:
					baseSpeed = 12000;
					bonus = hyperspaceDrive * 3;
					if (playerClass == CharacterClass.General)
						bonus += 10;
					break;
				default:
					return 0;
			}
			var output = ((float) baseSpeed * (lfBonus / 100)) + ((float) baseSpeed * ((float) bonus + 10) / 10);
			return (int) Math.Round(output, MidpointRounding.ToZero);
		}

		public int CalcSlowestSpeed(Ships fleet, Researches researches, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			return CalcSlowestSpeed(fleet, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, lfBonuses, playerClass, allyClass);
		}

		public int CalcSlowestSpeed(Ships fleet, int combustionDrive, int impulseDrive, int hyperspaceDrive, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			int lowest = int.MaxValue;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);

				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler)
						continue;
					float lfBonus = lfBonuses.GetShipSpeedBonus(buildable);
					int speed = CalcShipSpeed(buildable, combustionDrive, impulseDrive, hyperspaceDrive, lfBonus, playerClass, allyClass);
					if (speed < lowest)
						lowest = speed;
				}
			}
			return lowest;
		}

		public int CalcFleetSpeed(Ships fleet, Researches researches, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			return CalcFleetSpeed(fleet, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, lfBonuses, playerClass, allyClass);
		}

		public int CalcFleetSpeed(Ships fleet, int combustionDrive, int impulseDrive, int hyperspaceDrive, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			int minSpeed = 0;
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(fleet, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					float lfBonus = lfBonuses.GetShipSpeedBonus(buildable);
					int thisSpeed = CalcShipSpeed(buildable, combustionDrive, impulseDrive, hyperspaceDrive, lfBonus, playerClass, allyClass);
					if (thisSpeed < minSpeed)
						minSpeed = thisSpeed;
				}
			}
			return minSpeed;
		}

		public int CalcShipConsumption(Buildables buildable, Researches researches, ServerData serverData, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass) {
			float lfBonus = lfBonuses.GetShipConsumptionBonus(buildable);
			return CalcShipConsumption(buildable, researches.ImpulseDrive, researches.HyperspaceDrive, serverData.GlobalDeuteriumSaveFactor, lfBonus, playerClass);
		}

		public int CalcShipConsumption(Buildables buildable, int impulseDrive, int hyperspaceDrive, double deuteriumSaveFactor, float lfBonus = 0, CharacterClass playerClass = CharacterClass.NoClass) {
			int baseConsumption;
			switch (buildable) {
				case Buildables.SmallCargo:
					baseConsumption = 10;
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
					baseConsumption = 300;
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
			double fuelConsumption = (double) deuteriumSaveFactor * (double) baseConsumption * (1 - lfBonus / 100);
			if (playerClass == CharacterClass.General)
				fuelConsumption /= 2;
			fuelConsumption = Math.Round(fuelConsumption);
			if (fuelConsumption < 1) {
				return 1;
			} else {
				return (int) fuelConsumption;
			}
		}

		public long CalcFlightTime(Coordinate origin, Coordinate destination, Ships ships, Missions mission, decimal speed, Researches researches, ServerData serverData, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			var fleetSpeed = mission switch {
				Missions.Attack or Missions.FederalAttack or Missions.Destroy or Missions.Spy or Missions.Harvest => serverData.SpeedFleetWar,
				Missions.FederalDefense => serverData.SpeedFleetHolding,
				_ => serverData.SpeedFleetPeaceful,
			};
			return CalcFlightTime(origin, destination, ships, speed, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem, fleetSpeed, lfBonuses, playerClass, allyClass);
		}

		public long CalcFlightTime(Coordinate origin, Coordinate destination, Ships ships, decimal speed, int combustionDrive, int impulseDrive, int hyperspaceDrive, int numberOfGalaxies, int numberOfSystems, bool donutGalaxies, bool donutSystems, int fleetSpeed, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			int slowestShipSpeed = CalcSlowestSpeed(ships, combustionDrive, impulseDrive, hyperspaceDrive, lfBonuses, playerClass, allyClass);
			int distance = CalcDistance(origin, destination, numberOfGalaxies, numberOfSystems, donutGalaxies, donutSystems);
			double s = (double) speed;
			double v = (double) slowestShipSpeed;
			double a = (double) fleetSpeed;
			double d = (double) distance;
			long output = (long) Math.Round((((double) 35000 / s) * Math.Sqrt(d * (double) 10 / v) + (double) 10) / a);
			return output;
		}

		public long CalcFuelConsumption(Coordinate origin, Coordinate destination, Ships ships, Missions mission, long flightTime, Researches researches, ServerData serverData, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			var fleetSpeed = mission switch {
				Missions.Attack or Missions.FederalAttack or Missions.Destroy or Missions.Harvest or Missions.Spy => serverData.SpeedFleetWar,
				Missions.FederalDefense => serverData.SpeedFleetHolding,
				_ => serverData.SpeedFleetPeaceful,
			};
			return CalcFuelConsumption(origin, destination, ships, flightTime, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem, fleetSpeed, serverData.GlobalDeuteriumSaveFactor, lfBonuses, playerClass, allyClass);
		}

		public long CalcFuelConsumption(Coordinate origin, Coordinate destination, Ships ships, long flightTime, int combustionDrive, int impulseDrive, int hyperspaceDrive, int numberOfGalaxies, int numberOfSystems, bool donutGalaxies, bool donutSystems, int fleetSpeed, float deuteriumSaveFactor, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			int distance = CalcDistance(origin, destination, numberOfGalaxies, numberOfSystems, donutGalaxies, donutSystems);
			double tempFuel = (double) 0;
			foreach (PropertyInfo prop in ships.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(ships, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					float lfSpeedBonus = lfBonuses.GetShipSpeedBonus(buildable);
					float lfConsBonus = lfBonuses.GetShipConsumptionBonus(buildable);
					double tempSpeed = 35000 / (((double) flightTime * (double) fleetSpeed) - (double) 10) * (double) Math.Sqrt((double) distance * (double) 10 / (double) CalcShipSpeed(buildable, combustionDrive, impulseDrive, hyperspaceDrive, lfSpeedBonus, playerClass, allyClass));
					int shipConsumption = CalcShipConsumption(buildable, impulseDrive, hyperspaceDrive, deuteriumSaveFactor, lfConsBonus, playerClass);
					double thisFuel = ((double) shipConsumption * (double) qty * (double) distance) / (double) 35000 * Math.Pow(((double) tempSpeed / (double) 10) + (double) 1, 2);
					tempFuel += thisFuel;
				}
			}
			long output = (long) (1 + Math.Round(tempFuel));
			return output;
		}
		
		public FleetPrediction CalcFleetPrediction(Coordinate origin, Coordinate destination, Ships ships, Missions mission, decimal speed, Researches researches, ServerData serverData, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			long time = CalcFlightTime(origin, destination, ships, mission, speed, researches, serverData, lfBonuses, playerClass, allyClass);
			long fuel = CalcFuelConsumption(origin, destination, ships, mission, time, researches, serverData, lfBonuses, playerClass, allyClass);
			return new() {
				Fuel = fuel,
				Time = time
			};
		}

		public FleetPrediction CalcFleetPrediction(Celestial origin, Coordinate destination, Ships ships, Missions mission, decimal speed, Researches researches, ServerData serverData, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			return CalcFleetPrediction(origin.Coordinate, destination, ships, mission, speed, researches, serverData, origin.LFBonuses, playerClass, allyClass);
		}

		public List<decimal> GetValidSpeedsForClass(CharacterClass playerClass) {
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

		public decimal CalcOptimalFarmSpeed(Coordinate origin, Coordinate destination, Ships ships, Resources loot, decimal ratio, long maxFlightTime, Researches researches, ServerData serverData, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			var speeds = GetValidSpeedsForClass(playerClass);
			var speedPredictions = new Dictionary<decimal, FleetPrediction>();
			var maxFuel = loot.ConvertedDeuterium * ratio;
			foreach (var speed in speeds) {
				speedPredictions.Add(speed, CalcFleetPrediction(origin, destination, ships, Missions.Attack, speed, researches, serverData, lfBonuses, playerClass, allyClass));
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

		public decimal CalcOptimalFarmSpeed(Celestial origin, Coordinate destination, Ships ships, Resources loot, decimal ratio, long maxFlightTime, Researches researches, ServerData serverData, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, AllianceClass allyClass = AllianceClass.NoClass) {
			return CalcOptimalFarmSpeed(origin.Coordinate, destination, ships, loot, ratio, maxFlightTime, researches, serverData, lfBonuses, playerClass, allyClass);
		}

		public Resources CalcMaxTransportableResources(Ships ships, Resources resources, int hyperspaceTech, ServerData serverData, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, long deutToLeave = 0, int probeCargo = 0) {
			var capacity = CalcFleetCapacity(ships, serverData, hyperspaceTech, lfBonuses, playerClass, probeCargo);
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

		public long CalcShipNumberForPayload(Resources payload, Buildables buildable, int hyperspaceTech, ServerData serverData, float cargoBonus = 0, CharacterClass playerClass = CharacterClass.NoClass, int probeCapacity = 0) {
			return (long) Math.Round(((float) payload.TotalResources / (float) CalcShipCapacity(buildable, hyperspaceTech, serverData, cargoBonus, playerClass, probeCapacity)), MidpointRounding.ToPositiveInfinity);
		}

		public Ships CalcIdealExpeditionShips(Buildables buildable, int hyperspaceTech, float expeditionResourcesBonus, Dictionary<int, LFBonusesShip> shipBonus, ServerData serverData, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			var fleet = new Ships();

			int ecoSpeed = serverData.Speed;
			float topOnePoints = serverData.TopScore;
			float buildableCargoBonus = 0;
			if (shipBonus != null && shipBonus.Count > 0 && shipBonus.ContainsKey((int) buildable)) {
				buildableCargoBonus = shipBonus.GetValueOrDefault((int) buildable).Cargo;
			}
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

			if(expeditionResourcesBonus > 0)
				freightCap += (int) Math.Round((float) freightCap * expeditionResourcesBonus / 100, MidpointRounding.ToPositiveInfinity);

			int oneCargoCapacity = CalcShipCapacity(buildable, hyperspaceTech, serverData, buildableCargoBonus, playerClass, probeCargo);
			int cargoNumber = (int) Math.Round((float) freightCap / (float) oneCargoCapacity, MidpointRounding.ToPositiveInfinity);

			fleet = fleet.Add(buildable, cargoNumber);

			return fleet;
		}

		public Buildables CalcMilitaryShipForExpedition(Ships fleet, int expeditionsNumber) {
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

		public Ships CalcExpeditionShips(Ships fleet, Buildables primaryShip, int expeditionsNumber, int hyperspaceTech, float expeditionsResourcesBonus, Dictionary<int, LFBonusesShip> shipsBonus, ServerData serverData, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			Ships ideal = CalcIdealExpeditionShips(primaryShip, hyperspaceTech, expeditionsResourcesBonus, shipsBonus, serverData, playerClass, probeCargo);
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

		public Ships CalcExpeditionShips(Ships fleet, Buildables primaryShip, int expeditionsNumber, ServerData serverdata, Researches researches, LFBonuses LFBonuses, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			return CalcExpeditionShips(fleet, primaryShip, expeditionsNumber, researches.HyperspaceTechnology, LFBonuses.Expeditions.Resources, LFBonuses.Ships, serverdata, playerClass, probeCargo);
		}

		public bool MayAddShipToExpedition(Ships fleet, Buildables buildable, int expeditionsNumber) {
			foreach (PropertyInfo prop in fleet.GetType().GetProperties()) {
				if (prop.Name == buildable.ToString()) {
					long availableVal = (long) prop.GetValue(fleet);
					if (availableVal >= expeditionsNumber)
						return true;
				}
			}
			return false;
		}

		public Ships CalcFullExpeditionShips(Ships fleet, Buildables primaryShip, int expeditionsNumber, ServerData serverdata, Researches researches, LFBonuses LFBonuses, CharacterClass playerClass = CharacterClass.NoClass, int probeCargo = 0) {
			Ships oneExpeditionFleet = CalcExpeditionShips(fleet, primaryShip, expeditionsNumber, serverdata, researches, LFBonuses, playerClass, probeCargo);

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

		public int CalcDistance(Coordinate origin, Coordinate destination, int galaxiesNumber, int systemsNumber = 499, bool donutGalaxy = true, bool donutSystem = true) {
			if (origin.Galaxy != destination.Galaxy)
				return CalcGalaxyDistance(origin, destination, galaxiesNumber, donutGalaxy);

			if (origin.System != destination.System)
				return CalcSystemDistance(origin, destination, systemsNumber, donutSystem);

			if (origin.Position != destination.Position)
				return CalcPlanetDistance(origin, destination);

			return 5;
		}

		public int CalcDistance(Coordinate origin, Coordinate destination, ServerData serverData) {
			return CalcDistance(origin, destination, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem);
		}

		private int CalcGalaxyDistance(Coordinate origin, Coordinate destination, int galaxiesNumber, bool donutGalaxy = true) {
			if (!donutGalaxy)
				return 20000 * Math.Abs(origin.Galaxy - destination.Galaxy);

			if (origin.Galaxy > destination.Galaxy)
				return 20000 * Math.Min((origin.Galaxy - destination.Galaxy), ((destination.Galaxy + galaxiesNumber) - origin.Galaxy));

			return 20000 * Math.Min((destination.Galaxy - origin.Galaxy), ((origin.Galaxy + galaxiesNumber) - destination.Galaxy));
		}

		private int CalcSystemDistance(Coordinate origin, Coordinate destination, int systemsNumber, bool donutSystem = true) {
			if (!donutSystem)
				return 2700 + 95 * Math.Abs(origin.System - destination.System);

			if (origin.System > destination.System)
				return 2700 + 95 * Math.Min((origin.System - destination.System), ((destination.System + systemsNumber) - origin.System));

			return 2700 + 95 * Math.Min((destination.System - origin.System), ((origin.System + systemsNumber) - destination.System));

		}

		private int CalcPlanetDistance(Coordinate origin, Coordinate destination) {
			return 1000 + 5 * Math.Abs(destination.Position - origin.Position);
		}

		public long CalcEnergyProduction(Buildables buildable, int level, int energyTechnology = 0, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
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

		public long CalcEnergyProduction(Buildables buildable, int level, Researches researches, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
			return CalcEnergyProduction(buildable, level, researches.EnergyTechnology, ratio, playerClass, hasEngineer, hasStaff);
		}

		public long CalcMetalProduction(int level, int position, int speedFactor, float ratio = 1, int plasma = 0, float metalLFBonus = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
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
			int bonusProd = 0;
			if (metalLFBonus > 0) {
				bonusProd = (int) Math.Round(prod * metalLFBonus / 100);
			}
			return (long) Math.Round(((prod + plasmaProd + geologistProd + staffProd + classProd + bonusProd) * ratio + crawlerProd * crawlerRatio), 0);
		}

		public long CalcMetalProduction(Buildings buildings, int position, int speedFactor, float ratio = 1, Researches researches = null, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			if (lfBonuses == null)
				lfBonuses = new() { Production = new() { Metal = 0 } };
			return CalcMetalProduction(buildings.MetalMine, position, speedFactor, ratio, researches.PlasmaTechnology, lfBonuses.Production.Metal, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public long CalcMetalProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			if (planet.LFBonuses == null)
				planet.LFBonuses = new() { Production = new() { Metal = 0 } };
			return CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public long CalcCrystalProduction(int level, int position, int speedFactor, float ratio = 1, int plasma = 0, float crystalLFBonus = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
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
			int bonusProd = 0;
			if (crystalLFBonus > 0) {
				bonusProd = (int) Math.Round(prod * crystalLFBonus / 100);
			}
			return (long) Math.Round(((prod + plasmaProd + geologistProd + staffProd + classProd + bonusProd) * ratio + crawlerProd * crawlerRatio), 0);
		}

		public long CalcCrystalProduction(Buildings buildings, int position, int speedFactor, float ratio = 1, Researches researches = null, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			if (lfBonuses == null)
				lfBonuses = new() { Production = new() { Crystal = 0 } };
			return CalcCrystalProduction(buildings.CrystalMine, position, speedFactor, ratio, researches.PlasmaTechnology, lfBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public long CalcCrystalProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			if (planet.LFBonuses == null)
				planet.LFBonuses = new() { Production = new() { Crystal = 0 } };
			return CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public long CalcDeuteriumProduction(int level, float temp, int speedFactor, float ratio = 1, int plasma = 0, float deuteriumLFBonus = 0, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
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
			int bonusProd = 0;
			if (deuteriumLFBonus > 0) {
				bonusProd = (int) Math.Round(prod * deuteriumLFBonus / 100);
			}
			return (long) Math.Round(((prod + plasmaProd + geologistProd + staffProd + classProd + deuteriumLFBonus) * ratio + crawlerProd * crawlerRatio), 0);
		}

		public long CalcDeuteriumProduction(Buildings buildings, Temperature temp, int speedFactor, float ratio = 1, Researches researches = null, LFBonuses lfBonuses = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			if (lfBonuses == null)
				lfBonuses = new() { Production = new() { Deuterium = 0 } };
			return CalcDeuteriumProduction(buildings.CrystalMine, temp.Average, speedFactor, ratio, researches.PlasmaTechnology, lfBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public long CalcDeuteriumProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			if (researches == null)
				researches = new Researches() { PlasmaTechnology = 0 };
			if (planet.LFBonuses == null)
				planet.LFBonuses = new() { Production = new() { Deuterium = 0 } };
			return CalcDeuteriumProduction(planet.Buildings.CrystalMine, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio);
		}

		public Resources CalcPlanetHourlyProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, int crawlers = 0, float crawlerRatio = 1) {
			Resources hourlyProduction = new() {
				Metal = CalcMetalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio),
				Crystal = CalcCrystalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio),
				Deuterium = CalcDeuteriumProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff, crawlers, crawlerRatio)
			};
			return hourlyProduction;
		}

		public Resources CalcPrice(Buildables buildable, int level, LFBonuses lfBonuses = null) {
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

			if (lfBonuses != null) {
				float reduction = 0; 
				if (lfBonuses.Buildings.Keys.Any(b => b == (int) buildable)) {
					reduction = lfBonuses.Buildings[(int) buildable].Cost;
				}
				else if (lfBonuses.Researches.Keys.Any(b => b == (int) buildable)) {
					reduction = lfBonuses.Researches[(int) buildable].Cost;
				}
				output.Metal = (long) (output.Metal - (output.Metal * reduction));
				output.Crystal = (long) (output.Crystal - (output.Crystal * reduction));
				output.Deuterium = (long) (output.Deuterium - (output.Deuterium * reduction));
			}

			return output;
		}

		public Resources CalcPrice(LFBuildables buildable, int level, double costReduction = 0, double energyCostReduction = 0, double populationCostReduction = 0) {
			long metalBaseCost = 0;
			long crystalbaseCost = 0;
			long deutBaseCost = 0;
			long energyBaseCost = 0;
			long populationBaseCost = 0;
			double metalFactor = 0;
			double crystalFactor = 0;
			double deutFactor = 0;
			double energyFactor = 0;
			double populationFactor = 0;

			switch (buildable) {
				case LFBuildables.ResidentialSector:
					metalBaseCost = 7;
					crystalbaseCost = 2;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					break;
				case LFBuildables.BiosphereFarm:
					metalBaseCost = 5;
					crystalbaseCost = 2;
					energyBaseCost = 8;
					metalFactor = 1.23;
					crystalFactor = 1.23;
					energyFactor = 1.02;
					break;
				case LFBuildables.ResearchCentre:
					metalBaseCost = 20000;
					crystalbaseCost = 25000;
					deutBaseCost = 10000;
					energyBaseCost = 10;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					energyFactor = 1.08;
					break;
				case LFBuildables.AcademyOfSciences:
					metalBaseCost = 5000;
					crystalbaseCost = 3200;
					deutBaseCost = 1500;
					energyBaseCost = 15;
					populationBaseCost = 20000000;
					metalFactor = 1.7;
					crystalFactor = 1.7;
					deutFactor = 1.7;
					energyFactor = 1.25;
					populationFactor = 1.1;
					break;
				case LFBuildables.NeuroCalibrationCentre:
					metalBaseCost = 50000;
					crystalbaseCost = 40000;
					deutBaseCost = 50000;
					energyBaseCost = 30;
					populationBaseCost = 100000000;
					metalFactor = 1.7;
					crystalFactor = 1.7;
					deutFactor = 1.7;
					energyFactor = 1.25;
					populationFactor = 1.1;
					break;
				case LFBuildables.HighEnergySmelting:
					metalBaseCost = 9000;
					crystalbaseCost = 6000;
					deutBaseCost = 3000;
					energyBaseCost = 40;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.1;
					break;
				case LFBuildables.FoodSilo:
					metalBaseCost = 25000;
					crystalbaseCost = 13000;
					deutBaseCost = 7000;
					metalFactor = 1.09;
					crystalFactor = 1.09;
					deutFactor = 1.09;
					break;
				case LFBuildables.FusionPoweredProduction:
					metalBaseCost = 50000;
					crystalbaseCost = 25000;
					deutBaseCost = 15000;
					energyBaseCost = 80;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.1;
					break;
				case LFBuildables.Skyscraper:
					metalBaseCost = 75000;
					crystalbaseCost = 20000;
					deutBaseCost = 25000;
					energyBaseCost = 50;
					metalFactor = 1.09;
					crystalFactor = 1.09;
					deutFactor = 1.09;
					energyFactor = 1.02;
					break;
				case LFBuildables.BiotechLab:
					metalBaseCost = 150000;
					crystalbaseCost = 30000;
					deutBaseCost = 15000;
					energyBaseCost = 60;
					metalFactor = 1.12;
					crystalFactor = 1.12;
					deutFactor = 1.12;
					energyFactor = 1.03;
					break;
				case LFBuildables.Metropolis:
					metalBaseCost = 80000;
					crystalbaseCost = 35000;
					deutBaseCost = 60000;
					energyBaseCost = 90;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.05;
					break;
				case LFBuildables.PlanetaryShield:
					metalBaseCost = 250000;
					crystalbaseCost = 125000;
					deutBaseCost = 125000;
					energyBaseCost = 100;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					energyFactor = 1.02;
					break;
				case LFBuildables.MeditationEnclave:
					metalBaseCost = 9;
					crystalbaseCost = 3;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					break;
				case LFBuildables.CrystalFarm:
					metalBaseCost = 7;
					crystalbaseCost = 2;
					energyBaseCost = 10;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					energyFactor = 1.03;
					break;
				case LFBuildables.RuneTechnologium:
					metalBaseCost = 40000;
					crystalbaseCost = 10000;
					deutBaseCost = 15000;
					energyBaseCost = 15;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					energyFactor = 1.1;
					break;
				case LFBuildables.RuneForge:
					metalBaseCost = 5000;
					crystalbaseCost = 3800;
					deutBaseCost = 1000;
					energyBaseCost = 20;
					populationBaseCost = 16000000;
					metalFactor = 1.7;
					crystalFactor = 1.7;
					deutFactor = 1.7;
					energyFactor = 1.35;
					populationFactor = 1.14;
					break;
				case LFBuildables.Oriktorium:
					metalBaseCost = 50000;
					crystalbaseCost = 40000;
					deutBaseCost = 50000;
					energyBaseCost = 60;
					populationBaseCost = 90000000;
					metalFactor = 1.65;
					crystalFactor = 1.65;
					deutFactor = 1.65;
					energyFactor = 1.3;
					populationFactor = 1.1;
					break;
				case LFBuildables.MagmaForge:
					metalBaseCost = 10000;
					crystalbaseCost = 8000;
					deutBaseCost = 1000;
					energyBaseCost = 40;
					metalFactor = 1.4;
					crystalFactor = 1.4;
					deutFactor = 1.4;
					energyFactor = 1.1;
					break;
				case LFBuildables.DisruptionChamber:
					metalBaseCost = 20000;
					crystalbaseCost = 15000;
					deutBaseCost = 10000;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					break;
				case LFBuildables.Megalith:
					metalBaseCost = 50000;
					crystalbaseCost = 35000;
					deutBaseCost = 15000;
					energyBaseCost = 80;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.3;
					break;
				case LFBuildables.CrystalRefinery:
					metalBaseCost = 85000;
					crystalbaseCost = 44000;
					deutBaseCost = 25000;
					energyBaseCost = 90;
					metalFactor = 1.4;
					crystalFactor = 1.4;
					deutFactor = 1.4;
					energyFactor = 1.1;
					break;
				case LFBuildables.DeuteriumSynthesiser:
					metalBaseCost = 120000;
					crystalbaseCost = 50000;
					deutBaseCost = 20000;
					energyBaseCost = 90;
					metalFactor = 1.4;
					crystalFactor = 1.4;
					deutFactor = 1.4;
					energyFactor = 1.1;
					break;
				case LFBuildables.MineralResearchCentre:
					metalBaseCost = 250000;
					crystalbaseCost = 150000;
					deutBaseCost = 100000;
					energyBaseCost = 120;
					metalFactor = 1.8;
					crystalFactor = 1.8;
					deutFactor = 1.8;
					energyFactor = 1.3;
					break;
				case LFBuildables.AdvancedRecyclingPlant:
					metalBaseCost = 250000;
					crystalbaseCost = 125000;
					deutBaseCost = 125000;
					energyBaseCost = 100;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.1;
					break;
				case LFBuildables.AssemblyLine:
					metalBaseCost = 6;
					crystalbaseCost = 2;
					metalFactor = 1.21;
					crystalFactor = 1.21;
					break;
				case LFBuildables.FusionCellFactory:
					metalBaseCost = 5;
					crystalbaseCost = 2;
					energyBaseCost = 8;
					metalFactor = 1.18;
					crystalFactor = 1.18;
					energyFactor = 1.02;
					break;
				case LFBuildables.RoboticsResearchCentre:
					metalBaseCost = 30000;
					crystalbaseCost = 20000;
					deutBaseCost = 10000;
					energyBaseCost = 13;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					energyFactor = 1.08;
					break;
				case LFBuildables.UpdateNetwork:
					metalBaseCost = 5000;
					crystalbaseCost = 3800;
					deutBaseCost = 1000;
					energyBaseCost = 10;
					populationBaseCost = 40000000;
					metalFactor = 1.8;
					crystalFactor = 1.8;
					deutFactor = 1.8;
					energyFactor = 1.2;
					populationFactor = 1.1;
					break;
				case LFBuildables.QuantumComputerCentre:
					metalBaseCost = 50000;
					crystalbaseCost = 40000;
					deutBaseCost = 50000;
					energyBaseCost = 40;
					populationBaseCost = 130000000;
					metalFactor = 1.8;
					crystalFactor = 1.8;
					deutFactor = 1.8;
					energyFactor = 1.2;
					populationFactor = 1.1;
					break;
				case LFBuildables.AutomatisedAssemblyCentre:
					metalBaseCost = 7500;
					crystalbaseCost = 7000;
					deutBaseCost = 1000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFBuildables.HighPerformanceTransformer:
					metalBaseCost = 35000;
					crystalbaseCost = 15000;
					deutBaseCost = 10000;
					energyBaseCost = 40;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.05;
					break;
				case LFBuildables.MicrochipAssemblyLine:
					metalBaseCost = 50000;
					crystalbaseCost = 20000;
					deutBaseCost = 30000;
					energyBaseCost = 40;
					metalFactor = 1.07;
					crystalFactor = 1.07;
					deutFactor = 1.07;
					energyFactor = 1.01;
					break;
				case LFBuildables.ProductionAssemblyHall:
					metalBaseCost = 100000;
					crystalbaseCost = 10000;
					deutBaseCost = 3000;
					energyBaseCost = 80;
					metalFactor = 1.14;
					crystalFactor = 1.14;
					deutFactor = 1.14;
					energyFactor = 1.04;
					break;
				case LFBuildables.HighPerformanceSynthesiser:
					metalBaseCost = 100000;
					crystalbaseCost = 40000;
					deutBaseCost = 20000;
					energyBaseCost = 60;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.1;
					break;
				case LFBuildables.ChipMassProduction:
					metalBaseCost = 55000;
					crystalbaseCost = 50000;
					deutBaseCost = 30000;
					energyBaseCost = 70;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.05;
					break;
				case LFBuildables.NanoRepairBots:
					metalBaseCost = 250000;
					crystalbaseCost = 125000;
					deutBaseCost = 125000;
					energyBaseCost = 100;
					metalFactor = 1.4;
					crystalFactor = 1.4;
					deutFactor = 1.4;
					energyFactor = 1.05;
					break;
				case LFBuildables.Sanctuary:
					metalBaseCost = 4;
					crystalbaseCost = 3;
					metalFactor = 1.21;
					crystalFactor = 1.21;
					break;
				case LFBuildables.AntimatterCondenser:
					metalBaseCost = 6;
					crystalbaseCost = 3;
					energyBaseCost = 9;
					metalFactor = 1.21;
					crystalFactor = 1.21;
					energyFactor = 1.02;
					break;
				case LFBuildables.VortexChamber:
					metalBaseCost = 20000;
					crystalbaseCost = 20000;
					deutBaseCost = 30000;
					energyBaseCost = 10;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					energyFactor = 1.08;
					break;
				case LFBuildables.HallsOfRealisation:
					metalBaseCost = 7500;
					crystalbaseCost = 5000;
					deutBaseCost = 800;
					energyBaseCost = 15;
					populationBaseCost = 30000000;
					metalFactor = 1.8;
					crystalFactor = 1.8;
					deutFactor = 1.8;
					energyFactor = 1.3;
					populationFactor = 1.1;
					break;
				case LFBuildables.ForumOfTranscendence:
					metalBaseCost = 60000;
					crystalbaseCost = 30000;
					deutBaseCost = 50000;
					energyBaseCost = 30;
					populationBaseCost = 100000000;
					metalFactor = 1.8;
					crystalFactor = 1.8;
					deutFactor = 1.8;
					energyFactor = 1.3;
					populationFactor = 1.1;
					break;
				case LFBuildables.AntimatterConvector:
					metalBaseCost = 8500;
					crystalbaseCost = 5000;
					deutBaseCost = 3000;
					metalFactor = 1.25;
					crystalFactor = 1.25;
					deutFactor = 1.25;
					break;
				case LFBuildables.CloningLaboratory:
					metalBaseCost = 15000;
					crystalbaseCost = 15000;
					deutBaseCost = 20000;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					break;
				case LFBuildables.ChrysalisAccelerator:
					metalBaseCost = 75000;
					crystalbaseCost = 25000;
					deutBaseCost = 30000;
					energyBaseCost = 30;
					metalFactor = 1.05;
					crystalFactor = 1.05;
					deutFactor = 1.05;
					energyFactor = 1.03;
					break;
				case LFBuildables.BioModifier:
					metalBaseCost = 87500;
					crystalbaseCost = 25000;
					deutBaseCost = 30000;
					energyBaseCost = 40;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					energyFactor = 1.02;
					break;
				case LFBuildables.PsionicModulator:
					metalBaseCost = 150000;
					crystalbaseCost = 30000;
					deutBaseCost = 30000;
					energyBaseCost = 140;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					energyFactor = 1.05;
					break;
				case LFBuildables.ShipManufacturingHall:
					metalBaseCost = 75000;
					crystalbaseCost = 50000;
					deutBaseCost = 55000;
					energyBaseCost = 90;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					energyFactor = 1.04;
					break;
				case LFBuildables.SupraRefractor:
					metalBaseCost = 500000;
					crystalbaseCost = 250000;
					deutBaseCost = 250000;
					energyBaseCost = 100;
					metalFactor = 1.4;
					crystalFactor = 1.4;
					deutFactor = 1.4;
					energyFactor = 1.05;
					break;
				default:
					break;
			}

			return CalcLFPrice(level, metalBaseCost, metalFactor, crystalbaseCost, crystalFactor, deutBaseCost, deutFactor, energyBaseCost, energyFactor, populationBaseCost, populationFactor, costReduction, energyCostReduction, populationCostReduction);
		}

		public Resources CalcPrice(LFTechno buildable, int level, double costReduction = 0) {
			long metalBaseCost = 0;
			long crystalbaseCost = 0;
			long deutBaseCost = 0;
			double metalFactor = 0;
			double crystalFactor = 0;
			double deutFactor = 0;

			switch (buildable) {
				case LFTechno.IntergalacticEnvoys:
					metalBaseCost = 5000;
					crystalbaseCost = 2500;
					deutBaseCost = 500;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.HighPerformanceExtractors:
					metalBaseCost = 7000;
					crystalbaseCost = 10000;
					deutBaseCost = 5000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.FusionDrives:
					metalBaseCost = 15000;
					crystalbaseCost = 10000;
					deutBaseCost = 5000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.StealthFieldGenerator:
					metalBaseCost = 20000;
					crystalbaseCost = 15000;
					deutBaseCost = 7500;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.OrbitalDen:
					metalBaseCost = 25000;
					crystalbaseCost = 20000;
					deutBaseCost = 10000;
					metalFactor = 1.4;
					crystalFactor = 1.4;
					deutFactor = 1.4;
					break;
				case LFTechno.ResearchAI:
					metalBaseCost = 35000;
					crystalbaseCost = 25000;
					deutBaseCost = 15000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.HighPerformanceTerraformer:
					metalBaseCost = 70000;
					crystalbaseCost = 40000;
					deutBaseCost = 20000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.EnhancedProductionTechnologies:
					metalBaseCost = 80000;
					crystalbaseCost = 50000;
					deutBaseCost = 20000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.LightFighterMkII:
					metalBaseCost = 320000;
					crystalbaseCost = 240000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.CruiserMkII:
					metalBaseCost = 320000;
					crystalbaseCost = 240000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.ImprovedLabTechnology:
					metalBaseCost = 120000;
					crystalbaseCost = 30000;
					deutBaseCost = 25000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.PlasmaTerraformer:
					metalBaseCost = 100000;
					crystalbaseCost = 40000;
					deutBaseCost = 30000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.LowTemperatureDrives:
					metalBaseCost = 200000;
					crystalbaseCost = 100000;
					deutBaseCost = 100000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.BomberMkII:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.DestroyerMkII:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.BattlecruiserMkII:
					metalBaseCost = 320000;
					crystalbaseCost = 240000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.RobotAssistants:
					metalBaseCost = 300000;
					crystalbaseCost = 180000;
					deutBaseCost = 120000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.Supercomputer:
					metalBaseCost = 500000;
					crystalbaseCost = 300000;
					deutBaseCost = 200000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.VolcanicBatteries:
					metalBaseCost = 10000;
					crystalbaseCost = 6000;
					deutBaseCost = 1000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.AcousticScanning:
					metalBaseCost = 7500;
					crystalbaseCost = 12500;
					deutBaseCost = 5000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.HighEnergyPumpSystems:
					metalBaseCost = 15000;
					crystalbaseCost = 10000;
					deutBaseCost = 5000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.CargoHoldExpansionCivilianShips:
					metalBaseCost = 20000;
					crystalbaseCost = 15000;
					deutBaseCost = 7500;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.MagmaPoweredProduction:
					metalBaseCost = 25000;
					crystalbaseCost = 20000;
					deutBaseCost = 10000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.GeothermalPowerPlants:
					metalBaseCost = 50000;
					crystalbaseCost = 50000;
					deutBaseCost = 20000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.DepthSounding:
					metalBaseCost = 70000;
					crystalbaseCost = 40000;
					deutBaseCost = 20000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.IonCrystalEnhancementHeavyFighter:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.ImprovedStellarator:
					metalBaseCost = 75000;
					crystalbaseCost = 55000;
					deutBaseCost = 25000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.HardenedDiamondDrillHeads:
					metalBaseCost = 85000;
					crystalbaseCost = 40000;
					deutBaseCost = 35000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.SeismicMiningTechnology:
					metalBaseCost = 120000;
					crystalbaseCost = 30000;
					deutBaseCost = 25000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.MagmaPoweredPumpSystems:
					metalBaseCost = 100000;
					crystalbaseCost = 40000;
					deutBaseCost = 30000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.IonCrystalModules:
					metalBaseCost = 200000;
					crystalbaseCost = 100000;
					deutBaseCost = 100000;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					break;
				case LFTechno.OptimisedSiloConstructionMethod:
					metalBaseCost = 220000;
					crystalbaseCost = 110000;
					deutBaseCost = 110000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.DiamondEnergyTransmitter:
					metalBaseCost = 240000;
					crystalbaseCost = 120000;
					deutBaseCost = 120000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.ObsidianShieldReinforcement:
					metalBaseCost = 250000;
					crystalbaseCost = 250000;
					deutBaseCost = 250000;
					metalFactor = 1.4;
					crystalFactor = 1.4;
					deutFactor = 1.4;
					break;
				case LFTechno.RuneShields:
					metalBaseCost = 500000;
					crystalbaseCost = 300000;
					deutBaseCost = 200000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.RocktalCollectorEnhancement:
					metalBaseCost = 300000;
					crystalbaseCost = 180000;
					deutBaseCost = 120000;
					metalFactor = 1.7;
					crystalFactor = 1.7;
					deutFactor = 1.7;
					break;
				case LFTechno.CatalyserTechnology:
					metalBaseCost = 10000;
					crystalbaseCost = 6000;
					deutBaseCost = 1000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.PlasmaDrive:
					metalBaseCost = 7500;
					crystalbaseCost = 12500;
					deutBaseCost = 5000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.EfficiencyModule:
					metalBaseCost = 15000;
					crystalbaseCost = 10000;
					deutBaseCost = 5000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.DepotAI:
					metalBaseCost = 20000;
					crystalbaseCost = 15000;
					deutBaseCost = 7500;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.GeneralOverhaulLightFighter:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.AutomatedTransportLines:
					metalBaseCost = 50000;
					crystalbaseCost = 50000;
					deutBaseCost = 20000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.ImprovedDroneAI:
					metalBaseCost = 70000;
					crystalbaseCost = 40000;
					deutBaseCost = 20000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.ExperimentalRecyclingTechnology:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.GeneralOverhaulCruiser:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.SlingshotAutopilot:
					metalBaseCost = 85000;
					crystalbaseCost = 40000;
					deutBaseCost = 35000;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					break;
				case LFTechno.HighTemperatureSuperconductors:
					metalBaseCost = 120000;
					crystalbaseCost = 30000;
					deutBaseCost = 25000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.GeneralOverhaulBattleship:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.ArtificialSwarmIntelligence:
					metalBaseCost = 200000;
					crystalbaseCost = 100000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.GeneralOverhaulBattlecruiser:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.GeneralOverhaulBomber:
					metalBaseCost = 320000;
					crystalbaseCost = 240000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.GeneralOverhaulDestroyer:
					metalBaseCost = 320000;
					crystalbaseCost = 240000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.ExperimentalWeaponsTechnology:
					metalBaseCost = 500000;
					crystalbaseCost = 300000;
					deutBaseCost = 200000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.MechanGeneralEnhancement:
					metalBaseCost = 300000;
					crystalbaseCost = 180000;
					deutBaseCost = 120000;
					metalFactor = 1.7;
					crystalFactor = 1.7;
					deutFactor = 1.7;
					break;
				case LFTechno.HeatRecovery:
					metalBaseCost = 10000;
					crystalbaseCost = 6000;
					deutBaseCost = 1000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.SulphideProcess:
					metalBaseCost = 7500;
					crystalbaseCost = 12500;
					deutBaseCost = 5000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.PsionicNetwork:
					metalBaseCost = 15000;
					crystalbaseCost = 10000;
					deutBaseCost = 5000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.TelekineticTractorBeam:
					metalBaseCost = 20000;
					crystalbaseCost = 15000;
					deutBaseCost = 7500;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.EnhancedSensorTechnology:
					metalBaseCost = 25000;
					crystalbaseCost = 20000;
					deutBaseCost = 10000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.NeuromodalCompressor:
					metalBaseCost = 50000;
					crystalbaseCost = 50000;
					deutBaseCost = 20000;
					metalFactor = 1.3;
					crystalFactor = 1.3;
					deutFactor = 1.3;
					break;
				case LFTechno.NeuroInterface:
					metalBaseCost = 70000;
					crystalbaseCost = 40000;
					deutBaseCost = 20000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.InterplanetaryAnalysisNetwork:
					metalBaseCost = 80000;
					crystalbaseCost = 50000;
					deutBaseCost = 20000;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					break;
				case LFTechno.OverclockingHeavyFighter:
					metalBaseCost = 320000;
					crystalbaseCost = 240000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.TelekineticDrive:
					metalBaseCost = 85000;
					crystalbaseCost = 40000;
					deutBaseCost = 35000;
					metalFactor = 1.2;
					crystalFactor = 1.2;
					deutFactor = 1.2;
					break;
				case LFTechno.SixthSense:
					metalBaseCost = 120000;
					crystalbaseCost = 30000;
					deutBaseCost = 25000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.Psychoharmoniser:
					metalBaseCost = 100000;
					crystalbaseCost = 40000;
					deutBaseCost = 30000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.EfficientSwarmIntelligence:
					metalBaseCost = 200000;
					crystalbaseCost = 100000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.OverclockingLargeCargo:
					metalBaseCost = 160000;
					crystalbaseCost = 120000;
					deutBaseCost = 50000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.GravitationSensors:
					metalBaseCost = 240000;
					crystalbaseCost = 120000;
					deutBaseCost = 120000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.OverclockingBattleship:
					metalBaseCost = 320000;
					crystalbaseCost = 240000;
					deutBaseCost = 100000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.PsionicShieldMatrix:
					metalBaseCost = 500000;
					crystalbaseCost = 300000;
					deutBaseCost = 200000;
					metalFactor = 1.5;
					crystalFactor = 1.5;
					deutFactor = 1.5;
					break;
				case LFTechno.KaeleshDiscovererEnhancement:
					metalBaseCost = 300000;
					crystalbaseCost = 180000;
					deutBaseCost = 120000;
					metalFactor = 1.7;
					crystalFactor = 1.7;
					deutFactor = 1.7;
					break;
				default:
					break;
			}

			return CalcLFPrice(level, metalBaseCost, metalFactor, crystalbaseCost, crystalFactor, deutBaseCost, deutFactor, 0, 0, 0, 0, costReduction);
		}

		private long CalcLFPrice(long baseCost, double factor, int level, double costReduction = 0) {
			return (long) Math.Ceiling(((double) 1 - (costReduction/100)) * ((double) baseCost * (double) level * Math.Pow(factor, level - 1)));
		}

		private long CalcLFEnergyPrice(long baseCost, double factor, int level, double costReduction = 0) {
			return (long) Math.Round(((double) 1 - (costReduction / 100)) * ((double) baseCost * (double) level * Math.Pow(factor, level)));
		}

		private long CalcLFPopulationPrice(long baseCost, double factor, int level, double costReduction = 0) {
			return (long)  Math.Ceiling(((double) 1 - (costReduction / 100)) * ((double) baseCost * Math.Pow(factor, level - 1)));
		}

		private Resources CalcLFPrice(int level, long metalBaseCost, double metalFactor, long crystalBaseCost, double crystalFactor, long deutBaseCost = 0, double deutFactor = 0, long energyBaseCost = 0, double energyFactor = 0, long populationBaseCost = 0, double populationFactor = 0, double costReduction = 0, double energyCostReduction = 0, double populationCostReduction = 0) {
			var output = new Resources() {
				Metal = CalcLFPrice(metalBaseCost, metalFactor, level, costReduction),
				Crystal = CalcLFPrice(crystalBaseCost, crystalFactor, level, costReduction),
				Deuterium = CalcLFPrice(deutBaseCost, deutFactor, level, costReduction),
				Energy = CalcLFEnergyPrice(energyBaseCost, energyFactor, level, energyCostReduction),
				Population = CalcLFPopulationPrice(populationBaseCost, populationFactor, level, populationCostReduction)
			};
			return output;
		}

		public int CalcCumulativeLabLevel(List<Celestial> celestials, Researches researches) {
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

		public long CalcProductionTime(Buildables buildable, int level, ServerData serverData, Facilities facilities, int cumulativeLabLevel = 0) {
			return CalcProductionTime(buildable, level, serverData.Speed, facilities, cumulativeLabLevel);
		}

		public long CalcProductionTime(Buildables buildable, int level, int speed = 1, Facilities facilities = null, int cumulativeLabLevel = 0, bool isDiscoverer = false, bool hasTechnocrat = false) {
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

		public long CalcProductionTime(LFBuildables buildable, int level, int speed = 1, Celestial celestial = null) {
			int baseTime = 0;
			double increaseFactor = 0;
			double speedReduction = 0;

			if (celestial == null)
				celestial = new() {
					LFtype = LFTypes.None,
					LFBuildings = new(),
					LFBonuses = new(),
					Facilities = new(),
				};

			switch (celestial.LFtype) {
				case LFTypes.Rocktal:
					speedReduction = celestial.LFBuildings.Megalith;
					break;
				default:
					break;
			}

			switch (buildable) {
				case LFBuildables.ResidentialSector:
					increaseFactor = 1.21;
					baseTime = 40;
					break;
				case LFBuildables.BiosphereFarm:
					increaseFactor = 1.25;
					baseTime = 40;
					break;
				case LFBuildables.ResearchCentre:
					increaseFactor = 1.25;
					baseTime = 16000;
					break;
				case LFBuildables.AcademyOfSciences:
					increaseFactor = 1.60;
					baseTime = 16000;
					break;
				case LFBuildables.NeuroCalibrationCentre:
					increaseFactor = 1.70;
					baseTime = 64000;
					break;
				case LFBuildables.HighEnergySmelting:
					increaseFactor = 1.30;
					baseTime = 2000;
					break;
				case LFBuildables.FoodSilo:
					increaseFactor = 1.17;
					baseTime = 12000;
					break;
				case LFBuildables.FusionPoweredProduction:
					increaseFactor = 1.20;
					baseTime = 28000;
					break;
				case LFBuildables.Skyscraper:
					increaseFactor = 1.20;
					baseTime = 40000;
					break;
				case LFBuildables.BiotechLab:
					increaseFactor = 1.20;
					baseTime = 52000;
					break;
				case LFBuildables.Metropolis:
					increaseFactor = 1.30;
					baseTime = 90000;
					break;
				case LFBuildables.PlanetaryShield:
					increaseFactor = 1.20;
					baseTime = 95000;
					break;
				case LFBuildables.MeditationEnclave:
					increaseFactor = 1.21;
					baseTime = 40;
					break;
				case LFBuildables.CrystalFarm:
					increaseFactor = 1.21;
					baseTime = 40;
					break;
				case LFBuildables.RuneTechnologium:
					increaseFactor = 1.25;
					baseTime = 16000;
					break;
				case LFBuildables.RuneForge:
					increaseFactor = 1.60;
					baseTime = 16000;
					break;
				case LFBuildables.Oriktorium:
					increaseFactor = 1.70;
					baseTime = 64000;
					break;
				case LFBuildables.MagmaForge:
					increaseFactor = 1.30;
					baseTime = 2000;
					break;
				case LFBuildables.DisruptionChamber:
					increaseFactor = 1.25;
					baseTime = 16000;
					break;
				case LFBuildables.Megalith:
					increaseFactor = 1.40;
					baseTime = 40000;
					break;
				case LFBuildables.CrystalRefinery:
					increaseFactor = 1.20;
					baseTime = 40000;
					break;
				case LFBuildables.DeuteriumSynthesiser:
					increaseFactor = 1.20;
					baseTime = 52000;
					break;
				case LFBuildables.MineralResearchCentre:
					increaseFactor = 1.30;
					baseTime = 90000;
					break;
				case LFBuildables.AdvancedRecyclingPlant:
					increaseFactor = 1.30;
					baseTime = 95000;
					break;
				case LFBuildables.AssemblyLine:
					increaseFactor = 1.22;
					baseTime = 40;
					break;
				case LFBuildables.FusionCellFactory:
					increaseFactor = 1.20;
					baseTime = 48;
					break;
				case LFBuildables.RoboticsResearchCentre:
					increaseFactor = 1.25;
					baseTime = 16000;
					break;
				case LFBuildables.UpdateNetwork:
					increaseFactor = 1.60;
					baseTime = 16000;
					break;
				case LFBuildables.QuantumComputerCentre:
					increaseFactor = 1.70;
					baseTime = 64000;
					break;
				case LFBuildables.AutomatisedAssemblyCentre:
					increaseFactor = 1.30;
					baseTime = 2000;
					break;
				case LFBuildables.HighPerformanceTransformer:
					increaseFactor = 1.40;
					baseTime = 16000;
					break;
				case LFBuildables.MicrochipAssemblyLine:
					increaseFactor = 1.17;
					baseTime = 12000;
					break;
				case LFBuildables.ProductionAssemblyHall:
					increaseFactor = 1.30;
					baseTime = 40000;
					break;
				case LFBuildables.HighPerformanceSynthesiser:
					increaseFactor = 1.20;
					baseTime = 52000;
					break;
				case LFBuildables.ChipMassProduction:
					increaseFactor = 1.30;
					baseTime = 50000;
					break;
				case LFBuildables.NanoRepairBots:
					increaseFactor = 1.40;
					baseTime = 95000;
					break;
				case LFBuildables.Sanctuary:
					increaseFactor = 1.22;
					baseTime = 40;
					break;
				case LFBuildables.AntimatterCondenser:
					increaseFactor = 1.22;
					baseTime = 40;
					break;
				case LFBuildables.VortexChamber:
					increaseFactor = 1.25;
					baseTime = 16000;
					break;
				case LFBuildables.HallsOfRealisation:
					increaseFactor = 1.70;
					baseTime = 16000;
					break;
				case LFBuildables.ForumOfTranscendence:
					increaseFactor = 1.80;
					baseTime = 64000;
					break;
				case LFBuildables.AntimatterConvector:
					increaseFactor = 1.35;
					baseTime = 2000;
					break;
				case LFBuildables.CloningLaboratory:
					increaseFactor = 1.20;
					baseTime = 12000;
					break;
				case LFBuildables.ChrysalisAccelerator:
					increaseFactor = 1.18;
					baseTime = 16000;
					break;
				case LFBuildables.BioModifier:
					increaseFactor = 1.20;
					baseTime = 40000;
					break;
				case LFBuildables.PsionicModulator:
					increaseFactor = 1.80;
					baseTime = 52000;
					break;
				case LFBuildables.ShipManufacturingHall:
					increaseFactor = 1.30;
					baseTime = 90000;
					break;
				case LFBuildables.SupraRefractor:
					increaseFactor = 1.30;
					baseTime = 95000;
					break;

				default:
					break;
			}
			return CalcLFTime(level, baseTime, increaseFactor, speed, celestial.Facilities.RoboticsFactory, celestial.Facilities.NaniteFactory, speedReduction);
		}
		public long CalcProductionTime(LFBuildables buildable, int level, ServerData serverData, Celestial celestial) {
			return CalcProductionTime(buildable, level, serverData.Speed, celestial);
		}
		public long CalcProductionTime(LFTechno buildable, int level, int speed = 1, double speedReduction = 0) {
			int baseTime = 0;
			double increaseFactor = 0;

			switch (buildable) {
				case LFTechno.IntergalacticEnvoys:
					increaseFactor = 1.2;
					baseTime = 1000;
					break;
				case LFTechno.HighPerformanceExtractors:
					increaseFactor = 1.3;
					baseTime = 2000;
					break;
				case LFTechno.FusionDrives:
					increaseFactor = 1.3;
					baseTime = 2500;
					break;
				case LFTechno.StealthFieldGenerator:
					increaseFactor = 1.3;
					baseTime = 3500;
					break;
				case LFTechno.OrbitalDen:
					increaseFactor = 1.2;
					baseTime = 4500;
					break;
				case LFTechno.ResearchAI:
					increaseFactor = 1.3;
					baseTime = 5000;
					break;
				case LFTechno.HighPerformanceTerraformer:
					increaseFactor = 1.3;
					baseTime = 8000;
					break;
				case LFTechno.EnhancedProductionTechnologies:
					increaseFactor = 1.3;
					baseTime = 6000;
					break;
				case LFTechno.LightFighterMkII:
					increaseFactor = 1.4;
					baseTime = 6500;
					break;
				case LFTechno.CruiserMkII:
					increaseFactor = 1.4;
					baseTime = 7000;
					break;
				case LFTechno.ImprovedLabTechnology:
					increaseFactor = 1.3;
					baseTime = 7500;
					break;
				case LFTechno.PlasmaTerraformer:
					increaseFactor = 1.3;
					baseTime = 10000;
					break;
				case LFTechno.LowTemperatureDrives:
					increaseFactor = 1.3;
					baseTime = 8500;
					break;
				case LFTechno.BomberMkII:
					increaseFactor = 1.4;
					baseTime = 9000;
					break;
				case LFTechno.DestroyerMkII:
					increaseFactor = 1.4;
					baseTime = 9500;
					break;
				case LFTechno.BattlecruiserMkII:
					increaseFactor = 1.4;
					baseTime = 10000;
					break;
				case LFTechno.RobotAssistants:
					increaseFactor = 1.3;
					baseTime = 11000;
					break;
				case LFTechno.Supercomputer:
					increaseFactor = 1.3;
					baseTime = 13000;
					break;
				case LFTechno.VolcanicBatteries:
					increaseFactor = 1.3;
					baseTime = 1000;
					break;
				case LFTechno.AcousticScanning:
					increaseFactor = 1.3;
					baseTime = 2000;
					break;
				case LFTechno.HighEnergyPumpSystems:
					increaseFactor = 1.3;
					baseTime = 2500;
					break;
				case LFTechno.CargoHoldExpansionCivilianShips:
					increaseFactor = 1.4;
					baseTime = 3500;
					break;
				case LFTechno.MagmaPoweredProduction:
					increaseFactor = 1.3;
					baseTime = 4500;
					break;
				case LFTechno.GeothermalPowerPlants:
					increaseFactor = 1.3;
					baseTime = 5000;
					break;
				case LFTechno.DepthSounding:
					increaseFactor = 1.3;
					baseTime = 5500;
					break;
				case LFTechno.IonCrystalEnhancementHeavyFighter:
					increaseFactor = 1.4;
					baseTime = 6000;
					break;
				case LFTechno.ImprovedStellarator:
					increaseFactor = 1.3;
					baseTime = 6500;
					break;
				case LFTechno.HardenedDiamondDrillHeads:
					increaseFactor = 1.3;
					baseTime = 7000;
					break;
				case LFTechno.SeismicMiningTechnology:
					increaseFactor = 1.3;
					baseTime = 7500;
					break;
				case LFTechno.MagmaPoweredPumpSystems:
					increaseFactor = 1.3;
					baseTime = 8000;
					break;
				case LFTechno.IonCrystalModules:
					increaseFactor = 1.3;
					baseTime = 8500;
					break;
				case LFTechno.OptimisedSiloConstructionMethod:
					increaseFactor = 1.3;
					baseTime = 9000;
					break;
				case LFTechno.DiamondEnergyTransmitter:
					increaseFactor = 1.3;
					baseTime = 9500;
					break;
				case LFTechno.ObsidianShieldReinforcement:
					increaseFactor = 1.4;
					baseTime = 10000;
					break;
				case LFTechno.RuneShields:
					increaseFactor = 1.3;
					baseTime = 13000;
					break;
				case LFTechno.RocktalCollectorEnhancement:
					increaseFactor = 1.4;
					baseTime = 11000;
					break;
				case LFTechno.CatalyserTechnology:
					increaseFactor = 1.3;
					baseTime = 1000;
					break;
				case LFTechno.PlasmaDrive:
					increaseFactor = 1.3;
					baseTime = 2000;
					break;
				case LFTechno.EfficiencyModule:
					increaseFactor = 1.4;
					baseTime = 2500;
					break;
				case LFTechno.DepotAI:
					increaseFactor = 1.3;
					baseTime = 3500;
					break;
				case LFTechno.GeneralOverhaulLightFighter:
					increaseFactor = 1.4;
					baseTime = 4500;
					break;
				case LFTechno.AutomatedTransportLines:
					increaseFactor = 1.3;
					baseTime = 5000;
					break;
				case LFTechno.ImprovedDroneAI:
					increaseFactor = 1.3;
					baseTime = 5500;
					break;
				case LFTechno.ExperimentalRecyclingTechnology:
					increaseFactor = 1.4;
					baseTime = 6000;
					break;
				case LFTechno.GeneralOverhaulCruiser:
					increaseFactor = 1.4;
					baseTime = 6500;
					break;
				case LFTechno.SlingshotAutopilot:
					increaseFactor = 1.3;
					baseTime = 7000;
					break;
				case LFTechno.HighTemperatureSuperconductors:
					increaseFactor = 1.3;
					baseTime = 7500;
					break;
				case LFTechno.GeneralOverhaulBattleship:
					increaseFactor = 1.4;
					baseTime = 8000;
					break;
				case LFTechno.ArtificialSwarmIntelligence:
					increaseFactor = 1.3;
					baseTime = 8500;
					break;
				case LFTechno.GeneralOverhaulBattlecruiser:
					increaseFactor = 1.4;
					baseTime = 9000;
					break;
				case LFTechno.GeneralOverhaulBomber:
					increaseFactor = 1.4;
					baseTime = 9500;
					break;
				case LFTechno.GeneralOverhaulDestroyer:
					increaseFactor = 1.4;
					baseTime = 10000;
					break;
				case LFTechno.ExperimentalWeaponsTechnology:
					increaseFactor = 1.3;
					baseTime = 13000;
					break;
				case LFTechno.MechanGeneralEnhancement:
					increaseFactor = 1.4;
					baseTime = 11000;
					break;
				case LFTechno.HeatRecovery:
					increaseFactor = 1.4;
					baseTime = 1000;
					break;
				case LFTechno.SulphideProcess:
					increaseFactor = 1.3;
					baseTime = 2000;
					break;
				case LFTechno.PsionicNetwork:
					increaseFactor = 1.4;
					baseTime = 2500;
					break;
				case LFTechno.TelekineticTractorBeam:
					increaseFactor = 1.4;
					baseTime = 3500;
					break;
				case LFTechno.EnhancedSensorTechnology:
					increaseFactor = 1.4;
					baseTime = 4500;
					break;
				case LFTechno.NeuromodalCompressor:
					increaseFactor = 1.4;
					baseTime = 5000;
					break;
				case LFTechno.NeuroInterface:
					increaseFactor = 1.3;
					baseTime = 5500;
					break;
				case LFTechno.InterplanetaryAnalysisNetwork:
					increaseFactor = 1.2;
					baseTime = 6000;
					break;
				case LFTechno.OverclockingHeavyFighter:
					increaseFactor = 1.4;
					baseTime = 6500;
					break;
				case LFTechno.TelekineticDrive:
					increaseFactor = 1.2;
					baseTime = 7000;
					break;
				case LFTechno.SixthSense:
					increaseFactor = 1.4;
					baseTime = 7500;
					break;
				case LFTechno.Psychoharmoniser:
					increaseFactor = 1.3;
					baseTime = 8000;
					break;
				case LFTechno.EfficientSwarmIntelligence:
					increaseFactor = 1.3;
					baseTime = 8500;
					break;
				case LFTechno.OverclockingLargeCargo:
					increaseFactor = 1.4;
					baseTime = 9000;
					break;
				case LFTechno.GravitationSensors:
					increaseFactor = 1.4;
					baseTime = 9500;
					break;
				case LFTechno.OverclockingBattleship:
					increaseFactor = 1.4;
					baseTime = 10000;
					break;
				case LFTechno.PsionicShieldMatrix:
					increaseFactor = 1.3;
					baseTime = 13000;
					break;
				case LFTechno.KaeleshDiscovererEnhancement:
					increaseFactor = 1.4;
					baseTime = 11000;
					break;

				default:
					break;
			}
			return CalcLFTime(level, baseTime, increaseFactor, speed, speedReduction);
		}
		public long CalcProductionTime(LFTechno buildable, int level, ServerData serverData, double speedReduction = 0) {
			return CalcProductionTime(buildable, level, serverData.Speed, speedReduction);
		}

		private long CalcLFTime(int level, int baseTime, double increaseFactor, int speed = 1, double speedReduction = 0) {
			long duration = (long) Math.Floor((double) level * (double) baseTime * (double) Math.Pow(increaseFactor, level));
			duration = (long) Math.Floor((double)(1 - 0.01 * speedReduction) * duration);
			duration = (long) Math.Floor((double) duration / (double) speed);
			return duration;
		}

		private long CalcLFTime(int level, int baseTime, double increaseFactor, int speed = 1, int robots = 0, int nanites = 1, double speedReduction = 0) {
			long duration = (long) Math.Round((double) level * (double) baseTime * (double) Math.Pow(increaseFactor, level));
			duration = (long) Math.Round((double) duration / (double) ((double) (robots + 1) * Math.Pow(2, nanites)));
			duration = (long) Math.Round((double) (1 - speedReduction / 100) * (double) duration / (double) speed);
			
			return duration;
		}

		public long CalcMaxBuildableNumber(Buildables buildable, Resources resources) {
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

		public long GetRequiredEnergyDelta(Buildables buildable, int level) {
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

		public long GetProductionEnergyDelta(Buildables buildable, int level, int energyTechnology = 0, float ratio = 1, CharacterClass userClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
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

		public int GetNextLevel(Celestial planet, Buildables buildable, bool isCollector = false, bool hasEngineer = false, bool hasFullStaff = false) {
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

		public int GetNextLevel(Researches researches, Buildables buildable) {
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

		public int GetNextLevel(Celestial planet, LFBuildables buildable) {
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

		public int GetNextLevel(Celestial planet, LFTechno buildable) {
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

		public long CalcDepositCapacity(int level) {
			return 5000 * (long) (2.5 * Math.Pow(Math.E, (20 * level / 33)));
		}

		public bool ShouldBuildMetalStorage(Planet planet, int maxLevel, int speedFactor, int hours = 12, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool forceIfFull = false) {
			long metalProduction = CalcMetalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff);
			long metalCapacity = CalcDepositCapacity(planet.Buildings.MetalStorage);
			if (forceIfFull && planet.Resources.Metal >= metalCapacity && GetNextLevel(planet, Buildables.MetalStorage) <= maxLevel)
				return true;
			if (metalCapacity < hours * metalProduction && GetNextLevel(planet, Buildables.MetalStorage) <= maxLevel)
				return true;
			else
				return false;
		}

		public bool ShouldBuildCrystalStorage(Planet planet, int maxLevel, int speedFactor, int hours = 12, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool forceIfFull = false) {
			long crystalProduction = CalcCrystalProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff);
			long crystalCapacity = CalcDepositCapacity(planet.Buildings.CrystalStorage);
			if (forceIfFull && planet.Resources.Crystal >= crystalCapacity && GetNextLevel(planet, Buildables.CrystalStorage) <= maxLevel)
				return true;
			if (crystalCapacity < hours * crystalProduction && GetNextLevel(planet, Buildables.CrystalStorage) <= maxLevel)
				return true;
			else
				return false;
		}

		public bool ShouldBuildDeuteriumTank(Planet planet, int maxLevel, int speedFactor, int hours = 12, float ratio = 1, Researches researches = null, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool forceIfFull = false) {
			long deuteriumProduction = CalcDeuteriumProduction(planet, speedFactor, ratio, researches, playerClass, hasGeologist, hasStaff);
			long deuteriumCapacity = CalcDepositCapacity(planet.Buildings.DeuteriumTank);
			if (forceIfFull && planet.Resources.Deuterium >= deuteriumCapacity && GetNextLevel(planet, Buildables.DeuteriumTank) <= maxLevel)
				return true;
			if (deuteriumCapacity < hours * deuteriumProduction && GetNextLevel(planet, Buildables.DeuteriumTank) <= maxLevel)
				return true;
			else
				return false;
		}

		public bool ShouldBuildEnergySource(Planet planet) {
			if (planet.ResourcesProduction.Energy.Available < 0)
				return true;
			else
				return false;
		}

		public Buildables GetNextEnergySourceToBuild(Planet planet, int maxSolarPlant, int maxFusionReactor, bool buildSolarSatellites = true) {
			if (planet.Buildings.SolarPlant < maxSolarPlant)
				return Buildables.SolarPlant;
			if (planet.Buildings.DeuteriumSynthesizer >= 5 && planet.Buildings.FusionReactor < maxFusionReactor)
				return Buildables.FusionReactor;
			if (buildSolarSatellites) {
				if (planet.Facilities.Shipyard >= 1)
					return Buildables.SolarSatellite;
				else if (planet.Facilities.RoboticsFactory >= 2)
					return Buildables.Shipyard;
				else
					return Buildables.RoboticsFactory;
			}
			return Buildables.Null;
		}

		public int GetSolarSatelliteOutput(Planet planet, bool isCollector = false, bool hasEngineer = false, bool hasFullStaff = false) {
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

		public int CalcNeededSolarSatellites(Planet planet, long requiredEnergy = 0, bool isCollector = false, bool hasEngineer = false, bool hasFullStaff = false) {
			if (requiredEnergy <= 0) {
				if (planet.Resources.Energy > 0)
					return 0;
				return (int) Math.Round((float) (Math.Abs(planet.Resources.Energy) / (float) GetSolarSatelliteOutput(planet, isCollector, hasEngineer, hasFullStaff)), MidpointRounding.ToPositiveInfinity);
			} else
				return (int) Math.Round((float) (Math.Abs(requiredEnergy) / (float) GetSolarSatelliteOutput(planet, isCollector, hasEngineer, hasFullStaff)), MidpointRounding.ToPositiveInfinity);
		}

		public Buildables GetNextMineToBuild(Planet planet, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, bool optimizeForStart = true) {
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

		public Buildables GetNextMineToBuild(Planet planet, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500) {
			if (researches == null)
				researches = new() { PlasmaTechnology = 0 };
			if (planet.LFBonuses == null || planet.LFBonuses.Production == null)
				planet.LFBonuses = new() { Production = new() { Metal = 0, Crystal = 0, Deuterium = 0 } };

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

		public float CalcROI(Planet planet, Buildables buildable, Researches researches = null, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new() { PlasmaTechnology = 0 };
			if (planet.LFBonuses == null || planet.LFBonuses.Production == null)
				planet.LFBonuses = new() { Production = new() { Metal = 0, Crystal = 0, Deuterium = 0 } };

			float currentProd;
			float nextLevelProd;
			float cost;
			switch (buildable) {
				case Buildables.MetalMine:
					currentProd = CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5;
					nextLevelProd = CalcMetalProduction(planet.Buildings.MetalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5;
					cost = CalcPrice(buildable, GetNextLevel(planet, buildable)).ConvertedDeuterium;
					break;
				case Buildables.CrystalMine:
					currentProd = CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5;
					nextLevelProd = CalcCrystalProduction(planet.Buildings.CrystalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5;
					cost = CalcPrice(buildable, GetNextLevel(planet, buildable)).ConvertedDeuterium;
					break;
				case Buildables.DeuteriumSynthesizer:
					currentProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff);
					nextLevelProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer + 1, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff);
					cost = CalcPrice(buildable, GetNextLevel(planet, buildable)).ConvertedDeuterium;
					break;
				default:
					return (float) 0;
			}

			float delta = nextLevelProd - currentProd;
			return delta / cost;
		}

		public float CalcDaysOfInvestmentReturn(Planet planet, Buildables buildable, Researches researches = null, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
				float currentOneDayProd = 1;
				float nextOneDayProd = 1;
				if (researches == null)
					researches = new() { PlasmaTechnology = 0 };
				if (planet.LFBonuses == null || planet.LFBonuses.Production == null)
					planet.LFBonuses = new() { Production = new() { Metal = 0, Crystal = 0, Deuterium = 0 } };
				switch (buildable) {
					case Buildables.MetalMine:
						currentOneDayProd = CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
						nextOneDayProd = CalcMetalProduction(planet.Buildings.MetalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
						break;
					case Buildables.CrystalMine:
						currentOneDayProd = CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
						nextOneDayProd = CalcCrystalProduction(planet.Buildings.CrystalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
						break;
					case Buildables.DeuteriumSynthesizer:
						currentOneDayProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff) * 24;
						nextOneDayProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer + 1, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff) * 24;
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

		public float CalcNextDaysOfInvestmentReturn(Planet planet, Researches researches = null, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new() { PlasmaTechnology = 0 };
			if (planet.LFBonuses == null || planet.LFBonuses.Production == null)
				planet.LFBonuses = new() { Production = new() { Metal = 0, Crystal = 0, Deuterium = 0 } };
			
			var metalCost = CalcPrice(Buildables.MetalMine, GetNextLevel(planet, Buildables.MetalMine)).ConvertedDeuterium;
			var currentOneDayMetalProd = CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
			var nextOneDayMetalProd = CalcMetalProduction(planet.Buildings.MetalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24;
			float metalDOIR = metalCost / (float) (nextOneDayMetalProd - currentOneDayMetalProd);
			var crystalCost = CalcPrice(Buildables.CrystalMine, GetNextLevel(planet, Buildables.CrystalMine)).ConvertedDeuterium;
			var currentOneDayCrystalProd = CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
			var nextOneDayCrystalProd = CalcCrystalProduction(planet.Buildings.CrystalMine + 1, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24;
			float crystalDOIR = crystalCost / (float) (nextOneDayCrystalProd - currentOneDayCrystalProd);
			var deuteriumCost = CalcPrice(Buildables.DeuteriumSynthesizer, GetNextLevel(planet, Buildables.DeuteriumSynthesizer)).ConvertedDeuterium;
			var currentOneDayDeuteriumProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff) * 24;
			var nextOneDayDeuteriumProd = CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer + 1, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff) * 24;
			float deuteriumDOIR = deuteriumCost / (float) (nextOneDayDeuteriumProd - currentOneDayDeuteriumProd);

			return Math.Min(float.IsNaN(deuteriumDOIR) ? float.MaxValue : deuteriumDOIR, Math.Min(float.IsNaN(crystalDOIR) ? float.MaxValue : crystalDOIR, float.IsNaN(metalDOIR) ? float.MaxValue : metalDOIR));
		}

		public float CalcNextPlasmaTechDOIR(List<Planet> planets, Researches researches, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new() { PlasmaTechnology = 0 };

			var nextPlasmaLevel = researches.PlasmaTechnology + 1;
			var nextPlasmaCost = CalcPrice(Buildables.PlasmaTechnology, nextPlasmaLevel).ConvertedDeuterium;

			long currentProd = 0;
			long nextProd = 0;
			foreach (var planet in planets) {
				if (planet.LFBonuses == null || planet.LFBonuses.Production == null)
					planet.LFBonuses = new() { Production = new() { Metal = 0, Crystal = 0, Deuterium = 0 } };
				
				currentProd += (long) Math.Round(CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24, 0);
				currentProd += (long) Math.Round(CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24, 0);
				currentProd += CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff) * 24;

				nextProd += (long) Math.Round(CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, nextPlasmaLevel, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24, 0);
				nextProd += (long) Math.Round(CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, nextPlasmaLevel, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24, 0);
				nextProd += CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, nextPlasmaLevel, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff) * 24;
			}

			float delta = nextProd - currentProd;
			return nextPlasmaCost / delta;
		}

		public float CalcNextAstroDOIR(List<Planet> planets, Researches researches, int speedFactor = 1, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false) {
			if (researches == null)
				researches = new() { PlasmaTechnology = 0 };

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
				if (planet.LFBonuses == null || planet.LFBonuses.Production == null)
					planet.LFBonuses = new() { Production = new() { Metal = 0, Crystal = 0, Deuterium = 0 } };
				
				dailyProd += (long) Math.Round(CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Metal, playerClass, hasGeologist, hasStaff) / (float) 2.5 * 24, 0);
				dailyProd += (long) Math.Round(CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Crystal, playerClass, hasGeologist, hasStaff) / (float) 1.5 * 24, 0);
				dailyProd += CalcDeuteriumProduction(planet.Buildings.DeuteriumSynthesizer, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, planet.LFBonuses.Production.Deuterium, playerClass, hasGeologist, hasStaff) * 24;
			}
			long nextDailyProd = dailyProd + (long) Math.Round((float) dailyProd / (float) planets.Count, 0);

			float delta = nextDailyProd - dailyProd;
			return totalCost / delta;
		}

		public Buildings GetMaxBuildings(int maxMetalMine, int maxCrystalMine, int maxDeuteriumSynthetizer, int maxSolarPlant, int maxFusionReactor, int maxMetalStorage, int maxCrystalStorage, int maxDeuteriumTank) {
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
		public Facilities GetMaxFacilities(int maxRoboticsFactory, int maxShipyard, int maxResearchLab, int maxMissileSilo, int maxNaniteFactory, int maxTerraformer, int maxSpaceDock) {
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

		public Facilities GetMaxLunarFacilities(int maxLunarBase, int maxLunarShipyard, int maxLunarRoboticsFactory, int maxSensorPhalanx, int maxJumpGate) {
			return new() {
				LunarBase = maxLunarBase,
				Shipyard = maxLunarShipyard,
				RoboticsFactory = maxLunarRoboticsFactory,
				SensorPhalanx = maxSensorPhalanx,
				JumpGate = maxJumpGate
			};
		}

		private Dictionary<LFBuildables, int> GetLFBuildingRequirements(LFBuildables buildable) {
			var rez = new Dictionary<LFBuildables, int>();
			//Humans
			if (buildable == LFBuildables.ResearchCentre) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 21 }, { LFBuildables.BiosphereFarm, 22 } };
			} else if (buildable == LFBuildables.AcademyOfSciences) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 41 } };
			} else if (buildable == LFBuildables.NeuroCalibrationCentre) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 41 }, { LFBuildables.AcademyOfSciences, 1 }, { LFBuildables.FusionPoweredProduction, 1 }, { LFBuildables.Skyscraper, 5 } };
			} else if (buildable == LFBuildables.HighEnergySmelting) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 21 }, { LFBuildables.BiosphereFarm, 22 }, { LFBuildables.ResearchCentre, 5 } };
			} else if (buildable == LFBuildables.FoodSilo) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 21 }, { LFBuildables.BiosphereFarm, 22 }, { LFBuildables.ResearchCentre, 5 }, { LFBuildables.HighEnergySmelting, 3 } };
			} else if (buildable == LFBuildables.FusionPoweredProduction) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 41 }, { LFBuildables.AcademyOfSciences, 1 } };
			} else if (buildable == LFBuildables.Skyscraper) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 41 }, { LFBuildables.AcademyOfSciences, 1 }, { LFBuildables.FusionPoweredProduction, 1 } };
			} else if (buildable == LFBuildables.BiotechLab) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 41 }, { LFBuildables.AcademyOfSciences, 1 }, { LFBuildables.FusionPoweredProduction, 2 } };
			} else if (buildable == LFBuildables.Metropolis) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 41 }, { LFBuildables.AcademyOfSciences, 1 }, { LFBuildables.FusionPoweredProduction, 1 }, { LFBuildables.Skyscraper, 6 }, { LFBuildables.NeuroCalibrationCentre, 1 } };
			} else if (buildable == LFBuildables.PlanetaryShield) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.ResidentialSector, 41 }, { LFBuildables.BiosphereFarm, 22 }, { LFBuildables.ResearchCentre, 5 }, { LFBuildables.AcademyOfSciences, 1 }, { LFBuildables.FusionPoweredProduction, 5 }, { LFBuildables.Skyscraper, 6 }, { LFBuildables.HighEnergySmelting, 3 }, { LFBuildables.Metropolis, 5 }, { LFBuildables.FoodSilo, 4 }, { LFBuildables.NeuroCalibrationCentre, 5 } };
			}
			//Rocktal
			else if (buildable == LFBuildables.RuneTechnologium) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 21 }, { LFBuildables.CrystalFarm, 22 } };
			} else if (buildable == LFBuildables.RuneForge) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 41 } };
			} else if (buildable == LFBuildables.Oriktorium) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 41 }, { LFBuildables.RuneForge, 1 }, { LFBuildables.Megalith, 1 }, { LFBuildables.CrystalRefinery, 5 } };
			} else if (buildable == LFBuildables.MagmaForge) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 21 }, { LFBuildables.CrystalFarm, 22 }, { LFBuildables.RuneTechnologium, 5 } };
			} else if (buildable == LFBuildables.DisruptionChamber) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 21 }, { LFBuildables.CrystalFarm, 22 }, { LFBuildables.RuneTechnologium, 5 }, { LFBuildables.MagmaForge, 3 } };
			} else if (buildable == LFBuildables.Megalith) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 41 }, { LFBuildables.RuneForge, 1 } };
			} else if (buildable == LFBuildables.CrystalRefinery) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 41 }, { LFBuildables.RuneForge, 1 }, { LFBuildables.Megalith, 1 } };
			} else if (buildable == LFBuildables.DeuteriumSynthesiser) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 41 }, { LFBuildables.RuneForge, 1 }, { LFBuildables.Megalith, 2 } };
			} else if (buildable == LFBuildables.MineralResearchCentre) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 41 }, { LFBuildables.RuneForge, 1 }, { LFBuildables.Megalith, 1 }, { LFBuildables.CrystalRefinery, 6 }, { LFBuildables.Oriktorium, 1 } };
			} else if (buildable == LFBuildables.AdvancedRecyclingPlant) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.MeditationEnclave, 41 }, { LFBuildables.CrystalFarm, 22 }, { LFBuildables.RuneForge, 1 }, { LFBuildables.Megalith, 5 }, { LFBuildables.CrystalRefinery, 6 }, { LFBuildables.Oriktorium, 5 }, { LFBuildables.RuneTechnologium, 5 }, { LFBuildables.MagmaForge, 3 }, { LFBuildables.DisruptionChamber, 4 }, { LFBuildables.MineralResearchCentre, 5 } };
			}
			//Mechas
			else if (buildable == LFBuildables.RoboticsResearchCentre) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 17 }, { LFBuildables.FusionCellFactory, 20 } };
			} else if (buildable == LFBuildables.UpdateNetwork) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 41 } };
			} else if (buildable == LFBuildables.QuantumComputerCentre) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 41 }, { LFBuildables.UpdateNetwork, 1 }, { LFBuildables.MicrochipAssemblyLine, 1 }, { LFBuildables.ProductionAssemblyHall, 5 } };
			} else if (buildable == LFBuildables.AutomatisedAssemblyCentre) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 17 }, { LFBuildables.FusionCellFactory, 20 }, { LFBuildables.RoboticsResearchCentre, 5 } };
			} else if (buildable == LFBuildables.HighPerformanceTransformer) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 17 }, { LFBuildables.FusionCellFactory, 20 }, { LFBuildables.RoboticsResearchCentre, 5 }, { LFBuildables.AutomatisedAssemblyCentre, 3 } };
			} else if (buildable == LFBuildables.MicrochipAssemblyLine) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 41 }, { LFBuildables.UpdateNetwork, 1 } };
			} else if (buildable == LFBuildables.ProductionAssemblyHall) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 41 }, { LFBuildables.UpdateNetwork, 1 }, { LFBuildables.MicrochipAssemblyLine, 1 } };
			} else if (buildable == LFBuildables.HighPerformanceSynthesiser) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 41 }, { LFBuildables.UpdateNetwork, 1 }, { LFBuildables.MicrochipAssemblyLine, 2 } };
			} else if (buildable == LFBuildables.ChipMassProduction) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 41 }, { LFBuildables.UpdateNetwork, 1 }, { LFBuildables.MicrochipAssemblyLine, 1 }, { LFBuildables.ProductionAssemblyHall, 6 }, { LFBuildables.QuantumComputerCentre, 1 } };
			} else if (buildable == LFBuildables.NanoRepairBots) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.AssemblyLine, 41 }, { LFBuildables.FusionCellFactory, 20 }, { LFBuildables.MicrochipAssemblyLine, 5 }, { LFBuildables.RoboticsResearchCentre, 5 }, { LFBuildables.HighPerformanceTransformer, 4 }, { LFBuildables.ProductionAssemblyHall, 6 }, { LFBuildables.QuantumComputerCentre, 5 }, { LFBuildables.ChipMassProduction, 5 } };
			}
			//Kaelesh
			else if (buildable == LFBuildables.VortexChamber) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 20 }, { LFBuildables.AntimatterCondenser, 21 } };
			} else if (buildable == LFBuildables.HallsOfRealisation) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 42 } };
			} else if (buildable == LFBuildables.ForumOfTranscendence) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 42 }, { LFBuildables.HallsOfRealisation, 1 }, { LFBuildables.ChrysalisAccelerator, 1 }, { LFBuildables.BioModifier, 5 } };
			} else if (buildable == LFBuildables.AntimatterConvector) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 20 }, { LFBuildables.AntimatterCondenser, 21 }, { LFBuildables.VortexChamber, 5 } };
			} else if (buildable == LFBuildables.CloningLaboratory) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 20 }, { LFBuildables.AntimatterCondenser, 21 }, { LFBuildables.VortexChamber, 5 }, { LFBuildables.AntimatterConvector, 3 } };
			} else if (buildable == LFBuildables.ChrysalisAccelerator) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 42 }, { LFBuildables.HallsOfRealisation, 1 } };
			} else if (buildable == LFBuildables.BioModifier) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 42 }, { LFBuildables.HallsOfRealisation, 1 }, { LFBuildables.ChrysalisAccelerator, 1 } };
			} else if (buildable == LFBuildables.PsionicModulator) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 42 }, { LFBuildables.HallsOfRealisation, 1 }, { LFBuildables.ChrysalisAccelerator, 2 } };
			} else if (buildable == LFBuildables.ShipManufacturingHall) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 42 }, { LFBuildables.HallsOfRealisation, 1 }, { LFBuildables.ChrysalisAccelerator, 1 }, { LFBuildables.BioModifier, 6 }, { LFBuildables.ForumOfTranscendence, 1 } };
			} else if (buildable == LFBuildables.SupraRefractor) {
				rez = new Dictionary<LFBuildables, int> { { LFBuildables.Sanctuary, 42 }, { LFBuildables.AntimatterCondenser, 21 }, { LFBuildables.VortexChamber, 5 }, { LFBuildables.AntimatterConvector, 3 }, { LFBuildables.CloningLaboratory, 4 }, { LFBuildables.HallsOfRealisation, 1 }, { LFBuildables.ChrysalisAccelerator, 5 }, { LFBuildables.BioModifier, 6 }, { LFBuildables.ShipManufacturingHall, 5 }, { LFBuildables.ForumOfTranscendence, 5 } };
			}

			return rez;
		}

		public bool isUnlocked(Celestial celestial, LFBuildables buildable) {
			var LFproperties = celestial.LFBuildings.GetType();
			var reqlist = GetLFBuildingRequirements(buildable);

			if (reqlist.Count == 0)
				return true; //nextBuilding do not need requirement (base building)

			foreach (var item in reqlist) {
				var lv = celestial.GetLevel(item.Key);
				if (lv < item.Value)
					return false;
			}
			return true;
		}

		public LFBuildables GetNextLFBuildingToBuild(Celestial planet, LFBuildings maxLFBuilding, bool preventIfMoreExpensiveThanNextMine = false, bool preventTechBuilding = false) {
			LFBuildables nextLFbuild = LFBuildables.None;
			if (planet is Moon || planet.LFtype == LFTypes.None) {
				return nextLFbuild;
			}

			float costReduction = CalcLFBuildingsResourcesCostBonus(planet);
			float popReduction = CalcLFBuildingsPopulationCostBonus(planet);
			float techPopReduction = CalcLFBuildingPopulationTechCapBonus(planet);

			LFBuildables foodBuilding = GetFoodBuilding(planet.LFtype);
			LFBuildables populationBuilding = GetPopulationBuilding(planet.LFtype);
			LFBuildables techBuilding = GetTechBuilding(planet.LFtype);
			LFBuildables T2Building = GetT2Building(planet.LFtype);
			LFBuildables T3Building = GetT3Building(planet.LFtype);

			Dictionary<LFBuildables, long> list = new();

			if (GetNextLevel(planet, foodBuilding) <= maxLFBuilding.GetLevel(foodBuilding) && planet.ResourcesProduction.Population.IsStarving()) {
				var requiredEnergy = CalcPrice(foodBuilding, GetNextLevel(planet, foodBuilding), costReduction, 0, popReduction).Energy;
				if (planet.ResourcesProduction.Energy.CurrentProduction >= requiredEnergy)
					return foodBuilding;
			}
			if (GetNextLevel(planet, foodBuilding) <= maxLFBuilding.GetLevel(foodBuilding) && planet.ResourcesProduction.Population.WillStarve()) {
				var requiredEnergy = CalcPrice(foodBuilding, GetNextLevel(planet, foodBuilding), costReduction, 0, popReduction).Energy;
				if (planet.ResourcesProduction.Energy.CurrentProduction >= requiredEnergy)
					list.Add(foodBuilding, CalcPrice(foodBuilding, GetNextLevel(planet, foodBuilding), costReduction, 0, popReduction).ConvertedDeuterium);
			}
			if (GetNextLevel(planet, populationBuilding) <= maxLFBuilding.GetLevel(populationBuilding) && planet.ResourcesProduction.Population.IsThereFoodForMore()) {
				list.Add(populationBuilding, CalcPrice(populationBuilding, GetNextLevel(planet, populationBuilding), costReduction, 0, popReduction).ConvertedDeuterium);
			}
			if (GetNextLevel(planet, populationBuilding) <= maxLFBuilding.GetLevel(populationBuilding) && planet.ResourcesProduction.Population.IsFull() && !list.Keys.Any(b => b == populationBuilding)) {
				list.Add(populationBuilding, CalcPrice(populationBuilding, GetNextLevel(planet, populationBuilding), costReduction, 0, popReduction).ConvertedDeuterium);
			}
			if (isUnlocked(planet, techBuilding) && GetNextLevel(planet, techBuilding) <= maxLFBuilding.GetLevel(techBuilding) && !preventTechBuilding) {
				list.Add(techBuilding, CalcPrice(techBuilding, GetNextLevel(planet, techBuilding), costReduction, 0, popReduction).ConvertedDeuterium);
			}
			if (planet.ResourcesProduction.Population.NeedsMoreT2(techPopReduction) || planet.ResourcesProduction.Population.NeedsMoreT3(techPopReduction)) {
				if (planet.ResourcesProduction.Population.NeedsMoreT2(techPopReduction) && isUnlocked(planet, T2Building) && GetNextLevel(planet, T2Building) <= maxLFBuilding.GetLevel(T2Building)) {
					if (CalcLivingSpace(planet as Planet) >= CalcPrice(T2Building, planet.GetLevel(T2Building) + 1, costReduction, 0, popReduction).Population) {
						Resources xCostBuildable = CalcPrice(T2Building, GetNextLevel(planet, T2Building), costReduction, 0, popReduction);
						if (xCostBuildable.Population <= planet.Resources.Population) {
							list.Add(T2Building, CalcPrice(T2Building, GetNextLevel(planet, T2Building), costReduction, 0, popReduction).ConvertedDeuterium);
						}
					}
				}
				else if (planet.ResourcesProduction.Population.NeedsMoreT3(techPopReduction)) {										
					if (isUnlocked(planet, T2Building)) {
						if (CalcLivingSpace(planet as Planet) >= CalcPrice(T2Building, planet.GetLevel(T2Building) + 1, costReduction, 0, popReduction).Population && GetNextLevel(planet, T2Building) <= maxLFBuilding.GetLevel(T2Building)) {
							Resources xCostBuildable = CalcPrice(T2Building, GetNextLevel(planet, T2Building), costReduction, 0, popReduction);
							if (xCostBuildable.Population <= planet.Resources.Population) {
								list.Add(T2Building, CalcPrice(T2Building, GetNextLevel(planet, T2Building), costReduction, 0, popReduction).ConvertedDeuterium);
							}
						}
					}
					if (isUnlocked(planet, T3Building)) {
						if (CalcLivingSpace(planet as Planet) >= CalcPrice(T3Building, planet.GetLevel(T3Building) + 1, costReduction, 0, popReduction).Population && GetNextLevel(planet, T3Building) <= maxLFBuilding.GetLevel(T3Building)) {
							Resources xCostBuildable = CalcPrice(T3Building, GetNextLevel(planet, T3Building), costReduction, 0, popReduction);
							if (xCostBuildable.Population <= planet.Resources.Population) {
								list.Add(T3Building, CalcPrice(T3Building, GetNextLevel(planet, T3Building), costReduction, 0, popReduction).ConvertedDeuterium);
							}
						}
					}					
				}				
			}

			var leastExpensiveBuilding = GetLeastExpensiveLFBuilding(planet, maxLFBuilding);
			if (leastExpensiveBuilding != LFBuildables.None)
				list.Add(leastExpensiveBuilding, CalcPrice(leastExpensiveBuilding, GetNextLevel(planet, leastExpensiveBuilding), costReduction, 0, popReduction).ConvertedDeuterium);

			if (list.Count == 0) {
				if (GetNextLevel(planet, foodBuilding) <= maxLFBuilding.GetLevel(foodBuilding)) {
					list.Add(foodBuilding, CalcPrice(foodBuilding, GetNextLevel(planet, foodBuilding), costReduction, 0, popReduction).ConvertedDeuterium);
				}
				if (GetNextLevel(planet, populationBuilding) <= maxLFBuilding.GetLevel(populationBuilding)) {
					list.Add(populationBuilding, CalcPrice(populationBuilding, GetNextLevel(planet, populationBuilding), costReduction, 0, popReduction).ConvertedDeuterium);
				}
			}

			if (list.Count > 0) {
				nextLFbuild = list.OrderBy(x => x.Value).First().Key;
			}
			
			if (preventIfMoreExpensiveThanNextMine) {
				var nextlvl = GetNextLevel(planet, nextLFbuild);
				var nextlvlcost = CalcPrice(nextLFbuild, nextlvl, costReduction, 0, popReduction);
				var nextMine = GetNextMineToBuild(planet as Planet, 100, 100, 100, false);
				var nextMineCost = CalcPrice(nextMine, GetNextLevel(planet, nextMine));
				if (nextlvlcost.ConvertedDeuterium > nextMineCost.ConvertedDeuterium) {
					_logger.WriteLog(LogLevel.Debug, LogSender.Brain, $"{nextLFbuild.ToString()} level {nextlvl} is more expensive than this planet's next mine, build {nextMine.ToString()} first.");
					nextLFbuild = LFBuildables.None;
				}
			}
			return nextLFbuild;
		}

		public LFBuildables GetPopulationBuilding(LFTypes LFtype) {
			LFBuildables populationBuilding = LFBuildables.None;
			if (LFtype == LFTypes.Humans) {
				populationBuilding = LFBuildables.ResidentialSector;
			} else if (LFtype == LFTypes.Rocktal) {
				populationBuilding = LFBuildables.MeditationEnclave;
			} else if (LFtype == LFTypes.Mechas) {
				populationBuilding = LFBuildables.AssemblyLine;
			} else if (LFtype == LFTypes.Kaelesh) {
				populationBuilding = LFBuildables.Sanctuary;
			}
			return populationBuilding;
		}

		public LFBuildables GetFoodBuilding(LFTypes LFtype) {
			LFBuildables foodBuilding = LFBuildables.None;
			if (LFtype == LFTypes.Humans) {
				foodBuilding = LFBuildables.BiosphereFarm;
			} else if (LFtype == LFTypes.Rocktal) {
				foodBuilding = LFBuildables.CrystalFarm;
			} else if (LFtype == LFTypes.Mechas) {
				foodBuilding = LFBuildables.FusionCellFactory;
			} else if (LFtype == LFTypes.Kaelesh) {
				foodBuilding = LFBuildables.AntimatterCondenser;
			}
			return foodBuilding;
		}

		public LFBuildables GetTechBuilding(LFTypes LFtype) {
			LFBuildables techBuilding = LFBuildables.None;
			if (LFtype == LFTypes.Humans) {
				techBuilding = LFBuildables.ResearchCentre;
			} else if (LFtype == LFTypes.Rocktal) {
				techBuilding = LFBuildables.RuneTechnologium;
			} else if (LFtype == LFTypes.Mechas) {
				techBuilding = LFBuildables.RoboticsResearchCentre;
			} else if (LFtype == LFTypes.Kaelesh) {
				techBuilding = LFBuildables.VortexChamber;
			}
			return techBuilding;
		}

		public LFBuildables GetT2Building(LFTypes LFtype) {
			LFBuildables t2Building = LFBuildables.None;
			if (LFtype == LFTypes.Humans) {
				t2Building = LFBuildables.AcademyOfSciences;
			} else if (LFtype == LFTypes.Rocktal) {
				t2Building = LFBuildables.RuneForge;
			} else if (LFtype == LFTypes.Mechas) {
				t2Building = LFBuildables.UpdateNetwork;
			} else if (LFtype == LFTypes.Kaelesh) {
				t2Building = LFBuildables.HallsOfRealisation;
			}
			return t2Building;
		}

		public LFBuildables GetT3Building(LFTypes LFtype) {
			LFBuildables t3Building = LFBuildables.None;
			if (LFtype == LFTypes.Humans) {
				t3Building = LFBuildables.NeuroCalibrationCentre;
			} else if (LFtype == LFTypes.Rocktal) {
				t3Building = LFBuildables.Oriktorium;
			} else if (LFtype == LFTypes.Mechas) {
				t3Building = LFBuildables.QuantumComputerCentre;
			} else if (LFtype == LFTypes.Kaelesh) {
				t3Building = LFBuildables.ForumOfTranscendence;
			}
			return t3Building;
		}

		public List<LFBuildables> GetOtherBuildings(LFTypes LFtype) {
			List<LFBuildables> list = new();
			if (LFtype == LFTypes.Humans) {
				list.Add(LFBuildables.HighEnergySmelting);
				list.Add(LFBuildables.FoodSilo);
				list.Add(LFBuildables.FusionPoweredProduction);
				list.Add(LFBuildables.Skyscraper);
				list.Add(LFBuildables.BiotechLab);
				list.Add(LFBuildables.Metropolis);
				list.Add(LFBuildables.PlanetaryShield);
			} else if (LFtype == LFTypes.Rocktal) {
				list.Add(LFBuildables.MagmaForge);
				list.Add(LFBuildables.DisruptionChamber);
				list.Add(LFBuildables.Megalith);
				list.Add(LFBuildables.CrystalRefinery);
				list.Add(LFBuildables.DeuteriumSynthesiser);
				list.Add(LFBuildables.MineralResearchCentre);
				list.Add(LFBuildables.AdvancedRecyclingPlant);
			} else if (LFtype == LFTypes.Mechas) {
				list.Add(LFBuildables.AutomatisedAssemblyCentre);
				list.Add(LFBuildables.HighPerformanceTransformer);
				list.Add(LFBuildables.MicrochipAssemblyLine);
				list.Add(LFBuildables.ProductionAssemblyHall);
				list.Add(LFBuildables.HighPerformanceSynthesiser);
				list.Add(LFBuildables.ChipMassProduction);
				list.Add(LFBuildables.NanoRepairBots);
			} else if (LFtype == LFTypes.Kaelesh) {
				list.Add(LFBuildables.AntimatterConvector);
				list.Add(LFBuildables.CloningLaboratory);
				list.Add(LFBuildables.ChrysalisAccelerator);
				list.Add(LFBuildables.BioModifier);
				list.Add(LFBuildables.PsionicModulator);
				list.Add(LFBuildables.ShipManufacturingHall);
				list.Add(LFBuildables.SupraRefractor);
			}
			return list;
		}

		public float CalcLFBuildingsResourcesCostBonus(Celestial planet) {
			var output = 0;
			switch (planet.LFtype) {
				case LFTypes.Rocktal:
					output = planet.GetLevel(LFBuildables.Megalith);
					break;
				default:
					break;
			}
			return output;
		}

		public float CalcLFBuildingsPopulationCostBonus(Celestial planet) {
			var output = 0;
			switch (planet.LFtype) {
				case LFTypes.Rocktal:
					output = planet.GetLevel(LFBuildables.Megalith);
					break;
				default:
					break;
			}
			return output;
		}

		public float CalcLFBuildingPopulationTechCapBonus(Celestial planet) {
			var output = 0;
			switch (planet.LFtype) {
				case LFTypes.Kaelesh:
					output = 2 * planet.GetLevel(LFBuildables.PsionicModulator);
					break;
				default:
					break;
			}
			return output;
		}

		private LFBuildables GetLessExpensiveLFBuilding(Celestial planet, Resources Currentlfbuildingcost, LFBuildings maxlvlLFBuilding) {
			LFBuildables lessExpensiveLFBuild = LFBuildables.None;
			List<LFBuildables> possibleBuildings = GetOtherBuildings(planet.LFtype);
			var livingSpace = CalcLivingSpace(planet as Planet);

			float costReduction = CalcLFBuildingsResourcesCostBonus(planet);
			float popReduction = CalcLFBuildingsPopulationCostBonus(planet);

			possibleBuildings = possibleBuildings.Where(b => isUnlocked(planet, b))
				.Where(b => CalcPrice(b, GetNextLevel(planet, b), costReduction, 0, popReduction).ConvertedDeuterium < Currentlfbuildingcost.ConvertedDeuterium)
				.Where(b => CalcPrice(b, GetNextLevel(planet, b), costReduction, 0, popReduction).Population <= livingSpace)
				.Where(b => (int) GetNextLevel(planet, b) <= (int) maxlvlLFBuilding.GetLevel(b))
				.Where(b => planet.ResourcesProduction.Energy.CurrentProduction >= CalcPrice(b, GetNextLevel(planet, b), costReduction, 0, popReduction).Energy)
				.OrderBy(b => CalcPrice(b, GetNextLevel(planet, b), costReduction, 0, popReduction).ConvertedDeuterium)
				.ToList();

			if (possibleBuildings.Count > 0) {
				lessExpensiveLFBuild = possibleBuildings.First();
			} else {
				lessExpensiveLFBuild = LFBuildables.None;
			}

			return lessExpensiveLFBuild;
		}

		public LFBuildables GetLeastExpensiveLFBuilding(Celestial planet, LFBuildings maxlvlLFBuilding) {
			Resources nextlfcost = new();
			LFBuildables leastExpensiveLFBuild = LFBuildables.None;
			var livingSpace = CalcLivingSpace(planet as Planet);
			List<LFBuildables> possibleBuildings = GetOtherBuildings(planet.LFtype);

			float costReduction = CalcLFBuildingsResourcesCostBonus(planet);
			float popReduction = CalcLFBuildingsPopulationCostBonus(planet);

			possibleBuildings = possibleBuildings.Where(b => isUnlocked(planet, b))
				.Where(b => CalcPrice(b, GetNextLevel(planet, b), costReduction, 0, popReduction).Population <= livingSpace)
				.Where(b => (int) GetNextLevel(planet, b) <= (int) maxlvlLFBuilding.GetLevel(b))
				.Where(b => planet.ResourcesProduction.Energy.CurrentProduction >= CalcPrice(b, GetNextLevel(planet, b), costReduction, 0, popReduction).Energy)
				.OrderBy(b => CalcPrice(b, GetNextLevel(planet, b), costReduction, 0, popReduction).ConvertedDeuterium)
				.ToList();

			if (possibleBuildings.Count > 0) {
				leastExpensiveLFBuild = possibleBuildings.First();
			} else {
				leastExpensiveLFBuild = LFBuildables.None;
			}

			return leastExpensiveLFBuild;
		}

		public long CalcFoodProduction(Planet planet) {
			var foodFactory = GetFoodBuilding(planet.LFtype);
			var level = planet.GetLevel(foodFactory);
			var bonus = CalcFoodProductionBonus(planet);
			return CalcFoodProduction(foodFactory, level, bonus);
		}

		public long CalcFoodProduction(LFBuildables foodFactory, int level, double bonus = 0) {
			long output = 0;
			long baseProd = 0;
			double increaseFactor = 0;

			switch (foodFactory) {
				case LFBuildables.BiosphereFarm:
					baseProd = 10;
					increaseFactor = 1.14;
					break;
				case LFBuildables.CrystalFarm:
					baseProd = 6;
					increaseFactor = 1.14;
					break;
				case LFBuildables.FusionCellFactory:
					baseProd = 23;
					increaseFactor = 1.12;
					break;
				case LFBuildables.AntimatterCondenser:
					baseProd = 12;
					increaseFactor = 1.14;
					break;
				default:
					break;
			}

			output = (long) Math.Floor(baseProd * Math.Pow(increaseFactor, level) * (level + 1) * (1 + bonus));
			return output;
		}

		public double CalcFoodProductionBonus(Planet planet) {
			double bonus = 0;

			switch (planet.LFtype) {
				case LFTypes.Humans:
					bonus += planet.GetLevel(LFBuildables.BiotechLab) * 0.05;
					break;
				case LFTypes.Mechas:
					bonus += planet.GetLevel(LFBuildables.MicrochipAssemblyLine) * 0.02;
					break;
				case LFTypes.Rocktal:
				case LFTypes.Kaelesh:
				default:
					break;
			}

			return bonus;
		}

		public long CalcLivingSpace(Planet planet) {
			var popuFactory = GetPopulationBuilding(planet.LFtype);
			var level = planet.GetLevel(popuFactory);
			var bonus = CalcLivingSpaceBonus(planet);
			return CalcLivingSpace(popuFactory, level, bonus);
		}

		public long CalcLivingSpace(LFBuildables populationFactory, int level, double bonus = 0) {
			long output = 0;
			long baseProd = 0;			
			double prodIncreaseFactor = 0;

			switch (populationFactory) {
				case LFBuildables.ResidentialSector:
					baseProd = 210;
					prodIncreaseFactor = 1.21;
					break;
				case LFBuildables.MeditationEnclave:
					baseProd = 150;
					prodIncreaseFactor = 1.216;
					break;
				case LFBuildables.AssemblyLine:
					baseProd = 500;
					prodIncreaseFactor = 1.205;
					break;
				case LFBuildables.Sanctuary:
					baseProd = 250;
					prodIncreaseFactor = 1.21;
					break;
				default:
					break;
			}

			output = (long) Math.Floor(baseProd * Math.Pow(prodIncreaseFactor, level) * (level + 1) * (1 + bonus));
			return output;
		}

		public double CalcLivingSpaceBonus(Planet planet) {
			double bonus = 0;

			switch (planet.LFtype) {
				case LFTypes.Humans:
					bonus += planet.GetLevel(LFBuildables.Skyscraper) * 0.015;
					break;
				case LFTypes.Mechas:
					bonus += planet.GetLevel(LFBuildables.ProductionAssemblyHall) * 0.02;
					break;
				case LFTypes.Kaelesh:
					bonus += planet.GetLevel(LFBuildables.ChrysalisAccelerator) * 0.02;
					break;
				case LFTypes.Rocktal:
				default:
					break;
			}

			return bonus;
		}
		
		public long CalcFoodConsumption(Planet planet) {
			var popuFactory = GetPopulationBuilding(planet.LFtype);
			var level = planet.GetLevel(popuFactory);
			var bonus = CalcFoodConsumptionBonus(planet);
			return CalcFoodConsumption(popuFactory, level, bonus);
		}

		public long CalcFoodConsumption(LFBuildables populationFactory, int level, double bonus = 0) {
			long output = 0;
			long consumptionBase = 0;
			double consumptionIncreaseFactor = 0;

			switch (populationFactory) {
				case LFBuildables.ResidentialSector:
					consumptionBase = 9;
					consumptionIncreaseFactor = 1.15;
					break;
				case LFBuildables.MeditationEnclave:
					consumptionBase = 5;
					consumptionIncreaseFactor = 1.15;
					break;
				case LFBuildables.AssemblyLine:
					consumptionBase = 22;
					consumptionIncreaseFactor = 1.15;
					break;
				case LFBuildables.Sanctuary:
					consumptionBase = 11;
					consumptionIncreaseFactor = 1.15;
					break;
				default:
					break;
			}

			output = (long) Math.Floor(consumptionBase * Math.Pow(consumptionIncreaseFactor, level) * (level + 1) * (1 + bonus));
			return output;
		}

		public double CalcFoodConsumptionBonus(Planet planet) {
			double bonus = 0;

			switch (planet.LFtype) {
				case LFTypes.Humans:
					bonus += planet.GetLevel(LFBuildables.FoodSilo) * 0.01;
					break;
				case LFTypes.Kaelesh:
					bonus += planet.GetLevel(LFBuildables.AntimatterConvector) * 0.01;
					break;
				case LFTypes.Rocktal:
				case LFTypes.Mechas:
				default:
					break;
			}

			return bonus;
		}

		public long CalcSatisfied(Planet planet) {
			var popuFactory = GetPopulationBuilding(planet.LFtype);
			var popuFactoryLevel = planet.GetLevel(popuFactory);
			var foodFactory = GetFoodBuilding(planet.LFtype);
			var foodFactoryLevel = planet.GetLevel(foodFactory);
			var populationBonus = CalcLivingSpaceBonus(planet);
			var foodProductionBonus = CalcFoodProductionBonus(planet);
			var foodConsumptionBonus = CalcFoodConsumptionBonus(planet);
			return CalcSatisfied(popuFactory, popuFactoryLevel, foodFactory, foodFactoryLevel, populationBonus, foodProductionBonus, foodConsumptionBonus);
		}

		public long CalcSatisfied(LFBuildables populationFactory, int populationFactoryLevel, LFBuildables foodFactory, int foodFactoryLevel, double populationBonus = 0, double foodProductionBonus = 0, double foodConsumptionBonus = 0) {
			long livingSpace = CalcLivingSpace(populationFactory, populationFactoryLevel, populationBonus);
			long foodConsumption = CalcFoodConsumption(populationFactory, populationFactoryLevel, foodConsumptionBonus);
			long foodProduction = CalcFoodProduction(foodFactory, foodFactoryLevel, foodProductionBonus);
			return (long) Math.Floor(((double) foodProduction / (double) foodConsumption) * (double) livingSpace);
		}

		public LFTechno GetNextLFTechToBuild(Celestial celestial, LFTechs MaxReasearchLevel) {
			//TODO
			//As planets can have any lifeform techs, its complicated to find which techs are existing on a planet if the techs are not at least level 1
			//Therefore, for the moment, up only techs that are minimum level 1, its a way to also allows player to chose which research to up
			foreach (PropertyInfo prop in celestial.LFTechs.GetType().GetProperties()) {
				foreach (LFTechno nextLFTech in Enum.GetValues<LFTechno>()) {
					if ((int) prop.GetValue(celestial.LFTechs) > 0 && GetNextLevel(celestial, nextLFTech) <= MaxReasearchLevel.GetLevel(nextLFTech) && prop.Name == nextLFTech.ToString()) {
						//Console.WriteLine($"-----------------------------> {nextLFTech}: {GetNextLevel(celestial, nextLFTech)} / {MaxReasearchLevel.GetLevel(nextLFTech)}");
						return nextLFTech;
					}

				}

			}
			return LFTechno.None;
		}

		public LFTechno GetLessExpensiveLFTechToBuild(Celestial celestial, Resources currentcost, LFTechs MaxReasearchLevel, double costReduction = 0) {
			LFTechno nextLFtech = LFTechno.None;
			Resource nextLFtechcost = new();
			foreach (PropertyInfo prop in celestial.LFTechs.GetType().GetProperties()) {
				foreach (LFTechno next in Enum.GetValues<LFTechno>()) {
					if ((int) prop.GetValue(celestial.LFTechs) > 0 && GetNextLevel(celestial, next) <= MaxReasearchLevel.GetLevel(next) && prop.Name == next.ToString()) {
						//Console.WriteLine($"-----------------------------> {next}: {GetNextLevel(celestial, next)} / {MaxReasearchLevel.GetLevel(next)}");
						var nextLFtechlvl = GetNextLevel(celestial, next);
						Resources newcost = CalcPrice(next, nextLFtechlvl, costReduction);
						if (newcost.ConvertedDeuterium < currentcost.ConvertedDeuterium) {
							currentcost = newcost;
							nextLFtech = next;
						}
					}

				}

			}
			return nextLFtech;
		}

		public Buildables GetNextBuildingToBuild(Planet planet, Researches researches, Buildings maxBuildings, Facilities maxFacilities, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1) {
			Buildables buildableToBuild = Buildables.Null;
			if (ShouldBuildTerraformer(planet, researches, maxFacilities.Terraformer))
				buildableToBuild = Buildables.Terraformer;
			if (buildableToBuild == Buildables.Null && ShouldBuildEnergySource(planet))
				buildableToBuild = GetNextEnergySourceToBuild(planet, maxBuildings.SolarPlant, maxBuildings.FusionReactor, settings.BuildSolarSatellites);
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

		public Buildables GetNextDepositToBuild(Planet planet, Researches researches, Buildings maxBuildings, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1) {
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
		public Buildables GetNextFacilityToBuild(Planet planet, Researches researches, Buildings maxBuildings, Facilities maxFacilities, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1, bool force = false) {
			Buildables facilityToBuild = Buildables.Null;
			if (settings.PrioritizeRobotsAndNanites)
				if (planet.Facilities.RoboticsFactory < 10 && planet.Facilities.RoboticsFactory < maxFacilities.RoboticsFactory && planet.Constructions.LFBuildingID == (int) LFBuildables.None)
					facilityToBuild = Buildables.RoboticsFactory;
				else if (planet.Facilities.RoboticsFactory >= 10 && researches.ComputerTechnology >= 10 && planet.Facilities.NaniteFactory < maxFacilities.NaniteFactory && !planet.HasProduction() && planet.Constructions.LFBuildingID == (int) LFBuildables.None)
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

		public Buildables GetNextMineToBuild(Planet planet, Researches researches, Buildings maxBuildings, Facilities maxFacilities, CharacterClass playerClass, Staff staff, ServerData serverData, AutoMinerSettings settings, float ratio = 1) {
			return GetNextMineToBuild(planet, researches, serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, ratio, playerClass, staff.Geologist, staff.IsFull, settings.OptimizeForStart, settings.MaxDaysOfInvestmentReturn);
		}

		public Buildables GetNextLunarFacilityToBuild(Moon moon, Researches researches, Facilities maxLunarFacilities) {
			return GetNextLunarFacilityToBuild(moon, researches, maxLunarFacilities.LunarBase, maxLunarFacilities.RoboticsFactory, maxLunarFacilities.SensorPhalanx, maxLunarFacilities.JumpGate, maxLunarFacilities.Shipyard);
		}

		public Buildables GetNextLunarFacilityToBuild(Moon moon, Researches researches, int maxLunarBase = 8, int maxRoboticsFactory = 8, int maxSensorPhalanx = 6, int maxJumpGate = 1, int maxShipyard = 0) {
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

		public bool ShouldBuildRoboticFactory(Celestial celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			if (celestial is Planet) {
				if (celestial.Constructions.LFBuildingID != (int) LFBuildables.None)
					return false;

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

		public bool ShouldBuildShipyard(Celestial celestial, int maxLevel = 12, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
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

		public bool ShouldBuildResearchLab(Planet celestial, int maxLevel = 12, Researches researches = null, int speedFactor = 1, float researchDurationDivisor = 2, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			var nextMine = GetNextMineToBuild(celestial, researches, speedFactor, maxMetalMine, maxCrystalMine, maxDeuteriumSynthetizer, ratio, playerClass, hasGeologist, hasStaff, optimizeForStart, maxDaysOfInvestmentReturn);
			var nextMineLevel = GetNextLevel(celestial, nextMine);
			var nextMinePrice = CalcPrice(nextMine, nextMineLevel);

			var nextLabLevel = GetNextLevel(celestial, Buildables.ResearchLab);
			var nextLabPrice = CalcPrice(Buildables.ResearchLab, nextLabLevel);

			/*
			var nextResearch = GetNextResearchToBuild(celestial, researches);
			var nextResearchLevel = GetNextLevel(researches, nextResearch);
			var nextResearchTime = CalcProductionTime(nextResearch, nextResearchLevel, (int) Math.Round(speedFactor * researchDurationDivisor), celestial.Facilities);
			var nextLabTime = CalcProductionTime(Buildables.ResearchLab, nextLabLevel, speedFactor, celestial.Facilities);
			*/

			if (
				nextLabLevel <= maxLevel &&
				(nextMinePrice.ConvertedDeuterium > nextLabPrice.ConvertedDeuterium || force)
			)
				return true;
			else
				return false;
		}

		public bool ShouldBuildMissileSilo(Planet celestial, int maxLevel = 6, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
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

		public bool ShouldBuildNanites(Planet celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
			if (celestial.Constructions.LFBuildingID != (int) LFBuildables.None)
				return false;

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

		public bool ShouldBuildTerraformer(Planet celestial, Researches researches, int maxLevel = 10) {
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

		private bool ShouldBuildLFBasics(Celestial celestial, int maxPopuFactory = 100, int maxFoodFactory = 100) {
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

		public bool ShouldBuildSpaceDock(Planet celestial, int maxLevel = 10, Researches researches = null, int speedFactor = 1, int maxMetalMine = 100, int maxCrystalMine = 100, int maxDeuteriumSynthetizer = 100, float ratio = 1, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasStaff = false, bool optimizeForStart = true, float maxDaysOfInvestmentReturn = 36500, bool force = false) {
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

		public bool ShouldBuildLunarBase(Moon moon, int maxLevel = 8) {
			var nextLunarBaseLevel = GetNextLevel(moon, Buildables.LunarBase);

			if (
				nextLunarBaseLevel <= maxLevel &&
				moon.Fields.Free == 1
			)
				return true;
			else
				return false;
		}

		public bool ShouldBuildSensorPhalanx(Moon moon, int maxLevel = 7) {
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

		public bool ShouldBuildJumpGate(Moon moon, int maxLevel = 1, Researches researches = null) {
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

		public bool ShouldResearchEnergyTech(List<Planet> planets, int energyTech, int maxEnergyTech = 25, CharacterClass playerClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
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

		public bool ShouldResearchEnergyTech(List<Planet> planets, Researches researches, int maxEnergyTech = 25, CharacterClass playerClass = CharacterClass.NoClass, bool hasEngineer = false, bool hasStaff = false) {
			return ShouldResearchEnergyTech(planets, researches.EnergyTechnology, maxEnergyTech, playerClass, hasEngineer, hasStaff);
		}

		public Buildables GetNextResearchToBuild(Planet celestial, Researches researches, bool prioritizeRobotsAndNanitesOnNewPlanets = false, Slots slots = null, int maxEnergyTechnology = 20, int maxLaserTechnology = 12, int maxIonTechnology = 5, int maxHyperspaceTechnology = 20, int maxPlasmaTechnology = 20, int maxCombustionDrive = 19, int maxImpulseDrive = 17, int maxHyperspaceDrive = 15, int maxEspionageTechnology = 8, int maxComputerTechnology = 20, int maxAstrophysics = 23, int maxIntergalacticResearchNetwork = 12, int maxWeaponsTechnology = 25, int maxShieldingTechnology = 25, int maxArmourTechnology = 25, bool optimizeForStart = true, bool ensureExpoSlots = true, CharacterClass playerClass = CharacterClass.NoClass, bool hasGeologist = false, bool hasAdmiral = false) {
			if (ShouldBuildResearchLab(celestial, 12, researches))
				return Buildables.Null;

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
						if (researches.GravitonTechnology >= 1)
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

		public bool IsThereTransportTowardsCelestial(Celestial celestial, List<Fleet> fleets) {
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

		public bool AreThereIncomingResources(Celestial celestial, List<Fleet> fleets) {
			return fleets
				.Where(f => f.Mission == Missions.Transport)
				.Where(f => f.Resources.TotalResources > 0)
				.Where(f => f.ReturnFlight == false)
				.Where(f => f.Destination.IsSame(celestial.Coordinate))
				.Any();
		}

		public List<Fleet> GetIncomingFleets(Celestial celestial, List<Fleet> fleets) {
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

		public List<Fleet> GetIncomingFleetsWithResources(Celestial celestial, List<Fleet> fleets) {
			List<Fleet> incomingFleets = GetIncomingFleets(celestial, fleets);
			incomingFleets = incomingFleets
				.Where(f => f.Resources.TotalResources > 0)
				.ToList();
			return incomingFleets;
		}

		public Fleet GetFirstReturningExpedition(Coordinate coord, List<Fleet> fleets) {
			var celestialExpos = fleets
				.Where(f => f.Origin.IsSame(coord))
				.Where(f => f.Mission == Missions.Expedition);
			if (celestialExpos.Any()) {
				return celestialExpos
					.OrderBy(fleet => fleet.BackIn).First();
			} else
				return null;
		}

		public List<Fleet> GetMissionsInProgress(Missions mission, List<Fleet> fleets) {
			var inProgress = (fleets ?? new List<Fleet>()).Where(f => f.Mission == mission);
			if (inProgress.Any()) {
				return inProgress.ToList();
			} else
				return new List<Fleet>();
		}

		public List<Fleet> GetMissionsInProgress(Coordinate origin, Missions mission, List<Fleet> fleets) {
			var inProgress = (fleets ?? new List<Fleet>())
				.Where(f => f.Origin.IsSame(origin))
				.Where(f => f.Mission == mission);
			if (inProgress.Any()) {
				return inProgress.ToList();
			} else
				return new List<Fleet>();
		}

		public Fleet GetFirstReturningEspionage(List<Fleet> fleets) {
			var celestialEspionages = GetMissionsInProgress(Missions.Spy, fleets);
			if (celestialEspionages.Count > 0) {
				return celestialEspionages
					.OrderBy(fleet => fleet.BackIn).First();
			} else
				return null;
		}

		public Fleet GetLastReturningEspionage(List<Fleet> fleets) {
			var celestialEspionages = GetMissionsInProgress(Missions.Spy, fleets);
			if (celestialEspionages.Count > 0) {
				return celestialEspionages
					.OrderBy(fleet => fleet.BackIn).Last();
			} else
				return null;
		}

		public Fleet GetFirstReturningEspionage(Coordinate origin, List<Fleet> fleets) {
			var celestialEspionages = GetMissionsInProgress(origin, Missions.Spy, fleets);
			if (celestialEspionages != null) {
				return celestialEspionages
					.OrderBy(fleet => fleet.BackIn).First();
			} else
				return null;
		}

		public List<Celestial> ParseCelestialsList(dynamic source, List<Celestial> currentCelestials) {
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

		public int CalcMaxExpeditions(Researches researches, CharacterClass playerClass, Staff staff) {
			return CalcMaxExpeditions(researches.Astrophysics, playerClass == CharacterClass.Discoverer, staff.Geologist);
		}
		public int CalcMaxExpeditions(int astrophysics, bool isDiscoverer, bool hasAdmiral) {
			int slots = (int) Math.Round(Math.Sqrt(astrophysics), 0, MidpointRounding.ToZero);
			if (isDiscoverer)
				slots += 2;
			if (hasAdmiral)
				slots += 1;
			return slots;
		}

		public int CalcMaxPlanets(int astrophysics) {
			return (int) Math.Round((float) ((astrophysics + 3) / 2), 0, MidpointRounding.ToZero);
		}

		public int CalcMaxPlanets(Researches researches) {
			return researches == null ? 1 : CalcMaxPlanets(researches.Astrophysics);
		}

		public bool CalcLimitAstro(int pos, Researches researches) {
			return ((pos >= 4 && pos <= 13 && (int)researches.Astrophysics >= 4) ||
				(pos >= 2 && pos <= 14 && (int)researches.Astrophysics >= 6) ||
				(pos >= 1 && pos <= 15 && (int)researches.Astrophysics >= 8));
		}

		public int CalcMaxCrawlers(Planet planet, CharacterClass userClass, bool hasGeologist) {
			if (userClass == CharacterClass.Collector && hasGeologist) {
				return (int) Math.Round(8.8 * (planet.Buildings.MetalMine + planet.Buildings.CrystalMine + planet.Buildings.DeuteriumSynthesizer));
			}
			return 8 * (planet.Buildings.MetalMine + planet.Buildings.CrystalMine + planet.Buildings.DeuteriumSynthesizer);
		}

		public int CalcOptimalCrawlers(Planet planet, CharacterClass userClass, Staff staff, Researches researches, ServerData serverData) {
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

		public bool ShouldAbandon(Planet celestial, int maxCases, int Temperature, Fields fieldsSettings, Temperature temperaturesSettings) {
			if (maxCases < fieldsSettings.Total || (Temperature < temperaturesSettings.Min || Temperature > temperaturesSettings.Max)) {
				return true;
			}
			return false;
		}
		
		public int CountPlanetsInRange(List<Planet> planets, int galaxy, int minSystem = 1, int maxSystem = 499, int minPosition = 1, int maxPositions = 15, int minSlots = 1, int minTemperature = -130, int maxTemperature = 260) {
			return planets
				.Where(planet => planet.Coordinate.Type == Celestials.Planet)
				.Where(planet => planet.Coordinate.Galaxy == galaxy)
				.Where(planet => planet.Coordinate.System >= minSystem && planet.Coordinate.System <= maxSystem)
				.Where(planet => planet.Coordinate.Position >= minPosition && planet.Coordinate.Position <= maxPositions)
				.Where(planet => planet.Fields.Total >= minSlots)
				.Where(planet => planet.Temperature.Max >= minTemperature && planet.Temperature.Max <= maxTemperature)
				.Count();
		}

		public int CountPlanetsInRange(List<Celestial> planets, int galaxy, int minSystem = 1, int maxSystem = 499, int minPosition = 1, int maxPositions = 15, int minSlots = 1, int minTemperature = -130, int maxTemperature = 260) {
			return planets
				.Where(planet => planet is Planet)
				.Where(planet => planet.Coordinate.Type == Celestials.Planet)
				.Where(planet => planet.Coordinate.Galaxy == galaxy)
				.Where(planet => planet.Coordinate.System >= minSystem && planet.Coordinate.System <= maxSystem)
				.Where(planet => planet.Coordinate.Position >= minPosition && planet.Coordinate.Position <= maxPositions)
				.Where(planet => planet.Fields.Total >= minSlots)
				.Where(planet => (planet as Planet).Temperature.Max >= minTemperature && (planet as Planet).Temperature.Max <= maxTemperature)
				.Count();
		}

		public bool	IsThereMoonHere(List<Celestial> planets, Celestial celestial) {
			Celestial moon = planets.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) celestial.Coordinate.Galaxy)
				.Where(c => c.Coordinate.System == (int) celestial.Coordinate.System)
				.Where(c => c.Coordinate.Position == (int) celestial.Coordinate.Position)
				.Where(c => c.Coordinate.Type == Celestials.Moon)
				.SingleOrDefault() ?? new() { ID = 0 };

			return moon.ID == 0 ? false: true;
		}
	}
}
