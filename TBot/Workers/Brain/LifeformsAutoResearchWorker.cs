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

namespace Tbot.Workers.Brain {
	public class LifeformsAutoResearchWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public LifeformsAutoResearchWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge,
			IWorkerFactory workerFactory) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
			_workerFactory = workerFactory;
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
			return "LifeformsAutoResearch";
		}
		public override Feature GetFeature() {
			return Feature.BrainLifeformAutoResearch;
		}

		public override LogSender GetLogSender() {
			return LogSender.LifeformsAutoResearch;
		}

		protected override async Task Execute() {
			try {
				DoLog(LogLevel.Information, "Running Lifeform autoresearch...");

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active)) {
					LFTechs maxLFTechs = new();
					maxLFTechs.IntergalacticEnvoys = maxLFTechs.VolcanicBatteries = maxLFTechs.CatalyserTechnology = maxLFTechs.HeatRecovery = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs11;
					maxLFTechs.HighPerformanceExtractors = maxLFTechs.AcousticScanning = maxLFTechs.PlasmaDrive = maxLFTechs.SulphideProcess = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs12;
					maxLFTechs.FusionDrives = maxLFTechs.HighEnergyPumpSystems = maxLFTechs.EfficiencyModule = maxLFTechs.PsionicNetwork = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs13;
					maxLFTechs.StealthFieldGenerator = maxLFTechs.CargoHoldExpansionCivilianShips = maxLFTechs.DepotAI = maxLFTechs.TelekineticTractorBeam = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs14;
					maxLFTechs.OrbitalDen = maxLFTechs.MagmaPoweredProduction = maxLFTechs.GeneralOverhaulLightFighter = maxLFTechs.EnhancedSensorTechnology = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs15;
					maxLFTechs.ResearchAI = maxLFTechs.GeothermalPowerPlants = maxLFTechs.AutomatedTransportLines = maxLFTechs.NeuromodalCompressor = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs16;
					maxLFTechs.HighPerformanceTerraformer = maxLFTechs.DepthSounding = maxLFTechs.ImprovedDroneAI = maxLFTechs.NeuroInterface = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs21;
					maxLFTechs.EnhancedProductionTechnologies = maxLFTechs.IonCrystalEnhancementHeavyFighter = maxLFTechs.ExperimentalRecyclingTechnology = maxLFTechs.InterplanetaryAnalysisNetwork = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs22;
					maxLFTechs.LightFighterMkII = maxLFTechs.ImprovedStellarator = maxLFTechs.GeneralOverhaulCruiser = maxLFTechs.OverclockingHeavyFighter = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs23;
					maxLFTechs.CruiserMkII = maxLFTechs.HardenedDiamondDrillHeads = maxLFTechs.SlingshotAutopilot = maxLFTechs.TelekineticDrive = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs24;
					maxLFTechs.ImprovedLabTechnology = maxLFTechs.SeismicMiningTechnology = maxLFTechs.HighTemperatureSuperconductors = maxLFTechs.SixthSense = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs25;
					maxLFTechs.PlasmaTerraformer = maxLFTechs.MagmaPoweredPumpSystems = maxLFTechs.GeneralOverhaulBattleship = maxLFTechs.Psychoharmoniser = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs26;
					maxLFTechs.LowTemperatureDrives = maxLFTechs.IonCrystalModules = maxLFTechs.ArtificialSwarmIntelligence = maxLFTechs.EfficientSwarmIntelligence = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs31;
					maxLFTechs.BomberMkII = maxLFTechs.OptimisedSiloConstructionMethod = maxLFTechs.GeneralOverhaulBattlecruiser = maxLFTechs.OverclockingLargeCargo = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs32;
					maxLFTechs.DestroyerMkII = maxLFTechs.DiamondEnergyTransmitter = maxLFTechs.GeneralOverhaulBomber = maxLFTechs.GravitationSensors = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs33;
					maxLFTechs.BattlecruiserMkII = maxLFTechs.ObsidianShieldReinforcement = maxLFTechs.GeneralOverhaulDestroyer = maxLFTechs.OverclockingBattleship = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs34;
					maxLFTechs.RobotAssistants = maxLFTechs.RuneShields = maxLFTechs.ExperimentalWeaponsTechnology = maxLFTechs.PsionicShieldMatrix = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs35;
					maxLFTechs.Supercomputer = maxLFTechs.RocktalCollectorEnhancement = maxLFTechs.MechanGeneralEnhancement = maxLFTechs.KaeleshDiscovererEnhancement = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxTechs36;
					int maxResearchLevel = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxResearchLevel;
					
					List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Exclude, _tbotInstance.UserData.celestials);
					List<Celestial> celestialsToMine = new();

					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						var cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
						cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFTechs);
						cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);

						if (cel.LFtype == LFTypes.None) {
							DoLog(LogLevel.Information, $"Skipping {cel.ToString()}: No Lifeform active on this planet.");
							continue;
						}
						var nextLFTechToBuild = _calculationService.GetNextLFTechToBuild(cel, maxLFTechs);//maxResearchLevel);
						if (nextLFTechToBuild != LFTechno.None) {
							var level = _calculationService.GetNextLevel(cel, nextLFTechToBuild);
							Resources nextLFTechCost = _calculationService.CalcPrice(nextLFTechToBuild, level);
							var isLessCostLFTechToBuild = _calculationService.GetLessExpensiveLFTechToBuild(cel, nextLFTechCost, maxLFTechs);//maxResearchLevel);
							if (isLessCostLFTechToBuild != LFTechno.None) {
								level = _calculationService.GetNextLevel(cel, isLessCostLFTechToBuild);
								nextLFTechToBuild = isLessCostLFTechToBuild;
							}

							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Next Lifeform Research: {nextLFTechToBuild.ToString()} lv {level.ToString()}.");
							celestialsToMine.Add(celestial);
						} else {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: No Next Lifeform technology to build found. All research reached _tbotInstance.InstanceSettings MaxResearchLevel ?");
						}

					}
					foreach (Celestial celestial in celestialsToMine) {
						if (celestialsToExclude.Has(celestial)) {
							DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}
						var celestialWorker = _workerFactory.InitializeCelestialWorker(this, Feature.BrainCelestialLifeformAutoResearch, _tbotInstance, _tbotOgameBridge, celestial);
						await celestialWorker.StartWorker(new CancellationTokenSource().Token, TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds)));
					}
				} else {
					DoLog(LogLevel.Information, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"Lifeform AutoResearch Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}
