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
	public class AutoMineWorker : WorkerBase {

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
				Fields fieldsSettings = new() {
					Total = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinFields
				};
				Temperature temperaturesSettings = new() {
					Min = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinTemperatureAcceptable,
					Max = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MaxTemperatureAcceptable
				};

				_tbotInstance.UserData.researches = await _ogameService.GetResearches();
				List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoMine.Exclude, _tbotInstance.UserData.celestials);
				List<Celestial> celestialsToMine = new();
				foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
					var cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
					cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
					cel = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBonuses);					
					Planet abaCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast) as Planet;
					var nextMine = _calculationService.GetNextMineToBuild(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, maxBuildings.MetalMine, maxBuildings.CrystalMine, maxBuildings.DeuteriumSynthesizer, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull, true, int.MaxValue);
					if (nextMine != Buildables.Null) {
						var lv = _calculationService.GetNextLevel(cel, nextMine);
						var DOIR = _calculationService.CalcNextDaysOfInvestmentReturn(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						DoLog(LogLevel.Debug, $"Celestial {cel.ToString()}: Next Mine: {nextMine.ToString()} lv {lv.ToString()}; DOIR: {DOIR.ToString()}.");
						if (DOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
							_tbotInstance.UserData.nextDOIR = DOIR;
						}
					}
					if (celestial.Coordinate.Type == Celestials.Planet && celestial.Fields.Built == 0 && (bool) _tbotInstance.InstanceSettings.AutoColonize.Abandon.Active) {
						if (_calculationService.ShouldAbandon(celestial as Planet, celestial.Fields.Total, abaCelestial.Temperature.Max, fieldsSettings, temperaturesSettings)) {
							DoLog(LogLevel.Debug, $"Skipping {celestial.ToString()}: planet should be abandoned.");
						} else {
							//DoLog(LogLevel.Debug, $"Confirm AutoMine on {celestial.ToString()}.");
							celestialsToMine.Add(cel);
						}
						//DoLog(LogLevel.Debug, $"Because: cases -> {abaCelestial.Fields.Total.ToString()}/{fieldsSettings.Total.ToString()}, MinimumTemp -> {abaCelestial.Temperature.Max.ToString()}>={temperaturesSettings.Min.ToString()}, MaximumTemp -> {abaCelestial.Temperature.Max.ToString()}<={temperaturesSettings.Max.ToString()}");
					} else {
						celestialsToMine.Add(cel);
					}
				}
				celestialsToMine = celestialsToMine.OrderBy(cel => _calculationService.CalcNextDaysOfInvestmentReturn(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull)).ToList();
				celestialsToMine.AddRange(_tbotInstance.UserData.celestials.Where(c => c is Moon));

				foreach (Celestial celestial in (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.RandomOrder ? celestialsToMine.Shuffle().ToList() : celestialsToMine) {
					if (celestialsToExclude.Has(celestial)) {
						DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
						continue;
					}

					var celestialWorker = _workerFactory.InitializeCelestialWorker(this, Feature.BrainCelestialAutoMine, _tbotInstance, _tbotOgameBridge, celestial);
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
