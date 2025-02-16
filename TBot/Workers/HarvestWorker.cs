using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Services;
using System.Threading;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using TBot.Model;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure;

namespace Tbot.Workers {
	public class HarvestWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public HarvestWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.AutoHarvest.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "Harvest";
		}
		public override Feature GetFeature() {
			return Feature.Harvest;
		}

		public override LogSender GetLogSender() {
			return LogSender.Harvest;
		}

		protected override async Task Execute() {
			bool stop = false;
			bool delay = false;
			try {

				if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.Active) {
					DoLog(LogLevel.Information, "Detecting harvest targets");

					List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
					var dic = new Dictionary<Coordinate, Celestial>();

					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();

					foreach (Planet planet in _tbotInstance.UserData.celestials.Where(c => c is Planet)) {
						Planet tempCelestial = await _tbotOgameBridge.UpdatePlanet(planet, UpdateTypes.Fast) as Planet;
						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships) as Planet;
						Moon moon = new() {
							Ships = new()
						};

						bool hasMoon = _tbotInstance.UserData.celestials.Count(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) == 1;
						if (hasMoon) {
							moon = _tbotInstance.UserData.celestials.Unique().Single(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) as Moon;
							moon = await _tbotOgameBridge.UpdatePlanet(moon, UpdateTypes.Ships) as Moon;
						}

						if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.HarvestOwnDF) {
							Coordinate dest = new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris);
							if (dic.Keys.Any(d => d.IsSame(dest)))
								continue;
							if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
								continue;
							tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Debris) as Planet;
							if (tempCelestial.Debris != null && tempCelestial.Debris.Resources.TotalResources >= (long) _tbotInstance.InstanceSettings.AutoHarvest.MinimumResourcesOwnDF) {
								if (moon.Ships.Recycler >= tempCelestial.Debris.RecyclersNeeded)
									dic.Add(dest, moon);
								else if (moon.Ships.Recycler > 0)
									dic.Add(dest, moon);
								else if (tempCelestial.Ships.Recycler >= tempCelestial.Debris.RecyclersNeeded)
									dic.Add(dest, tempCelestial);
								else if (tempCelestial.Ships.Recycler > 0)
									dic.Add(dest, tempCelestial);
								else
									DoLog(LogLevel.Information, $"Skipping harvest in {dest.ToString()}: not enough recyclers.");
							}
						}

						if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.HarvestDeepSpace) {
							List<Coordinate> destinations = new List<Coordinate>();
							if ((bool) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Active) {
								int range = (int) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Range;

								for (int i = -range; i <= range + 1; i++) {
									Coordinate destination = new Coordinate {
										Galaxy = tempCelestial.Coordinate.Galaxy,
										System = tempCelestial.Coordinate.System + i,
										Position = 16,
										Type = Celestials.DeepSpace
									};
									destination.System = GeneralHelper.WrapSystem(destination.System);

									destinations.Add(destination);
								}
							} else {
								destinations.Add(new(tempCelestial.Coordinate.Galaxy, tempCelestial.Coordinate.System, 16, Celestials.DeepSpace));
							}

							foreach (Coordinate dest in destinations) {
								if (dic.Keys.Any(d => d.IsSame(dest)))
									continue;
								if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
									continue;
								ExpeditionDebris expoDebris = (await _ogameService.GetGalaxyInfo(dest)).ExpeditionDebris;
								if (expoDebris != null && expoDebris.Resources.TotalResources >= (long) _tbotInstance.InstanceSettings.AutoHarvest.MinimumResourcesDeepSpace) {
									if (moon.Ships.Pathfinder >= expoDebris.PathfindersNeeded)
										dic.Add(dest, moon);
									else if (moon.Ships.Pathfinder > 0)
										dic.Add(dest, moon);
									else if (tempCelestial.Ships.Pathfinder >= expoDebris.PathfindersNeeded)
										dic.Add(dest, tempCelestial);
									else if (tempCelestial.Ships.Pathfinder > 0)
										dic.Add(dest, tempCelestial);
									else
										DoLog(LogLevel.Information, $"Skipping harvest in {dest.ToString()}: not enough pathfinders.");
								}
							}
						}

						newCelestials.Remove(planet);
						newCelestials.Add(tempCelestial);
					}
					_tbotInstance.UserData.celestials = newCelestials;
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					List<RankSlotsPriority> rankSlotsPriority = new();
					RankSlotsPriority BrainRank = new(Feature.BrainAutoMine,
						(int) _tbotInstance.InstanceSettings.Brain.SlotPriorityLevel,
						((bool) _tbotInstance.InstanceSettings.Brain.Active &&
							(bool) _tbotInstance.InstanceSettings.Brain.Transports.Active && 
							((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Active ||
								(bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.Active ||
								(bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active ||
								(bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active)),
						(int) _tbotInstance.InstanceSettings.Brain.Transports.MaxSlots,
						(int) _tbotInstance.UserData.fleets.Where(fleet => fleet.Mission == Missions.Transport).Count());
					RankSlotsPriority ExpeditionsRank = new(Feature.Expeditions,
						(int) _tbotInstance.InstanceSettings.Expeditions.SlotPriorityLevel,
						(bool) _tbotInstance.InstanceSettings.Expeditions.Active,
						(int) _tbotInstance.UserData.slots.ExpTotal,
						(int) _tbotInstance.UserData.fleets.Where(fleet => fleet.Mission == Missions.Expedition).Count());
					RankSlotsPriority AutoFarmRank = new(Feature.AutoFarm,
						(int) _tbotInstance.InstanceSettings.AutoFarm.SlotPriorityLevel,
						(bool) _tbotInstance.InstanceSettings.AutoFarm.Active,
						(int) _tbotInstance.InstanceSettings.AutoFarm.MaxSlots,
						(int) _tbotInstance.UserData.fleets.Where(fleet => fleet.Mission == Missions.Attack).Count());
					RankSlotsPriority ColonizeRank = new(Feature.Colonize,
						(int) _tbotInstance.InstanceSettings.AutoColonize.SlotPriorityLevel,
						(bool) _tbotInstance.InstanceSettings.AutoColonize.Active,
						(bool) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active ?
							(int) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.MaxSlots :
							1,
						(int) _tbotInstance.UserData.fleets.Where(fleet => fleet.Mission == Missions.Colonize).Count());
					RankSlotsPriority AutoDiscoveryRank = new(Feature.AutoDiscovery,
						(int) _tbotInstance.InstanceSettings.AutoDiscovery.SlotPriorityLevel,
						(bool) _tbotInstance.InstanceSettings.AutoDiscovery.Active,
						(int) _tbotInstance.InstanceSettings.AutoDiscovery.MaxSlots,
						(int) _tbotInstance.UserData.fleets.Where(fleet => fleet.Mission == Missions.Discovery).Count());
					RankSlotsPriority presentFeature = new(Feature.Harvest);
					rankSlotsPriority.Add(BrainRank);
					rankSlotsPriority.Add(ExpeditionsRank);
					rankSlotsPriority.Add(AutoFarmRank);
					rankSlotsPriority.Add(ColonizeRank);
					rankSlotsPriority.Add(AutoDiscoveryRank);
					rankSlotsPriority = rankSlotsPriority.OrderBy(r => r.Rank).ToList();
					string msg = "";
					int reservedSlots = 0;
					int MaxSlots = presentFeature.MaxSlots - presentFeature.SlotsUsed;
					int otherSlots = (int) _tbotInstance.UserData.fleets.Where(fleet => (fleet.Mission != Missions.Transport &&
							fleet.Mission != Missions.Expedition &&
							fleet.Mission != Missions.Attack &&
							fleet.Mission != Missions.Spy &&
							fleet.Mission != Missions.Colonize &&
							fleet.Mission != Missions.Discovery)
						).Count();
					_tbotInstance.log(LogLevel.Warning, LogSender.Main, $"Main -> {presentFeature.ToString()}");
					foreach (RankSlotsPriority feature in rankSlotsPriority) {
						if (feature == presentFeature)
							continue;
						_tbotInstance.log(LogLevel.Warning, LogSender.Main, $"{feature.ToString()}");
						if (feature.Active && feature.HasPriorityOn(presentFeature)) {
							msg = $"{msg}, {feature.MaxSlots} are reserved for {feature.Feature.ToString()}";
							reservedSlots += feature.MaxSlots;
						} else {
							otherSlots += feature.SlotsUsed;
						}
					}
					if (otherSlots > 0)
						msg = $"{msg}, {otherSlots} are used for Other";
					int tempsValue = _tbotInstance.UserData.slots.Total - (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree - reservedSlots - otherSlots - presentFeature.SlotsUsed;
					tempsValue = tempsValue < 0 ? 0 : tempsValue;
					DoLog(LogLevel.Information, $"{presentFeature.MaxSlots} slots are reserved for {presentFeature.Feature.ToString()}. Total slots: {_tbotInstance.UserData.slots.Total}. {_tbotInstance.InstanceSettings.General.SlotsToLeaveFree} must remain free{msg}, {tempsValue} are availables");
					if (reservedSlots + otherSlots > _tbotInstance.UserData.slots.Total - (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree) {
						DoLog(LogLevel.Information, $"Unable to send fleet for {presentFeature.Feature.ToString()}, too many slots are already used/reserved");
						MaxSlots = 0;
					} else {
						MaxSlots = tempsValue;
					}

					if (dic.Count() == 0)
						DoLog(LogLevel.Information, "Skipping harvest: there are no fields to harvest.");

					if (dic.Count() > MaxSlots) {
						var pairsToRemove = dic.Take(dic.Count() - MaxSlots);
						foreach (var pair in pairsToRemove) {
							dic.Remove(pair.Key);
						}
					}

					foreach (Coordinate destination in dic.Keys) {
						var fleetId = (int) SendFleetCode.GenericError;
						Celestial origin = dic[destination];
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
						float cargoBonus = 0;
						if (destination.Position == 16) {
							ExpeditionDebris debris = (await _ogameService.GetGalaxyInfo(destination)).ExpeditionDebris;
							cargoBonus = origin.LFBonuses.GetShipCargoBonus(Buildables.Pathfinder);
							long pathfindersToSend = Math.Min(_calculationService.CalcShipNumberForPayload(debris.Resources, Buildables.Pathfinder, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class), origin.Ships.Pathfinder);
							DoLog(LogLevel.Information, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {pathfindersToSend.ToString()} {Buildables.Pathfinder.ToString()}");
							fleetId = await _fleetScheduler.SendFleet(origin, new Ships { Pathfinder = pathfindersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
						} else {
							if (_tbotInstance.UserData.celestials.Any(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet)))) {
								Debris debris = (_tbotInstance.UserData.celestials.Where(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet))).First() as Planet).Debris;
								cargoBonus = origin.LFBonuses.GetShipCargoBonus(Buildables.Recycler);
								long recyclersToSend = Math.Min(_calculationService.CalcShipNumberForPayload(debris.Resources, Buildables.Recycler, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class), origin.Ships.Recycler);
								DoLog(LogLevel.Information, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {recyclersToSend.ToString()} {Buildables.Recycler.ToString()}");
								fleetId = await _fleetScheduler.SendFleet(origin, new Ships { Recycler = recyclersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
							}
						}

						if (fleetId == (int) SendFleetCode.AfterSleepTime) {
							stop = true;
							return;
						}
						if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
							delay = true;
							return;
						}
					}

					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					long interval;
					if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest)) {
						interval = (_tbotInstance.UserData.fleets
							.Where(f => f.Mission == Missions.Harvest)
						.OrderBy(f => f.BackIn)
						.First()
							.BackIn ?? 0) * 1000;
					} else {
						interval = (int) RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMax);
					}
					var time = await _tbotOgameBridge.GetDateTime();
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"HandleHarvest exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				long interval = (int) RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMax);
				var time = await _tbotOgameBridge.GetDateTime();
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(interval);
				DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						DoLog(LogLevel.Information, $"Stopping feature.");
						await EndExecution();
					}
					if (delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						var time = await _tbotOgameBridge.GetDateTime();
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
					}
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}
