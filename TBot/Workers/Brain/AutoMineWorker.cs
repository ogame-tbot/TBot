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
using static Microsoft.AspNetCore.Razor.Language.TagHelperMetadata;

namespace Tbot.Workers.Brain {
	public class AutoMineWorker : WorkerBase, IAutoMineWorker {

		private readonly ICalculationService _calculationService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public AutoMineWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge,
			IWorkerFactory workerFactory) :
			base(parentInstance) {
			_calculationService = calculationService;
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
			_workerFactory = workerFactory;
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return ((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Active);
			} catch (Exception) {
				return false;
			}
		}

		public override string GetWorkerName() {
			return "AutoMine";
		}
		public override Feature GetFeature() {
			return Feature.BrainAutoMine;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoMine;
		}

		public async Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Resources resources, Buildables buildable = Buildables.Null, Buildings maxBuildings = null, Facilities maxFacilities = null, Facilities maxLunarFacilities = null, AutoMinerSettings autoMinerSettings = null) {
			try {
				if (origin.ID == destination.ID) {
					DoLog(LogLevel.Warning, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					DoLog(LogLevel.Warning, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = resources.Difference(destination.Resources);
					Resources resToLeave = new(0, 0, 0);
					if ((long) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.DeutToLeave > 0)
						resToLeave.Deuterium = (long) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.DeutToLeave;

					origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.CargoType, true, out preferredShip)) {
							DoLog(LogLevel.Warning, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}

						long idealShips = _calculationService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						Ships ships = new();
						Ships tempShips = new();
						tempShips.Add(preferredShip, 1);
						var flightPrediction = _calculationService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, tempShips, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class);
						long flightTime = flightPrediction.Time;
						idealShips = _calculationService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						var availableShips = origin.Ships.GetAmount(preferredShip);
						if (buildable != Buildables.Null) {
							int level = _calculationService.GetNextLevel(destination, buildable);
							long buildTime = _calculationService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, destination.Facilities);
							if (maxBuildings != null && maxFacilities != null && maxLunarFacilities != null && autoMinerSettings != null) {
								var tempCelestial = destination;
								while (flightTime * 2 >= buildTime && idealShips <= availableShips) {
									tempCelestial.SetLevel(buildable, level);
									if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler && buildable != Buildables.SpaceDock) {
										tempCelestial.Fields.Built += 1;
									}
									var nextBuildable = Buildables.Null;
									if (tempCelestial.Coordinate.Type == Celestials.Planet) {
										tempCelestial.Resources.Energy += _calculationService.GetProductionEnergyDelta(buildable, level, _tbotInstance.UserData.researches.EnergyTechnology, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
										tempCelestial.ResourcesProduction.Energy.Available += _calculationService.GetProductionEnergyDelta(buildable, level, _tbotInstance.UserData.researches.EnergyTechnology, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
										tempCelestial.Resources.Energy -= _calculationService.GetRequiredEnergyDelta(buildable, level);
										tempCelestial.ResourcesProduction.Energy.Available -= _calculationService.GetRequiredEnergyDelta(buildable, level);
										nextBuildable = _calculationService.GetNextBuildingToBuild(tempCelestial as Planet, _tbotInstance.UserData.researches, maxBuildings, maxFacilities, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.serverData, autoMinerSettings, 1);
									} else {
										nextBuildable = _calculationService.GetNextLunarFacilityToBuild(tempCelestial as Moon, _tbotInstance.UserData.researches, maxLunarFacilities);
									}
									if ((nextBuildable != Buildables.Null) && (buildable != Buildables.SolarSatellite)) {
										var nextLevel = _calculationService.GetNextLevel(tempCelestial, nextBuildable);
										var newMissingRes = missingResources.Sum(_calculationService.CalcPrice(nextBuildable, nextLevel));

										if (origin.Resources.IsEnoughFor(newMissingRes, resToLeave)) {
											var newIdealShips = _calculationService.CalcShipNumberForPayload(newMissingRes, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
											if (newIdealShips <= origin.Ships.GetAmount(preferredShip)) {
												idealShips = newIdealShips;
												missingResources = newMissingRes;
												buildTime += _calculationService.CalcProductionTime(nextBuildable, nextLevel, _tbotInstance.UserData.serverData, tempCelestial.Facilities);
												DoLog(LogLevel.Information, $"Sending resources for {nextBuildable.ToString()} level {nextLevel} too");
												level = nextLevel;
												buildable = nextBuildable;
											} else {
												break;
											}
										} else {
											break;
										}
									} else {
										break;
									}
								}
							}
						}

						if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine.Transports, "RoundResources") && (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.RoundResources) {
							missingResources = missingResources.Round();
							idealShips = _calculationService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						}

						if (idealShips <= origin.Ships.GetAmount(preferredShip)) {
							ships.Add(preferredShip, idealShips);

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourceSettings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Buildings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourcesProduction);

								float metProdInASecond = destination.ResourcesProduction.Metal.CurrentProduction / (float) 3600;
								float cryProdInASecond = destination.ResourcesProduction.Crystal.CurrentProduction / (float) 3600;
								float deutProdInASecond = destination.ResourcesProduction.Deuterium.CurrentProduction / (float) 3600;
								var metProdInFlightTime = metProdInASecond * flightTime;
								var cryProdInFlightTime = cryProdInASecond * flightTime;
								var deutProdInFlightTime = deutProdInASecond * flightTime;

								if (
									(metProdInASecond == 0 && missingResources.Metal > 0) ||
									(cryProdInFlightTime == 0 && missingResources.Crystal > 0) ||
									(deutProdInFlightTime == 0 && missingResources.Deuterium > 0) ||
									missingResources.Metal >= metProdInFlightTime ||
									missingResources.Crystal >= cryProdInFlightTime ||
									missingResources.Deuterium >= deutProdInFlightTime ||
									resources.Metal > destination.ResourcesProduction.Metal.StorageCapacity ||
									resources.Crystal > destination.ResourcesProduction.Crystal.StorageCapacity ||
									resources.Deuterium > destination.ResourcesProduction.Deuterium.StorageCapacity
								) {
									DoLog(LogLevel.Information, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
									return await _fleetScheduler.SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
								} else {
									DoLog(LogLevel.Information, "Skipping transport: it is quicker to wait for production.");
									return 0;
								}
							} else {
								DoLog(LogLevel.Information, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
								return await _fleetScheduler.SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
							}
						} else {
							DoLog(LogLevel.Information, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						DoLog(LogLevel.Information, $"Skipping transport: not enough resources in origin. Needed: {missingResources.TransportableResources} - Available: {origin.Resources.TransportableResources}");
						return 0;
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"HandleMinerTransport Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}


		protected override async Task Execute() {
			try {
				DoLog(LogLevel.Information, "Running automine...");

				Buildings maxBuildings = new() {
					MetalMine = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxMetalMine,
					CrystalMine = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxCrystalMine,
					DeuteriumSynthesizer = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxDeuteriumSynthetizer,
					SolarPlant = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxSolarPlant,
					FusionReactor = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxFusionReactor,
					MetalStorage = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxMetalStorage,
					CrystalStorage = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxCrystalStorage,
					DeuteriumTank = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxDeuteriumTank
				};
				Facilities maxFacilities = new() {
					RoboticsFactory = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxRoboticsFactory,
					Shipyard = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxShipyard,
					ResearchLab = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxResearchLab,
					MissileSilo = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxMissileSilo,
					NaniteFactory = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxNaniteFactory,
					Terraformer = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxTerraformer,
					SpaceDock = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxSpaceDock
				};
				Facilities maxLunarFacilities = new() {
					LunarBase = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxLunarBase,
					RoboticsFactory = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxLunarRoboticsFactory,
					SensorPhalanx = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxSensorPhalanx,
					JumpGate = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxJumpGate,
					Shipyard = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxLunarShipyard
				};
				AutoMinerSettings autoMinerSettings = new() {
					OptimizeForStart = (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.OptimizeForStart,
					PrioritizeRobotsAndNanites = (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.PrioritizeRobotsAndNanites,
					MaxDaysOfInvestmentReturn = (float) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxDaysOfInvestmentReturn,
					DepositHours = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DepositHours,
					BuildDepositIfFull = (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildDepositIfFull,
					DeutToLeaveOnMoons = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DeutToLeaveOnMoons
				};

				List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoMine.Exclude, _tbotInstance.UserData.celestials);
				List<Celestial> celestialsToMine = new();
				foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
					var cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
					var nextMine = _calculationService.GetNextMineToBuild(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 100, 100, 100, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull, true, int.MaxValue);
					var lv = _calculationService.GetNextLevel(cel, nextMine);
					var DOIR = _calculationService.CalcNextDaysOfInvestmentReturn(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
					DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Next Mine: {nextMine.ToString()} lv {lv.ToString()}; DOIR: {DOIR.ToString()}.");
					if (DOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
						_tbotInstance.UserData.nextDOIR = DOIR;
					}
					celestialsToMine.Add(cel);
				}
				celestialsToMine = celestialsToMine.OrderBy(cel => _calculationService.CalcNextDaysOfInvestmentReturn(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull)).ToList();
				celestialsToMine.AddRange(_tbotInstance.UserData.celestials.Where(c => c is Moon));

				foreach (Celestial celestial in (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.RandomOrder ? celestialsToMine.Shuffle().ToList() : celestialsToMine) {
					if (celestialsToExclude.Has(celestial)) {
						DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
						continue;
					}

					var celestialWorker = _workerFactory.InitializeCelestialWorker(Feature.BrainCelestialAutoMine, _tbotInstance, _tbotOgameBridge, celestial);
					await celestialWorker.StartWorker(new CancellationTokenSource().Token, TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds)));

				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}
