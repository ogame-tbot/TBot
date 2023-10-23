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

namespace Tbot.Workers.Brain {
	public class LifeformsAutoMineCelestialWorker : CelestialWorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;

		public LifeformsAutoMineCelestialWorker(ITBotMain parentInstance,
			ITBotWorker parentWorker,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOGameBridge,
			Celestial celestial) :
			base(parentInstance, parentWorker, celestial) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOGameBridge;
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return (
					(bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active
				);
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "LifeformsAutoMine-" + celestial.ToString();
		}
		public override Feature GetFeature() {
			return Feature.BrainLifeformAutoMine;
		}

		public override LogSender GetLogSender() {
			return LogSender.LifeformsAutoMine;
		}

		protected override async Task Execute() {
			try {
				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, "Skipping: Sleep Mode Active!");
					return;
				}

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active)) {
					await LifeformAutoMineCelestial(celestial);
				} else {
					DoLog(LogLevel.Information, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"Lifeform AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}

		private async Task LifeformAutoMineCelestial(Celestial celestial) {
			int fleetId = (int) SendFleetCode.GenericError;
			LFBuildables buildable = LFBuildables.None;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			bool delayProduction = false;
			bool delayLFResearch = false;
			long delayTime = 0;
			long interval = 0;
			try {
				int maxTechFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseTechBuilding;
				int maxPopuFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBasePopulationBuilding;
				int maxFoodFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseFoodBuilding;
				bool preventIfMoreExpensiveThanNextMine = (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.PreventIfMoreExpensiveThanNextMine;

				DoLog(LogLevel.Information, $"Running Lifeform AutoMine on {celestial.ToString()}");
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);

				if (celestial.Constructions.LFBuildingID != 0 || celestial.Constructions.BuildingID == (int) Buildables.RoboticsFactory || celestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
					DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: there is already a building (LF, robotic or nanite) in production.");
					delayProduction = true;
					if (celestial.Constructions.LFBuildingID != 0) {
						delayTime = (long) celestial.Constructions.LFBuildingCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} else {
						delayTime = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					}
				}
				if (delayTime == 0) {
					if (celestial is Planet) {
						buildable = await _calculationService.GetNextLFBuildingToBuild(celestial, maxPopuFactory, maxFoodFactory, maxTechFactory, preventIfMoreExpensiveThanNextMine);

						if (buildable != LFBuildables.None) {
							level = _calculationService.GetNextLevel(celestial, buildable);
							DoLog(LogLevel.Information, $"Best building for {celestial.ToString()}: {buildable.ToString()}");

							if (
								celestial.Constructions.LFResearchID != 0 &&
								(
									buildable == LFBuildables.ResearchCentre ||
									buildable == LFBuildables.RuneTechnologium ||
									buildable == LFBuildables.RoboticsResearchCentre ||
									buildable == LFBuildables.VortexChamber
								)
							) {
								DoLog(LogLevel.Warning, "Unable to start building construction: a LifeForm Research is already in progress.");
								delayLFResearch = true;
								return;
							}
							Resources xCostBuildable = await _ogameService.GetPrice(buildable, level);

							if (celestial.Resources.IsBuildable(xCostBuildable)) {
								DoLog(LogLevel.Information, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
								try {
									await _ogameService.BuildCancelable(celestial, buildable);
									celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
									if (celestial.Constructions.LFBuildingID == (int) buildable) {
										started = true;
										DoLog(LogLevel.Information, "Building succesfully started.");
									} else {
										celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
										if (celestial.GetLevel(buildable) != level)
											DoLog(LogLevel.Warning, "Unable to start building construction: an unknown error has occurred");
										else {
											started = true;
											DoLog(LogLevel.Information, "Building succesfully started.");
										}
									}

								} catch {
									DoLog(LogLevel.Warning, "Unable to start building construction: a network error has occurred");
								}
							} else {
								DoLog(LogLevel.Information, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.LFBuildingCostResources.ToString()} - Available: {celestial.Resources.LFBuildingCostResources.ToString()}");

								if ((bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Transports.Active && (bool) _tbotInstance.InstanceSettings.Brain.Transports.Active) {
									_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
									if (!_calculationService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets)) {
										Celestial origin = _tbotInstance.UserData.celestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
												.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
												.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
												.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
												.SingleOrDefault() ?? new() { ID = 0 };
										fleetId = await _fleetScheduler.HandleMinerTransport(origin, celestial, xCostBuildable);
										if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										}
										if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											delay = true;
											return;
										}
									} else {
										DoLog(LogLevel.Information, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
									}
								}
							}
						} else {
							DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: nothing to build. Check max Lifeform base building max level in _tbotInstance.InstanceSettings file?");
							stop = true;
						}
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"LifeformAutoMine Celestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await _tbotOgameBridge.GetDateTime();
				DateTime newTime;
				if (stop) {
					DoLog(LogLevel.Information, $"Stopping Lifeform AutoMine check for {celestial.ToString()}.");
					await EndExecution();
				} else {
					if (delayProduction) {
						DoLog(LogLevel.Information, $"Delaying...");
						interval = delayTime;
					} else if (delayLFResearch) {
						DoLog(LogLevel.Information, $"Delaying...");
						try {
							interval = (celestial.Constructions.LFResearchCountdown * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);
						}
					} else if (delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);
						}
					} else if (started) {
						interval = ((long) celestial.Constructions.LFBuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} else if (delayTime > 0) {
						interval = delayTime;
					} else {
						if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							var transportfleet = _tbotInstance.UserData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
							interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} else {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);
						}
					}
					time = await _tbotOgameBridge.GetDateTime();
					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}
	}
}
