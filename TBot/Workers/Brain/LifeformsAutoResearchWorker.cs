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
					AutoMinerSettings autoMinerSettings = new() {
						DeutToLeaveOnMoons = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DeutToLeaveOnMoons
					};
					int maxResearchLevel = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxResearchLevel;
					List<Celestial> celestialsToMine = new();
					LFBuildings maxLFBuildings = new();

					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						var cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
						cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFTechs);
						cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);

						if (cel.LFtype == LFTypes.None) {
							DoLog(LogLevel.Information, $"Skipping {cel.ToString()}: No Lifeform active on this planet.");
							continue;
						}
						var nextLFTechToBuild = _calculationService.GetNextLFTechToBuild(cel, maxResearchLevel);
						if (nextLFTechToBuild != LFTechno.None) {
							var level = _calculationService.GetNextLevel(cel, nextLFTechToBuild);
							Resources nextLFTechCost = await _ogameService.GetPrice(nextLFTechToBuild, level);
							var isLessCostLFTechToBuild = await _calculationService.GetLessExpensiveLFTechToBuild(cel, nextLFTechCost, maxResearchLevel);
							if (isLessCostLFTechToBuild != LFTechno.None) {
								level = _calculationService.GetNextLevel(cel, isLessCostLFTechToBuild);
								nextLFTechToBuild = isLessCostLFTechToBuild;
							}

							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Next Lifeform Research: {nextLFTechToBuild.ToString()} lv {level.ToString()}.");
							celestialsToMine.Add(celestial);
						} else {
							DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: No Next Lifeform technoDoLogy to build found. All research reached _tbotInstance.InstanceSettings MaxResearchLevel ?");
						}

					}
					foreach (Celestial celestial in celestialsToMine) {
						var celestialWorker = _workerFactory.InitializeCelestialWorker(this, Feature.BrainCelestialLifeformAutoResearch, _tbotInstance, _tbotOgameBridge, celestial);
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
