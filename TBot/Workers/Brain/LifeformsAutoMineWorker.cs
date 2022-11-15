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

namespace Tbot.Workers.Brain {
	public class LifeformsAutoMineWorker : WorkerBase {
		private readonly IAutoMineWorker _autoMineWorker;
		public LifeformsAutoMineWorker(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService, IAutoMineWorker autoMineWorker) :
			base(parentInstance, fleetScheduler, helpersService) {
			_autoMineWorker = autoMineWorker;
		}

		public LifeformsAutoMineWorker(ITBotMain parentInstance, IAutoMineWorker autoMineWorker) :
			base(parentInstance) {
			_autoMineWorker = autoMineWorker;
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
			return "LifeformsAutoMine";
		}
		public override Feature GetFeature() {
			return Feature.BrainLifeformAutoMine;
		}

		public override LogSender GetLogSender() {
			return LogSender.Brain;
		}

		protected override async Task Execute() {
			try {
				DoLog(LogLevel.Information, "Running Lifeform automine...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, "Skipping: Sleep Mode Active!");
					return;
				}

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active)) {
					AutoMinerSettings autoMinerSettings = new() {
						DeutToLeaveOnMoons = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DeutToLeaveOnMoons
					};

					List<Celestial> celestialsToMine = new();
					LFBuildings maxLFBuildings = new();
					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						var cel = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Buildings);

						if ((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.StartFromCrystalMineLvl > (int) cel.Buildings.CrystalMine) {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()} did not reach required CrystalMine level. Skipping..");
							continue;
						}
						int maxTechFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseTechBuilding;
						int maxPopuFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseFoodBuilding;
						int maxFoodFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBasePopulationBuilding;

						cel = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFBuildings);
						cel = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.ResourcesProduction);
						var nextLFBuilding = await _helpersService.GetNextLFBuildingToBuild(cel, maxPopuFactory, maxFoodFactory, maxTechFactory);
						if (nextLFBuilding != LFBuildables.None) {
							var lv = _helpersService.GetNextLevel(celestial, nextLFBuilding);
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Next Mine: {nextLFBuilding.ToString()} lv {lv.ToString()}.");

							celestialsToMine.Add(celestial);
						} else {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: No Next Lifeform building to build found.");
						}
					}

					foreach (Celestial celestial in celestialsToMine) {
						await LifeformAutoMineCelestial(celestial);
					}
				} else {
					DoLog(LogLevel.Information, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"Lifeform AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await TBotOgamedBridge.CheckCelestials(_tbotInstance);
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
			long delayTime = 0;
			long interval = 0;
			try {
				int maxTechFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseTechBuilding;
				int maxPopuFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseFoodBuilding;
				int maxFoodFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBasePopulationBuilding;

				DoLog(LogLevel.Information, $"Running Lifeform AutoMine on {celestial.ToString()}");
				celestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Resources);
				celestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.ResourcesProduction);
				celestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFBuildings);
				celestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Buildings);
				celestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);

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
						buildable = await _helpersService.GetNextLFBuildingToBuild(celestial, maxPopuFactory, maxFoodFactory, maxTechFactory);

						if (buildable != LFBuildables.None) {
							level = _helpersService.GetNextLevel(celestial, buildable);
							DoLog(LogLevel.Information, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
							Resources xCostBuildable = await _tbotInstance.OgamedInstance.GetPrice(buildable, level);

							if (celestial.Resources.IsBuildable(xCostBuildable)) {
								DoLog(LogLevel.Information, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
								try {
									await _tbotInstance.OgamedInstance.BuildCancelable(celestial, buildable);
									celestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);
									if (celestial.Constructions.LFBuildingID == (int) buildable) {
										started = true;
										DoLog(LogLevel.Information, "Building succesfully started.");
									} else {
										celestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFBuildings);
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

								if ((bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Transports.Active) {
									_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
									if (!_helpersService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets)) {
										Celestial origin = _tbotInstance.UserData.celestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Galaxy)
												.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.System)
												.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Position)
												.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Type))
												.SingleOrDefault() ?? new() { ID = 0 };
										fleetId = await _autoMineWorker.HandleMinerTransport(origin, celestial, xCostBuildable);
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
				var time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
				string autoMineTimer = $"LifeformAutoMine-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					DoLog(LogLevel.Information, $"Stopping Lifeform AutoMine check for {celestial.ToString()}.");
					if (timers.TryGetValue($"LifeformAutoMine-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					DoLog(LogLevel.Information, $"Delaying...");
					time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					newTime = time.AddMilliseconds(delayTime);
					ChangeWorkerPeriod(delayTime);
					DoLog(LogLevel.Information, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					DoLog(LogLevel.Information, $"Delaying...");
					time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					try {
						interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (started) {
					interval = ((long) celestial.Constructions.LFBuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();

					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (delayTime > 0) {
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(delayTime);
					ChangeWorkerPeriod(delayTime);
					DoLog(LogLevel.Information, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else {
					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var transportfleet = _tbotInstance.UserData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} else {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);
					}

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();

					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}
	}
}
