using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Common.Settings;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public class ExpeditionsWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public ExpeditionsWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.Expeditions.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "Expeditions";
		}
		public override Feature GetFeature() {
			return Feature.Expeditions;
		}

		public override LogSender GetLogSender() {
			return LogSender.Expeditions;
		}


		protected override async Task Execute() {
			bool stop = false;
			bool delay = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				long interval;
				DateTime time;
				DateTime newTime;

				if ((bool) _tbotInstance.InstanceSettings.Expeditions.Active) {
					_tbotInstance.UserData.researches = await _tbotOgameBridge.UpdateResearches();
					if (_tbotInstance.UserData.researches.Astrophysics == 0) {
						DoLog(LogLevel.Information, "Skipping: Astrophysics not yet researched!");
						time = await _tbotOgameBridge.GetDateTime();
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.AboutHalfAnHour);
						newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
						return;
					}

					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					_tbotInstance.UserData.serverData = await _ogameService.GetServerData();
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
					RankSlotsPriority presentFeature = ExpeditionsRank;
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

					int expsToSend;
					if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "WaitForAllExpeditions") && (bool) _tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions) {
						if (_tbotInstance.UserData.slots.ExpInUse == 0)
							expsToSend = _tbotInstance.UserData.slots.ExpTotal;
						else
							expsToSend = 0;
					} else {
						expsToSend = Math.Min(_tbotInstance.UserData.slots.ExpFree, _tbotInstance.UserData.slots.Free);
					}
					DoLog(LogLevel.Debug, $"Expedition slot free: {expsToSend}");
					if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "WaitForMajorityOfExpeditions") && (bool) _tbotInstance.InstanceSettings.Expeditions.WaitForMajorityOfExpeditions) {
						if ((double) expsToSend < Math.Round((double) _tbotInstance.UserData.slots.ExpTotal / 2D, 0, MidpointRounding.ToZero) + 1D) {
							DoLog(LogLevel.Debug, $"Majority of expedition already in flight, Skipping...");
							expsToSend = 0;
						}
					}
					expsToSend = expsToSend < MaxSlots ? expsToSend : MaxSlots;
					if (expsToSend > 0) {
						if (_tbotInstance.UserData.slots.ExpFree > 0) {
							if (_tbotInstance.UserData.slots.Free > 0) {
								List<Celestial> origins = new();
								if (_tbotInstance.InstanceSettings.Expeditions.Origin.Length > 0) {
									try {
										foreach (var origin in _tbotInstance.InstanceSettings.Expeditions.Origin) {
											Coordinate customOriginCoords = new(
												(int) origin.Galaxy,
												(int) origin.System,
												(int) origin.Position,
												Enum.Parse<Celestials>(origin.Type.ToString())
											);
											Celestial customOrigin = _tbotInstance.UserData.celestials
												.Unique()
												.Single(planet => planet.HasCoords(customOriginCoords));
											customOrigin = await _tbotOgameBridge.UpdatePlanet(customOrigin, UpdateTypes.Ships);
											customOrigin = await _tbotOgameBridge.UpdatePlanet(customOrigin, UpdateTypes.LFBonuses);
											origins.Add(customOrigin);
										}
									} catch (Exception e) {
										DoLog(LogLevel.Debug, $"Exception: {e.Message}");
										DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
										DoLog(LogLevel.Warning, "Unable to parse custom origin");

										_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.Ships);
										_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.LFBonuses);
										origins.Add(_tbotInstance.UserData.celestials
											.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
											.ThenByDescending(planet => _calculationService.CalcFleetCapacity(planet.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, null, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo))
											.First()
										);
									}
								} else {
									_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.Ships);
									_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.LFBonuses);
									origins.Add(_tbotInstance.UserData.celestials
										.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
										.ThenByDescending(planet => _calculationService.CalcFleetCapacity(planet.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, null, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo))
										.First()
									);
								}
								if ((bool) _tbotInstance.InstanceSettings.Expeditions.RandomizeOrder) {
									origins = origins.Shuffle().ToList();
								}

								LFBonuses lfBonuses = origins.First().LFBonuses;
								Dictionary<Celestial, int> originExps = new();
								int quot = (int) Math.Floor((float) expsToSend / (float) origins.Count());								
								foreach (var origin in origins) {
									originExps.Add(origin, quot);
								}
								int rest = (int) Math.Floor((float) expsToSend % (float) origins.Count());
								for (int i = 0; i < rest; i++) {
									originExps[origins[i]]++;
								}
								int delayExpedition = 0;
								foreach (var origin in originExps.Keys) {
									int expsToSendFromThisOrigin = originExps[origin];
									if (expsToSendFromThisOrigin == 0) {
										if (delayExpedition > 0)
											delayExpedition--;
										else
											continue;
									}
									else if (origin.Ships.IsEmpty()) {
										DoLog(LogLevel.Warning, "Unable to send expeditions: no ships available");
										delayExpedition++;
										continue;
									} else {
										Ships fleet;
										if ((bool) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Active) {
											fleet = new(
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.LightFighter,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.HeavyFighter,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Cruiser,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Battleship,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Battlecruiser,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Bomber,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Destroyer,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Deathstar,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.SmallCargo,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.LargeCargo,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.ColonyShip,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Recycler,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.EspionageProbe,
												0,
												0,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Reaper,
												(long) _tbotInstance.InstanceSettings.Expeditions.ManualShips.Ships.Pathfinder
											);
											if (!origin.Ships.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
												DoLog(LogLevel.Warning, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
												delayExpedition++;
												continue;
											}
										} else {
											Buildables primaryShip = Buildables.LargeCargo;
											if (!Enum.TryParse<Buildables>(_tbotInstance.InstanceSettings.Expeditions.PrimaryShip.ToString(), true, out primaryShip)) {
												DoLog(LogLevel.Warning, "Unable to parse PrimaryShip. Falling back to default LargeCargo");
												primaryShip = Buildables.LargeCargo;
											}
											if (primaryShip == Buildables.Null) {
												DoLog(LogLevel.Warning, "Unable to send expeditions: primary ship is Null");
												delayExpedition++;
												continue;
											}

											var availableShips = origin.Ships.GetMovableShips();
											if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "PrimaryToKeep") && (int) _tbotInstance.InstanceSettings.Expeditions.PrimaryToKeep > 0) {
												availableShips.SetAmount(primaryShip, Math.Max(0, availableShips.GetAmount(primaryShip) - (long) _tbotInstance.InstanceSettings.Expeditions.PrimaryToKeep));
											}
											DoLog(LogLevel.Warning, $"Available {primaryShip.ToString()} in origin {origin.ToString()}: {availableShips.GetAmount(primaryShip)} ({_tbotInstance.InstanceSettings.Expeditions.PrimaryToKeep} must be kept at dock)");
											fleet = _calculationService.CalcFullExpeditionShips(availableShips, primaryShip, expsToSendFromThisOrigin, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches, lfBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
											if (fleet.GetAmount(primaryShip) < (long) _tbotInstance.InstanceSettings.Expeditions.MinPrimaryToSend || availableShips.GetAmount(primaryShip) < 1) {
												fleet.SetAmount(primaryShip, (long) _tbotInstance.InstanceSettings.Expeditions.MinPrimaryToSend);
												if (availableShips.GetAmount(primaryShip) < 1) {
													DoLog(LogLevel.Warning, $"Unable to send expeditions: no ships available in origin {origin.ToString()}");
													delayExpedition++;
													continue;
												}
												if (!availableShips.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
													DoLog(LogLevel.Warning, $"Unable to send expeditions: available {primaryShip.ToString()} in origin {origin.ToString()} under set min number of {(long) _tbotInstance.InstanceSettings.Expeditions.MinPrimaryToSend}");
													delayExpedition++;
													continue;
												}
											}
											Buildables secondaryShip = Buildables.Null;
											if (!Enum.TryParse<Buildables>(_tbotInstance.InstanceSettings.Expeditions.SecondaryShip, true, out secondaryShip)) {
												DoLog(LogLevel.Warning, "Unable to parse SecondaryShip. Falling back to default Null");
												secondaryShip = Buildables.Null;
											}
											if (secondaryShip != Buildables.Null) {
												long secondaryToSend = Math.Min(
													(long) Math.Round(
														availableShips.GetAmount(secondaryShip) / (float) expsToSendFromThisOrigin,
												0,
												MidpointRounding.ToZero
												),
													(long) Math.Round(
														fleet.GetAmount(primaryShip) * (float) _tbotInstance.InstanceSettings.Expeditions.SecondaryToPrimaryRatio,
												0,
														MidpointRounding.ToZero
													)
												);
												if (secondaryToSend < (long) _tbotInstance.InstanceSettings.Expeditions.MinSecondaryToSend) {
													DoLog(LogLevel.Warning, $"Unable to send expeditions: available {secondaryShip.ToString()} in origin {origin.ToString()} under set number of {(long) _tbotInstance.InstanceSettings.Expeditions.MinSecondaryToSend}");
													delayExpedition++;
													continue;
												} else {
													fleet.Add(secondaryShip, secondaryToSend);
													if (!availableShips.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
														DoLog(LogLevel.Warning, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
														delayExpedition++;
														continue;
													}
												}
											}
										}

										DoLog(LogLevel.Information, $"{expsToSendFromThisOrigin.ToString()} expeditions with {fleet.ToString()} will be sent from {origin.ToString()}");
										List<int> syslist = new();
										for (int i = 0; i < expsToSendFromThisOrigin; i++) {
											Coordinate destination;
											if ((bool) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Active) {
												var rand = new Random();

												int range = (int) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Range;
												while (expsToSendFromThisOrigin > range * 2)
													range += 1;

												destination = new Coordinate {
													Galaxy = origin.Coordinate.Galaxy,
													System = rand.Next(origin.Coordinate.System - range, origin.Coordinate.System + range + 1),
													Position = 16,
													Type = Celestials.DeepSpace
												};
												destination.System = GeneralHelper.WrapSystem(destination.System);
												while (syslist.Contains(destination.System))
													destination.System = rand.Next(origin.Coordinate.System - range, origin.Coordinate.System + range + 1);
												syslist.Add(destination.System);
											} else {
												destination = new Coordinate {
													Galaxy = origin.Coordinate.Galaxy,
													System = origin.Coordinate.System,
													Position = 16,
													Type = Celestials.DeepSpace
												};
											}
											_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
											Resources payload = new();
											if ((long) _tbotInstance.InstanceSettings.Expeditions.FuelToCarry > 0) {
												payload.Deuterium = (long) _tbotInstance.InstanceSettings.Expeditions.FuelToCarry;
											}
											if (_tbotInstance.UserData.slots.ExpFree > 0) {
												var fleetId = await _fleetScheduler.SendFleet(origin, fleet, destination, Missions.Expedition, Speeds.HundredPercent, payload);

												if (fleetId == (int) SendFleetCode.AfterSleepTime) {
													stop = true;
													return;
												}
												if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
													delay = true;
													return;
												}
												
												var minWaitNextFleet = (int) _tbotInstance.InstanceSettings.Expeditions.MinWaitNextFleet;
												var maxWaitNextFleet = (int) _tbotInstance.InstanceSettings.Expeditions.MaxWaitNextFleet;

												if (minWaitNextFleet < 0)
													minWaitNextFleet = 0;
												if (maxWaitNextFleet < 1)
													maxWaitNextFleet = 1;

												var rndWaitTimeMs = (int) RandomizeHelper.CalcRandomIntervalSecToMs(minWaitNextFleet, maxWaitNextFleet);											

												DoLog(LogLevel.Information, $"Wait {((float) rndWaitTimeMs / 1000).ToString("0.00")}s for next Expedition");
												await Task.Delay(rndWaitTimeMs, _ct);

											} else {
												DoLog(LogLevel.Information, "Unable to send expeditions: no expedition slots available.");
												delay = true;
												return;
											}
										}
									}
								}
							} else {
								DoLog(LogLevel.Warning, "Unable to send expeditions: no fleet slots available");
							}
						} else {
							DoLog(LogLevel.Warning, "Unable to send expeditions: no expeditions slots available");
						}
					}

					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					List<Fleet> orderedFleets = _tbotInstance.UserData.fleets
						.Where(fleet => fleet.Mission == Missions.Expedition)
						.ToList();
					if ((bool) _tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions) {
						orderedFleets = orderedFleets
							.OrderByDescending(fleet => fleet.BackIn)
							.ToList();
					} else {
						orderedFleets = orderedFleets
						.OrderBy(fleet => fleet.BackIn)
							.ToList();
					}

					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					if ((orderedFleets.Count() == 0) || (_tbotInstance.UserData.slots.ExpFree > 0 && (!((bool) _tbotInstance.InstanceSettings.Expeditions.WaitForAllExpeditions) && !((bool) _tbotInstance.InstanceSettings.Expeditions.WaitForMajorityOfExpeditions)))) {
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.AboutFiveMinutes);
					} else {

						var minWaitNextRound = (int) _tbotInstance.InstanceSettings.Expeditions.MinWaitNextRound;
						var maxWaitNextRound = (int) _tbotInstance.InstanceSettings.Expeditions.MaxWaitNextRound;

						if (minWaitNextRound < 0) minWaitNextRound = 0;
						if (maxWaitNextRound < 1) maxWaitNextRound = 1;

						interval = (int) ((1000 * orderedFleets.First().BackIn) + RandomizeHelper.CalcRandomIntervalSecToMs(minWaitNextRound, maxWaitNextRound));

					}
					time = await _tbotOgameBridge.GetDateTime();
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
					await _tbotOgameBridge.CheckCelestials();
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"HandleExpeditions exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				long interval = (long) (RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo));
				var time = await _tbotOgameBridge.GetDateTime();
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
						long interval;
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Expeditions.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Expeditions.CheckIntervalMax);
						}
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
