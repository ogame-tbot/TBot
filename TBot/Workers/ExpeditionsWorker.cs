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
											origins.Add(customOrigin);
										}
									} catch (Exception e) {
										DoLog(LogLevel.Debug, $"Exception: {e.Message}");
										DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
										DoLog(LogLevel.Warning, "Unable to parse custom origin");

										_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.Ships);
										origins.Add(_tbotInstance.UserData.celestials
											.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
											.ThenByDescending(planet => _calculationService.CalcFleetCapacity(planet.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo))
											.First()
										);
									}
								} else {
									_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.Ships);
									origins.Add(_tbotInstance.UserData.celestials
										.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
										.ThenByDescending(planet => _calculationService.CalcFleetCapacity(planet.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo))
										.First()
									);
								}
								if ((bool) _tbotInstance.InstanceSettings.Expeditions.RandomizeOrder) {
									origins = origins.Shuffle().ToList();
								}
								foreach (var origin in origins) {
									int expsToSendFromThisOrigin;
									if (origins.Count() >= expsToSend) {
										expsToSendFromThisOrigin = 1;
									} else {
										expsToSendFromThisOrigin = (int) Math.Round((float) expsToSend / (float) origins.Count(), MidpointRounding.ToZero);
										//if (origin == origins.Last()) {
										//	expsToSendFromThisOrigin = (int) Math.Round((float) expsToSend / (float) origins.Count(), MidpointRounding.ToZero) + (expsToSend % origins.Count());
										//}
									}
									if (origin.Ships.IsEmpty()) {
										DoLog(LogLevel.Warning, "Unable to send expeditions: no ships available");
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
												continue;
											}

											var availableShips = origin.Ships.GetMovableShips();
											if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Expeditions, "PrimaryToKeep") && (int) _tbotInstance.InstanceSettings.Expeditions.PrimaryToKeep > 0) {
												availableShips.SetAmount(primaryShip, availableShips.GetAmount(primaryShip) - (long) _tbotInstance.InstanceSettings.Expeditions.PrimaryToKeep);
											}
											DoLog(LogLevel.Warning, $"Available {primaryShip.ToString()} in origin {origin.ToString()}: {availableShips.GetAmount(primaryShip)}");
											fleet = _calculationService.CalcFullExpeditionShips(availableShips, primaryShip, expsToSendFromThisOrigin, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
											if (fleet.GetAmount(primaryShip) < (long) _tbotInstance.InstanceSettings.Expeditions.MinPrimaryToSend) {
												fleet.SetAmount(primaryShip, (long) _tbotInstance.InstanceSettings.Expeditions.MinPrimaryToSend);
												if (!availableShips.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
													DoLog(LogLevel.Warning, $"Unable to send expeditions: available {primaryShip.ToString()} in origin {origin.ToString()} under set min number of {(long) _tbotInstance.InstanceSettings.Expeditions.MinPrimaryToSend}");
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
													continue;
												} else {
													fleet.Add(secondaryShip, secondaryToSend);
													if (!availableShips.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
														DoLog(LogLevel.Warning, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
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
												await Task.Delay((int) IntervalType.AFewSeconds, _ct);
											} else {
												DoLog(LogLevel.Information, "Unable to send expeditions: no expedition slots available.");
												break;
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
						interval = (int) ((1000 * orderedFleets.First().BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo));
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
