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
	public class ColonizeWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public ColonizeWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;	
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.AutoColonize.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "Colonize";
		}
		public override Feature GetFeature() {
			return Feature.Colonize;
		}

		public override LogSender GetLogSender() {
			return LogSender.Colonize;
		}

		protected override async Task Execute() {
			await _tbotOgameBridge.CheckCelestials();
			bool stop = false;
			bool delay = false;
			Fields fieldsSettings = new() {
				Total = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinFields
			};
			Temperature temperaturesSettings = new() {
				Min = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinTemperatureAcceptable,
				Max = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MaxTemperatureAcceptable
			};
			try {
				if ((bool) _tbotInstance.InstanceSettings.AutoColonize.Abandon.Active) {
					DoLog(LogLevel.Information, "Detecting planet to abandon");

					List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
					var dic = new Dictionary<Coordinate, Celestial>();
				
					foreach (Planet planet in _tbotInstance.UserData.celestials.Where(c => c is Planet)) {
						Planet tempCelestial = await _tbotOgameBridge.UpdatePlanet(planet, UpdateTypes.Fast) as Planet;						
						if (tempCelestial.Coordinate.Type == Celestials.Planet && tempCelestial.Fields.Built == 0) {
							if (_calculationService.ShouldAbandon(tempCelestial as Planet, tempCelestial.Fields.Total, tempCelestial.Temperature.Max, fieldsSettings, temperaturesSettings)) {
								DoLog(LogLevel.Debug, $"This planet should be abandoned: {tempCelestial.ToString()}");
								if (await _ogameService.AbandonCelestial(tempCelestial)) {
									DoLog(LogLevel.Debug, $"Successful Abandon on {tempCelestial.ToString()}.");
								} else {
									DoLog(LogLevel.Debug, $"Failed Abandon on {tempCelestial.ToString()}.");
								}
							} else {
								DoLog(LogLevel.Debug, $"No planet should be abandoned.");
							}
							//DoLog(LogLevel.Debug, $"Because: cases -> {tempCelestial.Fields.Total.ToString()}/{fieldsSettings.Total.ToString()}, MinimumTemp -> {tempCelestial.Temperature.Max.ToString()}>={temperaturesSettings.Min.ToString()}, MaximumTemp -> {tempCelestial.Temperature.Max.ToString()}<={temperaturesSettings.Max.ToString()}");
						}
					}
					await _tbotOgameBridge.CheckCelestials();
					DoLog(LogLevel.Information, "End of planet abandonment");
				}

				if ((bool) _tbotInstance.InstanceSettings.AutoColonize.Active) {
					long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMax);
					_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "Checking if a new planet is needed...");

					_tbotInstance.UserData.researches = await _tbotOgameBridge.UpdateResearches();
					var maxPlanets = _calculationService.CalcMaxPlanets(_tbotInstance.UserData.researches);
					var currentPlanets = _tbotInstance.UserData.celestials.Where(c => c.Coordinate.Type == Celestials.Planet).Count();
					var slotsToLeaveFree = (int) (_tbotInstance.InstanceSettings.AutoColonize.SlotsToLeaveFree ?? 0);
					if (currentPlanets + slotsToLeaveFree < maxPlanets) {
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "A new planet is needed.");

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
						RankSlotsPriority presentFeature = ColonizeRank;
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
						//_tbotInstance.log(LogLevel.Warning, LogSender.Main, $"Main -> {presentFeature.ToString()}");
						foreach (RankSlotsPriority feature in rankSlotsPriority) {
							if (feature == presentFeature)
								continue;
							//_tbotInstance.log(LogLevel.Warning, LogSender.Main, $"{feature.ToString()}");
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
						} else if (MaxSlots > tempsValue) {
							MaxSlots = tempsValue;
							DoLog(LogLevel.Information, $"Less slots available than {presentFeature.Feature.ToString()}, many slots are already used/reserved -> steping back to {MaxSlots} instead of {presentFeature.MaxSlots}");
						}

						if (
							(!(bool) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active && _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Colonize && !f.ReturnFlight) >= maxPlanets - currentPlanets)
							|| ((bool) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active && _tbotInstance.UserData.fleets.Count(f => f.Mission == Missions.Colonize && !f.ReturnFlight) > 0)
						) {
							_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "Colony Ship(s) already in flight.");
							interval = (_tbotInstance.UserData.fleets
								.OrderBy(f => f.ArriveIn)
							.First(f => !f.ReturnFlight)
								.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
						} else {
							Coordinate originCoords = new(
								(int) _tbotInstance.InstanceSettings.AutoColonize.Origin.Galaxy,
								(int) _tbotInstance.InstanceSettings.AutoColonize.Origin.System,
								(int) _tbotInstance.InstanceSettings.AutoColonize.Origin.Position,
								Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.AutoColonize.Origin.Type)
							);
							Celestial origin = _tbotInstance.UserData.celestials.Single(c => c.HasCoords(originCoords));
							origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
							origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);

							var neededColonizers = maxPlanets - currentPlanets - slotsToLeaveFree;

							if (origin.Ships.ColonyShip >= neededColonizers) {
								var minTemp = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinTemperatureAcceptable;
								var maxTemp = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MaxTemperatureAcceptable;
								var minFields = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinFields;
								List <Coordinate> targets = new();
								foreach (var t in _tbotInstance.InstanceSettings.AutoColonize.Targets) {
									var planetsInThisRange = _calculationService.CountPlanetsInRange(_tbotInstance.UserData.celestials, (int) t.Galaxy, (int) t.StartSystem, (int) t.EndSystem, (int) t.StartPosition, (int) t.EndPosition, minFields, minTemp, maxTemp);
									var maxPlanetsInThisRange = (int) t.MaxPlanets;
									if (planetsInThisRange >= maxPlanetsInThisRange) {
										DoLog(LogLevel.Information, $"You already have {planetsInThisRange.ToString()} planets that fit temperature and fields settings in the range [{t.Galaxy}:{t.StartSystem}-{t.EndSystem}:{t.StartPosition}-{t.EndPosition}]. The max number is {maxPlanetsInThisRange}. Skipping...");
										continue;
									}
									for (int i = (int) t.StartSystem; i <= (int) t.EndSystem; i++) {
										for (int ii = (int) t.StartPosition; ii <= (int) t.EndPosition; ii++) {
											Coordinate targetCoords = new(
												(int) t.Galaxy,
												(int) i,
												(int) ii,
												Celestials.Planet
											);
											if (_calculationService.CalcLimitAstro((int) targetCoords.Position, _tbotInstance.UserData.researches)) {
												targets.Add(targetCoords);
											}
										}
									}
								}
								List<Coordinate> filteredTargets = new();
								foreach (Coordinate t in targets) {
									if (_tbotInstance.UserData.celestials.Any(c => c.HasCoords(t))) {
										continue;
									}
									GalaxyInfo galaxy = await _ogameService.GetGalaxyInfo(t);
									if (galaxy.Planets.Any(p => p != null && p.HasCoords(t))) {
										continue;
									}
									filteredTargets.Add(t);
								}
								if (filteredTargets.Count() > 0) {
									if ((bool) _tbotInstance.InstanceSettings.AutoColonize.RandomPosition) {
										List<Coordinate> filteredTargetsRdm = new();
										var random = new Random();
										for (int i = 0; i < filteredTargets.Count; i++) {
											int index = random.Next(filteredTargets.Count);
											filteredTargetsRdm.Add(filteredTargets[index]);
											filteredTargets.RemoveAt(index);
										}
										filteredTargets = (bool) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active
											? filteredTargetsRdm
												.Take(MaxSlots)
												.ToList()
											: filteredTargetsRdm
												.Take(maxPlanets - currentPlanets)
												.ToList();
									} else {
										filteredTargets = (bool) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active
											? filteredTargets
												.OrderBy(t => _calculationService.CalcDistance(origin.Coordinate, t, _tbotInstance.UserData.serverData))
												.Take(MaxSlots)
												.ToList()
											: filteredTargets
												.OrderBy(t => _calculationService.CalcDistance(origin.Coordinate, t, _tbotInstance.UserData.serverData))
												.Take(maxPlanets - currentPlanets)
												.ToList();
									}
									Ships ships = new() { ColonyShip = 1 };
									filteredTargets = filteredTargets
										.OrderBy(t => _calculationService.CalcFleetPrediction(origin, t, ships, Missions.Colonize, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time)
										.ToList();
									int indexList = 0;
									foreach (var target in filteredTargets) {
										indexList++;
										_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
										var colonize = _tbotInstance.UserData.fleets
											.Where(f => f.Mission == Missions.Colonize)
											.Where(f => f.ReturnFlight == false)
											.Where(f => f.Destination.Galaxy == target.Galaxy)
											.Where(f => f.Destination.System == target.System)
											.Where(f => f.Destination.Position == target.Position)
											.Count();
										
										if (colonize > 0) {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Skipping colonize: there is already a colonize incoming in {target.ToString()}");
										} else {
											DoLog(LogLevel.Debug, "Send Colonize.");
											var fleetId = await _fleetScheduler.SendFleet(origin, ships, target, Missions.Colonize, Speeds.HundredPercent);
											_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
											List<Fleet> orderedFleet = _tbotInstance.UserData.fleets
												.Where(fleet => fleet.Mission == Missions.Colonize)
												.ToList();
											orderedFleet = (bool) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.Active
												? orderedFleet
													.OrderBy(fleet => fleet.ArriveIn)
													.ToList()
												: orderedFleet
													.OrderByDescending(fleet => fleet.ArriveIn)
													.ToList();
											if (orderedFleet.Count() > 0) {
												interval = (int) ((1000 * orderedFleet.First().ArriveIn) + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
											}

											if (fleetId == (int) SendFleetCode.AfterSleepTime) {
												stop = true;
												return;
											}
											if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
												delay = true;
												return;
											}
											var minWaitNextFleet = (int) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.MinWaitNextFleet;
											var maxWaitNextFleet = (int) _tbotInstance.InstanceSettings.AutoColonize.IntensiveResearch.MaxWaitNextFleet;
											
											if (minWaitNextFleet < 0)
												minWaitNextFleet = 0;
											if (maxWaitNextFleet < 1)
												maxWaitNextFleet = 1;
											
											var rndWaitTimeMs = 0;	//(int) RandomizeHelper.CalcRandomIntervalSecToMs(minWaitNextFleet, maxWaitNextFleet);
											if (indexList < filteredTargets.Count()) {
												Coordinate nextSlot = filteredTargets.ElementAt(indexList);
												rndWaitTimeMs = _calculationService.CalcFleetPrediction(origin, nextSlot, ships, Missions.Colonize, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time - _calculationService.CalcFleetPrediction(origin, target, ships, Missions.Colonize, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass).Time < maxWaitNextFleet ? 
													(int) RandomizeHelper.CalcRandomIntervalSecToMs(minWaitNextFleet, maxWaitNextFleet) :
													0;
											}

											

											DoLog(LogLevel.Information, $"Wait {((float) rndWaitTimeMs / 1000).ToString("0.00")}s for next Colonization");
											await Task.Delay(rndWaitTimeMs, _ct);
										}
									}
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "No valid coordinate in target list.");
								}
							} else {
								await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Productions);
								await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Facilities);
								if (origin.Productions.Any()) {
									_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed. {origin.Productions.Where(p => p.ID == (int) Buildables.ColonyShip).Sum(p => p.Nbr)} colony ship(s) already in production.");
									foreach (var prod in origin.Productions) {
										if (prod == origin.Productions.First()) {
											interval += (int) _calculationService.CalcProductionTime((Buildables) prod.ID, prod.Nbr - 1, _tbotInstance.UserData.serverData, origin.Facilities) * 1000;
										} else {
											interval += (int) _calculationService.CalcProductionTime((Buildables) prod.ID, prod.Nbr, _tbotInstance.UserData.serverData, origin.Facilities) * 1000;
										}
										if (prod.ID == (int) Buildables.ColonyShip) {
											break;
										}
									}
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed.");
									await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
									var cost = _calculationService.CalcPrice(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip);
									if (origin.Resources.IsEnoughFor(cost)) {
										await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Constructions);
										if (origin.HasConstruction() && (origin.Constructions.BuildingID == (int) Buildables.Shipyard || origin.Constructions.BuildingID == (int) Buildables.NaniteFactory)) {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Unable to build colony ship: {((Buildables) origin.Constructions.BuildingID).ToString()} is in construction");
											interval = (long) origin.Constructions.BuildingCountdown * (long) 1000;
										} else if (origin.HasProduction()) {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Unable to build colony ship: there is already something in production");
											interval = (long) _calculationService.CalcProductionTime((Buildables) origin.Productions.First().ID, origin.Productions.First().Nbr - 1, _tbotInstance.UserData.serverData, origin.Facilities) * 1000;
										} else if (origin.Facilities.Shipyard >= 4 && _tbotInstance.UserData.researches.ImpulseDrive >= 3) {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Building {neededColonizers - origin.Ships.ColonyShip}....");
											await _ogameService.BuildShips(origin, Buildables.ColonyShip, neededColonizers - origin.Ships.ColonyShip);
											interval = (int) _calculationService.CalcProductionTime(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip, _tbotInstance.UserData.serverData, origin.Facilities) * 1000;
										} else {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Requirements to build colony ship not met");
										}
									} else {
										_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Not enough resources to build {neededColonizers} colony ship(s). Needed: {cost.TransportableResources} - Available: {origin.Resources.TransportableResources}");
									}
								}
							}
						}
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "No new planet is needed.");
					}

					DateTime time = await _tbotOgameBridge.GetDateTime();
					if (interval <= 0) {
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					}

					DateTime newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
					await _tbotOgameBridge.CheckCelestials();
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.Colonize, $"HandleColonize exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Colonize, $"Stacktrace: {e.StackTrace}");
				long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMax);
				DateTime time = await _tbotOgameBridge.GetDateTime();
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(interval);
				_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Stopping feature.");
						await EndExecution();
					}
					if (delay) {
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Delaying...");
						var time = await _tbotOgameBridge.GetDateTime();
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval;
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
					}
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}
