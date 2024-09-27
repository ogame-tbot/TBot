using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Services;
using Microsoft.Extensions.Logging;
using System.Threading;
using Tbot.Helpers;
using TBot.Model;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure;
using Tbot.Common.Settings;

namespace Tbot.Workers.Brain {
	public class LifeformsAutoResearchCelestialWorker : CelestialWorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public LifeformsAutoResearchCelestialWorker(ITBotMain parentInstance,
			ITBotWorker parentWorker,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge,
			Celestial celestial) :
			base(parentInstance, parentWorker, celestial) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (
					(bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active
				);
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "LifeformsAutoResearch-" + celestial.ToString();
		}
		public override Feature GetFeature() {
			return Feature.BrainLifeformAutoResearch;
		}

		public override LogSender GetLogSender() {
			return LogSender.LifeformsAutoResearch;
		}

		protected override async Task Execute() {
			try {
				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, "Skipping: Sleep Mode Active!");
					return;
				}

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active)) {
					await LifeformAutoResearchCelestial(celestial);
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

		private async Task LifeformAutoResearchCelestial(Celestial celestial) {
			int fleetId = (int) SendFleetCode.GenericError;
			LFTechno buildable = LFTechno.None;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			bool delayProduction = false;
			long delayTime = 0;
			long interval = 0;
			try {
				DoLog(LogLevel.Information, $"Running Lifeform AutoResearch on {celestial.ToString()}");
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFTechs);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);

				int maxResearchLevel = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxResearchLevel") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxResearchLevel : 1;
				int maxTechs11 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs11") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs11 : maxResearchLevel;
				int maxTechs12 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs12") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs12 : maxResearchLevel;
				int maxTechs13 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs13") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs13 : maxResearchLevel;
				int maxTechs14 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs14") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs14 : maxResearchLevel;
				int maxTechs15 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs15") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs15 : maxResearchLevel;
				int maxTechs16 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs16") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs16 : maxResearchLevel;
				int maxTechs21 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs21") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs21 : maxResearchLevel;
				int maxTechs22 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs22") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs22 : maxResearchLevel;
				int maxTechs23 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs23") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs23 : maxResearchLevel;
				int maxTechs24 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs24") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs24 : maxResearchLevel;
				int maxTechs25 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs25") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs25 : maxResearchLevel;
				int maxTechs26 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs26") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs26 : maxResearchLevel;
				int maxTechs31 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs31") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs31 : maxResearchLevel;
				int maxTechs32 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs32") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs32 : maxResearchLevel;
				int maxTechs33 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs33") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs33 : maxResearchLevel;
				int maxTechs34 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs34") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs34 : maxResearchLevel;
				int maxTechs35 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs35") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs35 : maxResearchLevel;
				int maxTechs36 = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch, "MaxTechs36") ? (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs36 : maxResearchLevel;

				LFTechs maxLFTechs = new();
				maxLFTechs.IntergalacticEnvoys = maxLFTechs.VolcanicBatteries = maxLFTechs.CatalyserTechnology = maxLFTechs.HeatRecovery =  maxTechs11;
				maxLFTechs.HighPerformanceExtractors = maxLFTechs.AcousticScanning = maxLFTechs.PlasmaDrive = maxLFTechs.SulphideProcess = maxTechs12;
				maxLFTechs.FusionDrives = maxLFTechs.HighEnergyPumpSystems = maxLFTechs.EfficiencyModule = maxLFTechs.PsionicNetwork = maxTechs13;
				maxLFTechs.StealthFieldGenerator = maxLFTechs.CargoHoldExpansionCivilianShips = maxLFTechs.DepotAI = maxLFTechs.TelekineticTractorBeam = maxTechs14;
				maxLFTechs.OrbitalDen = maxLFTechs.MagmaPoweredProduction = maxLFTechs.GeneralOverhaulLightFighter = maxLFTechs.EnhancedSensorTechnology = maxTechs15;
				maxLFTechs.ResearchAI = maxLFTechs.GeothermalPowerPlants = maxLFTechs.AutomatedTransportLines = maxLFTechs.NeuromodalCompressor = maxTechs16;
				maxLFTechs.HighPerformanceTerraformer = maxLFTechs.DepthSounding = maxLFTechs.ImprovedDroneAI = maxLFTechs.NeuroInterface = maxTechs21;
				maxLFTechs.EnhancedProductionTechnologies = maxLFTechs.IonCrystalEnhancementHeavyFighter = maxLFTechs.ExperimentalRecyclingTechnology = maxLFTechs.InterplanetaryAnalysisNetwork = maxTechs22;
				maxLFTechs.LightFighterMkII = maxLFTechs.ImprovedStellarator = maxLFTechs.GeneralOverhaulCruiser = maxLFTechs.OverclockingHeavyFighter = maxTechs23;
				maxLFTechs.CruiserMkII = maxLFTechs.HardenedDiamondDrillHeads = maxLFTechs.SlingshotAutopilot = maxLFTechs.TelekineticDrive = maxTechs24;
				maxLFTechs.ImprovedLabTechnology = maxLFTechs.SeismicMiningTechnology = maxLFTechs.HighTemperatureSuperconductors = maxLFTechs.SixthSense = maxTechs25;
				maxLFTechs.PlasmaTerraformer = maxLFTechs.MagmaPoweredPumpSystems = maxLFTechs.GeneralOverhaulBattleship = maxLFTechs.Psychoharmoniser = maxTechs26;
				maxLFTechs.LowTemperatureDrives = maxLFTechs.IonCrystalModules = maxLFTechs.ArtificialSwarmIntelligence = maxLFTechs.EfficientSwarmIntelligence = maxTechs31;
				maxLFTechs.BomberMkII = maxLFTechs.OptimisedSiloConstructionMethod = maxLFTechs.GeneralOverhaulBattlecruiser = maxLFTechs.OverclockingLargeCargo = maxTechs32;
				maxLFTechs.DestroyerMkII = maxLFTechs.DiamondEnergyTransmitter = maxLFTechs.GeneralOverhaulBomber = maxLFTechs.GravitationSensors = maxTechs33;
				maxLFTechs.BattlecruiserMkII = maxLFTechs.ObsidianShieldReinforcement = maxLFTechs.GeneralOverhaulDestroyer = maxLFTechs.OverclockingBattleship = maxTechs34;
				maxLFTechs.RobotAssistants = maxLFTechs.RuneShields = maxLFTechs.ExperimentalWeaponsTechnology = maxLFTechs.PsionicShieldMatrix = maxTechs35;
				maxLFTechs.Supercomputer = maxLFTechs.RocktalCollectorEnhancement = maxLFTechs.MechanGeneralEnhancement = maxLFTechs.KaeleshDiscovererEnhancement = maxTechs36;
				
				
				if (celestial.Constructions.LFResearchID != 0) {
					DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: there is already a Lifeform research in production.");
					delayProduction = true;
					delayTime = (long) celestial.Constructions.LFResearchCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					return;
				}
				if (celestial is Planet) {
					buildable = _calculationService.GetNextLFTechToBuild(celestial, maxLFTechs);//maxResearchLevel);

					if (buildable != LFTechno.None) {
						level = _calculationService.GetNextLevel(celestial, buildable);
						Resources nextLFTechCost = _calculationService.CalcPrice(buildable, level);
						var isLessCostLFTechToBuild = _calculationService.GetLessExpensiveLFTechToBuild(celestial, nextLFTechCost, maxLFTechs);//maxResearchLevel);
						if (isLessCostLFTechToBuild != LFTechno.None) {
							level = _calculationService.GetNextLevel(celestial, isLessCostLFTechToBuild);
							buildable = isLessCostLFTechToBuild;
						}
						DoLog(LogLevel.Information, $"Best Lifeform Research for {celestial.ToString()}: {buildable.ToString()}");

						Resources xCostBuildable = _calculationService.CalcPrice(buildable, level);

						if (celestial.Resources.IsEnoughFor(xCostBuildable)) {
							DoLog(LogLevel.Information, $"Lifeform Research {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							try {
								await _ogameService.BuildCancelable(celestial, (LFTechno) buildable);
								celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.LFResearchID == (int) buildable) {
									started = true;
									DoLog(LogLevel.Information, "Lifeform Research succesfully started.");
								} else {
									celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFTechs);
									if (celestial.GetLevel(buildable) != level)
										DoLog(LogLevel.Warning, "Unable to start Lifeform Research construction: an unknown error has occurred");
									else {
										started = true;
										DoLog(LogLevel.Information, "Lifeform Research succesfully started.");
									}
								}

							} catch {
								DoLog(LogLevel.Warning, "Unable to start Lifeform Research: a network error has occurred");
							}
						} else {
							DoLog(LogLevel.Information, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.LFBuildingCostResources.ToString()} - Available: {celestial.Resources.LFBuildingCostResources.ToString()}");

							if ((bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Transports.Active && (bool) _tbotInstance.InstanceSettings.Brain.Transports.Active) {
								_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
								if (!_calculationService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets)) {
									Celestial origin;
									if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.CheckMoonOrPlanetFirst && _calculationService.IsThereMoonHere(_tbotInstance.UserData.celestials, celestial)) {
										origin = _tbotInstance.UserData.celestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) celestial.Coordinate.Galaxy)
												.Where(c => c.Coordinate.System == (int) celestial.Coordinate.System)
												.Where(c => c.Coordinate.Position == (int) celestial.Coordinate.Position)
												.Where(c => c.Coordinate.Type == (celestial.Coordinate.Type == Celestials.Planet ? Celestials.Moon : Celestials.Planet))
												.SingleOrDefault() ?? new() { ID = 0 };
									} else {
										origin = _tbotInstance.UserData.celestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
												.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
												.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
												.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
												.SingleOrDefault() ?? new() { ID = 0 };
									}
									fleetId = await _fleetScheduler.HandleMinerTransport(origin, celestial, xCostBuildable, Buildables.Null);
									
									if (fleetId == (int) SendFleetCode.NotEnoughRessources) {
										origin = _tbotInstance.UserData.celestials
											.Unique()
											.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
											.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
											.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
											.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
											.SingleOrDefault() ?? new() { ID = 0 };	
										fleetId = await _fleetScheduler.HandleMinerTransport(origin, celestial, xCostBuildable, Buildables.Null);
									}
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
						DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: nothing to build. All research reached _tbotInstance.InstanceSettings MaxResearchLevel ?");
						stop = true;
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"LifeformAutoResearch Celestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await _tbotOgameBridge.GetDateTime();
				DateTime newTime;
				if (stop) {
					DoLog(LogLevel.Information, $"Stopping Lifeform AutoResearch check for {celestial.ToString()}.");
					await EndExecution();
				} else {
					if (delayProduction) {
						DoLog(LogLevel.Information, $"Delaying...");
						interval = delayTime;
					} else if (delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.CheckIntervalMax);
						}
					} else if (started) {
						interval = ((long) celestial.Constructions.LFResearchCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
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
					DoLog(LogLevel.Information, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}

	}
}
