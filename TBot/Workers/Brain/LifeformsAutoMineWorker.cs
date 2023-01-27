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
	public class LifeformsAutoMineWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;

		public LifeformsAutoMineWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOGameBridge,
			IWorkerFactory workerFactory) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOGameBridge;
			_workerFactory = workerFactory;
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
			return LogSender.LifeformsAutoMine;
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

					List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Exclude, _tbotInstance.UserData.celestials);
					List<Celestial> celestialsToMine = new();
					LFBuildings maxLFBuildings = new();
					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						var cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);

						if ((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.StartFromCrystalMineLvl > (int) cel.Buildings.CrystalMine) {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()} did not reach required CrystalMine level. Skipping..");
							continue;
						}
						int maxTechFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseTechBuilding;
						int maxPopuFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBasePopulationBuilding;
						int maxFoodFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseFoodBuilding;
						bool preventIfMoreExpensiveThanNextMine = (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.PreventIfMoreExpensiveThanNextMine;

						cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
						cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
						var nextLFBuilding = await _calculationService.GetNextLFBuildingToBuild(cel, maxPopuFactory, maxFoodFactory, maxTechFactory, preventIfMoreExpensiveThanNextMine);
						if (nextLFBuilding != LFBuildables.None) {
							var lv = _calculationService.GetNextLevel(celestial, nextLFBuilding);
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Next Mine: {nextLFBuilding.ToString()} lv {lv.ToString()}.");

							celestialsToMine.Add(celestial);
						} else {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: No Next Lifeform building to build found.");
						}
					}

					foreach (Celestial celestial in celestialsToMine) {
						if (celestialsToExclude.Has(celestial)) {
							DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}
						var celestialWorker = _workerFactory.InitializeCelestialWorker(this, Feature.BrainCelestialLifeformAutoMine, _tbotInstance, _tbotOgameBridge, celestial);
						await celestialWorker.StartWorker(new CancellationTokenSource().Token, TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds)));
					}
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
	}
}
