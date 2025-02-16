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

namespace Tbot.Workers.Brain {
	public class AutoMineCelestialWorker : CelestialWorkerBase {

		private readonly ICalculationService _calculationService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public AutoMineCelestialWorker(ITBotMain parentInstance,
			ITBotWorker parentWorker,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge,
			Celestial celestial) :
			base(parentInstance, parentWorker, celestial) {
			_calculationService = calculationService;
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return ((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Active);
			} catch (Exception) {
				return false;
			}
		}

		public override string GetWorkerName() {
			return "AutoMine-" + celestial.ToString();
		}
		public override Feature GetFeature() {
			return Feature.BrainAutoMine;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoMine;
		}

		protected override async Task Execute() {
			try {
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
					DeutToLeaveOnMoons = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DeutToLeaveOnMoons,
					BuildSolarSatellites = (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildSolarSatellites
				};
				Fields fieldsSettings = new() {
					Total = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinFields
				};
				Temperature temperaturesSettings = new() {
					Min = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MinTemperatureAcceptable,
					Max = (int) _tbotInstance.InstanceSettings.AutoColonize.Abandon.MaxTemperatureAcceptable
				};
				
				await AutoMineCelestial(celestial, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings, fieldsSettings, temperaturesSettings);
				
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
		private async Task AutoMineCelestial(Celestial celestial, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings, Fields fieldsSettings, Temperature temperaturesSettings) {
			int fleetId = (int) SendFleetCode.GenericError;
			Buildables buildable = Buildables.Null;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			long delayBuilding = 0;
			bool delayProduction = false;
			try {
				DoLog(LogLevel.Information, $"Running AutoMine on {celestial.ToString()}");
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourceSettings);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Productions);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBonuses);
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.LFBuildings);
				Planet abaCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast) as Planet;
				if (
					(!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine, "BuildCrawlers") || (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildCrawlers) &&
					celestial.Coordinate.Type == Celestials.Planet &&
					celestial.Resources.Energy >= 0 &&
					_tbotInstance.UserData.userInfo.Class == CharacterClass.Collector &&
					celestial.Facilities.Shipyard >= 5 &&
					_tbotInstance.UserData.researches.CombustionDrive >= 4 &&
					_tbotInstance.UserData.researches.ArmourTechnology >= 4 &&
					_tbotInstance.UserData.researches.LaserTechnology >= 4 &&
					!celestial.Productions.Any(p => p.ID == (int) Buildables.Crawler) &&
					celestial.Constructions.BuildingID != (int) Buildables.Shipyard &&
					celestial.Constructions.BuildingID != (int) Buildables.NaniteFactory &&
					celestial.Ships.Crawler < _calculationService.CalcMaxCrawlers(celestial as Planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist) &&
					_calculationService.CalcOptimalCrawlers(celestial as Planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData) > celestial.Ships.Crawler
				) {
					buildable = Buildables.Crawler;
					level = _calculationService.CalcOptimalCrawlers(celestial as Planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData);
				} else {
					if (celestial.Fields.Free == 0) {
						DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: not enough fields available.");
						return;
					}
					if (_tbotInstance.UserData.celestials.Count > 1 && celestial.Coordinate.Type == Celestials.Planet && celestial.Fields.Built == 0 && (bool) _tbotInstance.InstanceSettings.AutoColonize.Abandon.Active) {
						if (_calculationService.ShouldAbandon(celestial as Planet, celestial.Fields.Total, abaCelestial.Temperature.Max, fieldsSettings, temperaturesSettings)) {
							DoLog(LogLevel.Debug, $"Skipping {celestial.ToString()}: planet should be abandoned.");
							//DoLog(LogLevel.Debug, $"Because: cases -> {abaCelestial.Fields.Total.ToString()}/{fieldsSettings.Total.ToString()}, MinimumTemp -> {abaCelestial.Temperature.Max.ToString()}>={temperaturesSettings.Min.ToString()}, MaximumTemp -> {abaCelestial.Temperature.Max.ToString()}<={temperaturesSettings.Max.ToString()}");
							return;
						}/* else {
							DoLog(LogLevel.Debug, $"Confirm AutoMine on {celestial.ToString()}.");
							DoLog(LogLevel.Debug, $"Because: cases -> {abaCelestial.Fields.Total.ToString()}/{fieldsSettings.Total.ToString()}, MinimumTemp -> {abaCelestial.Temperature.Max.ToString()}>={temperaturesSettings.Min.ToString()}, MaximumTemp -> {abaCelestial.Temperature.Max.ToString()}<={temperaturesSettings.Max.ToString()}");
						}*/
					}
					if (celestial.Constructions.BuildingID != 0) {
						DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: there is already a building in production.");
						if (
							celestial is Planet && (
								celestial.Constructions.BuildingID == (int) Buildables.MetalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.CrystalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.DeuteriumSynthesizer
							)
						) {
							var buildingBeingBuilt = (Buildables) celestial.Constructions.BuildingID;

							var levelBeingBuilt = _calculationService.GetNextLevel(celestial, buildingBeingBuilt);
							var DOIR = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildingBeingBuilt, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
							if (DOIR > _tbotInstance.UserData.lastDOIR) {
								_tbotInstance.UserData.lastDOIR = DOIR;
							}
						}
						delayBuilding = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						return;
					}

					if (celestial is Planet) {
						buildable = _calculationService.GetNextBuildingToBuild(celestial as Planet, _tbotInstance.UserData.researches, maxBuildings, maxFacilities, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.serverData, autoMinerSettings);
						level = _calculationService.GetNextLevel(celestial as Planet, buildable, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
					} else {
						buildable = _calculationService.GetNextLunarFacilityToBuild(celestial as Moon, _tbotInstance.UserData.researches, maxLunarFacilities);
						level = _calculationService.GetNextLevel(celestial as Moon, buildable, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
					}
				}

				if (buildable != Buildables.Null && level > 0) {
					DoLog(LogLevel.Information, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
					if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
						float DOIR = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						DoLog(LogLevel.Debug, $"Days of investment return: {Math.Round(DOIR, 2).ToString()} days.");
					}

					Resources xCostBuildable = _calculationService.CalcPrice(buildable, level);
					if (celestial is Moon)
						xCostBuildable.Deuterium += (long) autoMinerSettings.DeutToLeaveOnMoons;

					if (buildable == Buildables.Terraformer) {
						if (xCostBuildable.Energy > celestial.ResourcesProduction.Energy.CurrentProduction) {
							DoLog(LogLevel.Information, $"Not enough energy to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							if ((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildSolarSatellites) {
								buildable = Buildables.SolarSatellite;
								level = _calculationService.CalcNeededSolarSatellites(celestial as Planet, xCostBuildable.Energy - celestial.ResourcesProduction.Energy.CurrentProduction, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
								xCostBuildable = _calculationService.CalcPrice(buildable, level);
							}
							else {
								DoLog(LogLevel.Information, $"Unable to build SolarSatellites for Terraformer. Stopping AutoMiner for celestial {celestial.ToString()}");
								stop = true;
								return;
							}
						}
					}

					if (celestial.Resources.IsEnoughFor(xCostBuildable) && buildable != Buildables.Null) {
						bool result = false;
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							if (!celestial.HasProduction()) {
								DoLog(LogLevel.Information, $"Building {level.ToString()} x {buildable.ToString()} on {celestial.ToString()}");
								try {
									await _ogameService.BuildShips(celestial, buildable, level);
									result = true;
								} catch { }
							} else {
								DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: There is already a production ongoing.");
								delayProduction = true;
							}
						} else {
							DoLog(LogLevel.Information, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							try {
								await _ogameService.BuildConstruction(celestial, buildable);
								result = true;
							} catch { }
						}

						if (result) {
							if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
								float DOIR = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
								if (DOIR > _tbotInstance.UserData.lastDOIR) {
									_tbotInstance.UserData.lastDOIR = DOIR;
								}
							}
							if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
								celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Productions);
								try {
									if (celestial.Productions.First().ID == (int) buildable) {
										started = true;
										DoLog(LogLevel.Information, $"{celestial.Productions.First().Nbr.ToString()}x {buildable.ToString()} succesfully started.");
									} else {
										celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
										if (celestial.Resources.Energy >= 0) {
											started = true;
											DoLog(LogLevel.Information, $"{level.ToString()}x {buildable.ToString()} succesfully built");
										} else {
											DoLog(LogLevel.Warning, $"Unable to start {level.ToString()}x {buildable.ToString()} construction: an unknown error has occurred");
										}
									}
								} catch {
									started = true;
									DoLog(LogLevel.Information, $"Unable to determine if the production has started.");
								}
							} else {
								celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.BuildingID == (int) buildable) {
									started = true;
									DoLog(LogLevel.Information, "Building succesfully started.");
								} else {
									celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
									celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
									if (celestial.GetLevel(buildable) != level)
										DoLog(LogLevel.Warning, "Unable to start building construction: an unknown error has occurred");
									else {
										started = true;
										DoLog(LogLevel.Information, "Building succesfully started.");
									}
								}
							}
						} else if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler)
							DoLog(LogLevel.Warning, "Unable to start building construction: a network error has occurred");
					} else {
						if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
							float DOIR = _calculationService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
							if (DOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
								_tbotInstance.UserData.nextDOIR = DOIR;
							}
						}
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							DoLog(LogLevel.Information, $"Not enough resources to build: {level.ToString()}x {buildable.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");

						} else {
							DoLog(LogLevel.Information, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
						}
						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Active && (bool) _tbotInstance.InstanceSettings.Brain.Transports.Active) {
							_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
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
							RankSlotsPriority presentFeature = BrainRank;
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
							if (reservedSlots + otherSlots + presentFeature.SlotsUsed > _tbotInstance.UserData.slots.Total - (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree) {
								DoLog(LogLevel.Information, $"Unable to send fleet for {presentFeature.Feature.ToString()}, too many slots are already used/reserved");
								MaxSlots = 0;
							} else if (MaxSlots > tempsValue) {
								MaxSlots = tempsValue;
								DoLog(LogLevel.Information, $"Less slots available than {presentFeature.Feature.ToString()}, many slots are already used/reserved -> steping back to {MaxSlots} instead of {presentFeature.MaxSlots}");
							}

							if (MaxSlots > 0) {
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
												fleetId = await _fleetScheduler.HandleMinerTransport(origin, celestial, xCostBuildable, buildable, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
										if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
										}
										if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											delay = true;
										}
									}

									if (fleetId <= 0) {
										if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.Active) {
											var resultOrigins = new Dictionary<Celestial, Resources>();
											List<Celestial> allCelestials = _tbotInstance.UserData.celestials;
											List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.Exclude, allCelestials);
											for (int i = 0; i < allCelestials.Count(); i++) {
												allCelestials[i] = await _tbotOgameBridge.UpdatePlanet(allCelestials[i], UpdateTypes.Resources);
												allCelestials[i] = await _tbotOgameBridge.UpdatePlanet(allCelestials[i], UpdateTypes.Ships);
											}
											List<Celestial> closestCelestials = (bool) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.OnlyFromMoons ?
												allCelestials
													.Where(planet => !celestialsToExclude.Has(planet))
													.Where(planet => planet.Resources.TotalResources > 0)
													.Where(planet => planet.Resources.Deuterium > (long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeave)
													.Where(planet => planet.Coordinate.Type == Celestials.Moon)
													.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
													.ToList():
												allCelestials
													.Where(c => !celestialsToExclude.Has(c))
													.Where(c => c.Resources.TotalResources > 0)
													.Where(c => c.Resources.Deuterium > (long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeave)
													.ToList();

											closestCelestials = (bool) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.PriorityToProximityOverQuantity ?
												closestCelestials.OrderBy(c => _calculationService.CalcDistance(c.Coordinate, celestial.Coordinate, _tbotInstance.UserData.serverData)).ToList() :
												closestCelestials.OrderByDescending(c => c.Resources.TotalResources).ToList();

											Resources missingResources = xCostBuildable.Difference(celestial.Resources);
											Resources resourcesTotalAvailable = new();
											Resources possibleResources = new();

											Celestial destination;
											if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.SendToTheMoonIfPossible && celestial.Coordinate.Type == Celestials.Planet && _calculationService.IsThereMoonHere(allCelestials, celestial) && (!celestial.Ships.IsEmpty() || celestial.Resources.TotalResources > 0)) {
												destination = allCelestials
													.Where(planet => planet.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
													.Where(planet => planet.Coordinate.System == celestial.Coordinate.System)
													.Where(planet => planet.Coordinate.Position == celestial.Coordinate.Position)
													.Where(planet => planet.Coordinate.Type == Celestials.Moon)
													.First();
												missingResources = xCostBuildable.Difference(destination.Resources);
											} else {
												destination = allCelestials
													.Where(planet => planet.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
													.Where(planet => planet.Coordinate.System == celestial.Coordinate.System)
													.Where(planet => planet.Coordinate.Position == celestial.Coordinate.Position)
													.Where(planet => planet.Coordinate.Type == celestial.Coordinate.Type)
													.First();
											}
											closestCelestials = closestCelestials.Where(c => !c.Coordinate.IsSame(destination.Coordinate)).ToList();
											if (_calculationService.IsThereTransportTowardsCelestial(destination, _tbotInstance.UserData.fleets)) {
												DoLog(LogLevel.Information, $"Skipping transport: there is already a transport incoming in {destination.ToString()}");
												return;
											}

											Buildables preferredShip = Buildables.SmallCargo;
											if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.Transports.CargoType, true, out preferredShip)) {
												_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to parse CargoType. Falling back to default SmallCargo");
												preferredShip = Buildables.SmallCargo;
											}
											long idealShips;

											foreach (var possibleOrigin in closestCelestials) {
												//if (!celestialsToExclude.Has(possibleOrigin))			// if .Where(planet => !celestialsToExclude.Has(planet)) don't work
												if (!_calculationService.IsThereTransportTowardsCelestial(possibleOrigin, _tbotInstance.UserData.fleets))
													possibleResources = possibleResources.Sum(possibleOrigin.Resources);
											}
											if (!possibleResources.IsEnoughFor(xCostBuildable)) {
												possibleResources = possibleResources.Sum(celestial.Resources);
												DoLog(LogLevel.Information, $"Not enough resources available on all celestials to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {possibleResources.TransportableResources}");
												return;
											}
											
											Celestial moonDestination = new();
											foreach (var possibleOrigin in closestCelestials) {
												possibleResources = new();
												Celestial tempPossibleOrigin = possibleOrigin;
												tempPossibleOrigin.Resources.Deuterium = (tempPossibleOrigin.Resources.Deuterium - (long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeave) > 0 ? tempPossibleOrigin.Resources.Deuterium - (long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeave : 0;
												if (tempPossibleOrigin.Resources.Metal < missingResources.Metal - resourcesTotalAvailable.Metal)
													possibleResources.Metal = tempPossibleOrigin.Resources.Metal;
												else
													possibleResources.Metal = missingResources.Metal - resourcesTotalAvailable.Metal;
												if (tempPossibleOrigin.Resources.Crystal < missingResources.Crystal - resourcesTotalAvailable.Crystal)
													possibleResources.Crystal = tempPossibleOrigin.Resources.Crystal;
												else
													possibleResources.Crystal = missingResources.Crystal - resourcesTotalAvailable.Crystal;
												if (tempPossibleOrigin.Resources.Deuterium < missingResources.Deuterium - resourcesTotalAvailable.Deuterium)
													possibleResources.Deuterium = tempPossibleOrigin.Resources.Deuterium;
												else
													possibleResources.Deuterium = missingResources.Deuterium - resourcesTotalAvailable.Deuterium;

												idealShips = _calculationService.CalcShipNumberForPayload(possibleResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, celestial.LFBonuses.GetShipCargoBonus(preferredShip), _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
												if (idealShips > possibleOrigin.Ships.GetAmount(preferredShip) ||
													possibleResources.TotalResources <= (long) _tbotInstance.InstanceSettings.Brain.Transports.MultipleOrigins.MinimumResourcesToSend ||
													_calculationService.IsThereTransportTowardsCelestial(tempPossibleOrigin, _tbotInstance.UserData.fleets))
														continue;
												
												resourcesTotalAvailable = resourcesTotalAvailable.Sum(possibleResources);
												resultOrigins.Add(tempPossibleOrigin, possibleResources);
												_tbotInstance.log(LogLevel.Warning, LogSender.Main, $"{tempPossibleOrigin.ToString()} add with {possibleResources.TotalResources} - ({resultOrigins.Count}) {_calculationService.IsThereTransportTowardsCelestial(tempPossibleOrigin, _tbotInstance.UserData.fleets)}");
												if (resourcesTotalAvailable.IsEnoughFor(missingResources))
													break;
											}

											if (resultOrigins.Count > MaxSlots) {
												DoLog(LogLevel.Information, $"Not enough slots available to send all resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Slots needed: {resultOrigins.Count().ToString()}/{MaxSlots}.");
												delay = true;
												return;
											}
											
											possibleResources = new();
											foreach (var (originMultiple, resourcesValue) in resultOrigins) {
												possibleResources = possibleResources.Sum(resourcesValue);
											}
											if (!possibleResources.IsEnoughFor(missingResources)) {
												possibleResources = possibleResources.Sum(celestial.Resources);
												DoLog(LogLevel.Information, $"Not enough cargo available on all celestials to transport resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {possibleResources.TransportableResources}");
												return;
											}

											Ships ships = new();
											var fleetID = 0;
											foreach (var (originMultiple, resourcesValue) in resultOrigins) {
												ships = new();
												ships.Add(preferredShip, _calculationService.CalcShipNumberForPayload(resourcesValue, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, celestial.LFBonuses.GetShipCargoBonus(preferredShip), _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo));
												
												fleetID = await _fleetScheduler.SendFleet(originMultiple, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, resourcesValue);

												if (fleetId == (int) SendFleetCode.AfterSleepTime) {
													stop = true;
													return;
												}
												if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
													return;
												}
											}
										} else {
											Celestial destination;
											if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.SendToTheMoonIfPossible && celestial.Coordinate.Type == Celestials.Planet && _calculationService.IsThereMoonHere(_tbotInstance.UserData.celestials, celestial) && (!celestial.Ships.IsEmpty() || celestial.Resources.TotalResources > 0)) {
												destination = _tbotInstance.UserData.celestials
													.Where(planet => planet.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
													.Where(planet => planet.Coordinate.System == celestial.Coordinate.System)
													.Where(planet => planet.Coordinate.Position == celestial.Coordinate.Position)
													.Where(planet => planet.Coordinate.Type == Celestials.Moon)
													.First();
											} else {
												destination = _tbotInstance.UserData.celestials
													.Where(planet => planet.Coordinate.Galaxy == celestial.Coordinate.Galaxy)
													.Where(planet => planet.Coordinate.System == celestial.Coordinate.System)
													.Where(planet => planet.Coordinate.Position == celestial.Coordinate.Position)
													.Where(planet => planet.Coordinate.Type == celestial.Coordinate.Type)
													.First();
											}
											origin = _tbotInstance.UserData.celestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
												.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
												.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
												.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
												.SingleOrDefault() ?? new() { ID = 0 };
											fleetId = await _fleetScheduler.HandleMinerTransport(origin, destination, xCostBuildable, buildable, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
											if (fleetId == (int) SendFleetCode.AfterSleepTime) {
												stop = true;
											}
											if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
												delay = true;
											}
										}
									}
								} else {
									DoLog(LogLevel.Information, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
								}
							} else {
								delay = true;
							}
						}
					}
				} else {
					DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: nothing to build.");
					if (celestial.Coordinate.Type == Celestials.Planet) {
						var nextDOIR = _calculationService.CalcNextDaysOfInvestmentReturn(celestial as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						if (
							(celestial as Planet).HasFacilities(maxFacilities) && (
								(celestial as Planet).HasMines(maxBuildings) ||
								nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn
							)
						) {
							if (nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn) {
								var nextMine = _calculationService.GetNextMineToBuild(celestial as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 100, 100, 100, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull, autoMinerSettings.OptimizeForStart, float.MaxValue);
								var nexMineLevel = _calculationService.GetNextLevel(celestial, nextMine);
								if (nextDOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
									_tbotInstance.UserData.nextDOIR = nextDOIR;
								}
								DoLog(LogLevel.Debug, $"To continue building you should rise Brain.AutoMine.MaxDaysOfInvestmentReturn to at least {Math.Round(nextDOIR, 2, MidpointRounding.ToPositiveInfinity).ToString()}.");
								DoLog(LogLevel.Debug, $"Next mine to build: {nextMine.ToString()} lv {nexMineLevel.ToString()}.");

							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								DoLog(LogLevel.Debug, $"To continue building you should rise Brain.AutoMine mines max levels");
							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								DoLog(LogLevel.Debug, $"To continue building you should rise Brain.AutoMine facilities max levels");
							}
							stop = true;
						}
					} else if (celestial.Coordinate.Type == Celestials.Moon) {
						if ((celestial as Moon).HasLunarFacilities(maxLunarFacilities)) {
							DoLog(LogLevel.Debug, $"To continue building you should rise Brain.AutoMine lunar facilities max levels");
						}
						stop = true;
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"AutoMineCelestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await _tbotOgameBridge.GetDateTime();
				string autoMineTimer = $"AutoMine-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					DoLog(LogLevel.Information, $"Stopping AutoMine check for {celestial.ToString()}.");
					await EndExecution();
				} else if (delayProduction) {
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Productions);
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
					DoLog(LogLevel.Information, $"Delaying...");
					time = await _tbotOgameBridge.GetDateTime();
					long interval;
					try {
						interval = _calculationService.CalcProductionTime((Buildables) celestial.Productions.First().ID, celestial.Productions.First().Nbr, _tbotInstance.UserData.serverData, celestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
					}
					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					DoLog(LogLevel.Information, $"Delaying...");
					time = await _tbotOgameBridge.GetDateTime();
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					long interval;
					try {
						interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
					}
					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (started) {
					long interval = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);

					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (_tbotInstance.UserData.lastDOIR >= _tbotInstance.UserData.nextDOIR) {
						_tbotInstance.UserData.nextDOIR = 0;
					}
				} else if (delayBuilding > 0) {

					newTime = time.AddMilliseconds(delayBuilding);
					ChangeWorkerPeriod(delayBuilding);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else {
					long interval = await CalcAutoMineTimer(celestial, buildable, level, started, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
					if (fleetId > 0) {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();							
						var transportfleet = _tbotInstance.UserData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} else {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.CheckIntervalMax);
					}
					if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.DoMultipleTransportIsNotEnoughShipButSamePosition) {
						var transportfleet2 = _tbotInstance.UserData.fleets.Where(f => f.Mission == Missions.Transport)
							.Where(f => f.Destination.IsSame(celestial.Coordinate))
							.Where(f => f.Origin.Galaxy == celestial.Coordinate.Galaxy)
							.Where(f => f.Origin.System == celestial.Coordinate.System)
							.Where(f => f.Origin.Position == celestial.Coordinate.Position)
							.Where(f => f.Origin.Type == (celestial.Coordinate.Type == Celestials.Planet ? Celestials.Moon : Celestials.Planet))
							.ToList();
						if (transportfleet2.Count() > 0) {
							interval = (long) (transportfleet2.First().BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						}
					}

					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);

					newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (_tbotInstance.UserData.lastDOIR >= _tbotInstance.UserData.nextDOIR) {
						_tbotInstance.UserData.nextDOIR = 0;
					}
					//DoLog(LogLevel.Debug, $"Last DOIR: {Math.Round(_tbotInstance.UserData.lastDOIR, 2)}");
					//DoLog(LogLevel.Debug, $"Next DOIR: {Math.Round(_tbotInstance.UserData.nextDOIR, 2)}");

				}
			}
		}
		private async Task<long> CalcAutoMineTimer(Celestial celestial, Buildables buildable, int level, bool started, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
			try {
				if (celestial.Fields.Free == 0) {
					interval = long.MaxValue;
					DoLog(LogLevel.Information, $"Stopping AutoMine check for {celestial.ToString()}: not enough fields available.");
				}

				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
				if (started) {
					if (buildable == Buildables.SolarSatellite) {
						celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Productions);
						celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);
						interval = _calculationService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, celestial.Facilities) * 1000;
					} else if (buildable == Buildables.Crawler) {
						interval = (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} else {
						if (celestial.HasConstruction())
							interval = ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
						else
							interval = 0;
					}
				} else if (celestial.HasConstruction()) {
					interval = ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				} else {
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Buildings);
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities);

					if (buildable != Buildables.Null) {
						var price = _calculationService.CalcPrice(buildable, level);
						var productionTime = long.MaxValue;
						var transportTime = long.MaxValue;
						var returningExpoTime = long.MaxValue;
						var transportOriginTime = long.MaxValue;
						var returningExpoOriginTime = long.MaxValue;

						celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
						DateTime now = await _tbotOgameBridge.GetDateTime();
						if (
							celestial.Coordinate.Type == Celestials.Planet &&
							(price.Metal <= celestial.ResourcesProduction.Metal.StorageCapacity || price.Metal <= celestial.Resources.Metal) &&
							(price.Crystal <= celestial.ResourcesProduction.Crystal.StorageCapacity || price.Crystal <= celestial.Resources.Crystal) &&
							(price.Deuterium <= celestial.ResourcesProduction.Deuterium.StorageCapacity || price.Deuterium <= celestial.Resources.Deuterium)
						) {
							var missingResources = price.Difference(celestial.Resources);
							float metProdInASecond = celestial.ResourcesProduction.Metal.CurrentProduction / (float) 3600;
							float cryProdInASecond = celestial.ResourcesProduction.Crystal.CurrentProduction / (float) 3600;
							float deutProdInASecond = celestial.ResourcesProduction.Deuterium.CurrentProduction / (float) 3600;
							if (
								!(
									(missingResources.Metal > 0 && (metProdInASecond == 0 && celestial.Resources.Metal < price.Metal)) ||
									(missingResources.Crystal > 0 && (cryProdInASecond == 0 && celestial.Resources.Crystal < price.Crystal)) ||
									(missingResources.Deuterium > 0 && (deutProdInASecond == 0 && celestial.Resources.Deuterium < price.Deuterium))
								)
							) {
								float metProductionTime = float.IsNaN(missingResources.Metal / metProdInASecond) ? 0.0F : missingResources.Metal / metProdInASecond;
								float cryProductionTime = float.IsNaN(missingResources.Crystal / cryProdInASecond) ? 0.0F : missingResources.Crystal / cryProdInASecond;
								float deutProductionTime = float.IsNaN(missingResources.Deuterium / deutProdInASecond) ? 0.0F : missingResources.Deuterium / deutProdInASecond;
								productionTime = (long) (Math.Round(Math.Max(Math.Max(metProductionTime, cryProductionTime), deutProductionTime), 0) * 1000);
								//DoLog(LogLevel.Debug, $"The required resources will be produced by {now.AddMilliseconds(productionTime).ToString()}");
							}
						}

						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var incomingFleets = _calculationService.GetIncomingFleetsWithResources(celestial, _tbotInstance.UserData.fleets);
						if (incomingFleets.Any()) {
							var fleet = incomingFleets.First();
							transportTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
							//DoLog(LogLevel.Debug, $"Next fleet with resources arriving by {now.AddMilliseconds(transportTime).ToString()}");
						}

						var returningExpo = _calculationService.GetFirstReturningExpedition(celestial.Coordinate, _tbotInstance.UserData.fleets);
						if (returningExpo != null) {
							returningExpoTime = (long) (returningExpo.BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
							//DoLog(LogLevel.Debug, $"Next expedition returning by {now.AddMilliseconds(returningExpoTime).ToString()}");
						}

						if ((bool) _tbotInstance.InstanceSettings.Brain.Transports.Active) {
							Celestial origin = _tbotInstance.UserData.celestials
									.Unique()
									.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
									.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
									.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
									.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
									.SingleOrDefault() ?? new() { ID = 0 };
							if (origin.ID != 0) {
								var returningExpoOrigin = _calculationService.GetFirstReturningExpedition(origin.Coordinate, _tbotInstance.UserData.fleets);
								if (returningExpoOrigin != null) {
									returningExpoOriginTime = (long) (returningExpoOrigin.BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
									//DoLog(LogLevel.Debug, $"Next expedition returning in transport origin celestial by {now.AddMilliseconds(returningExpoOriginTime).ToString()}");
								}

								var incomingOriginFleets = _calculationService.GetIncomingFleetsWithResources(origin, _tbotInstance.UserData.fleets);
								if (incomingOriginFleets.Any()) {
									var fleet = incomingOriginFleets.First();
									transportOriginTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
									//DoLog(LogLevel.Debug, $"Next fleet with resources arriving in transport origin celestial by {DateTime.Now.AddMilliseconds(transportOriginTime).ToString()}");
								}
							}
						}

						productionTime = productionTime < 0 || double.IsNaN(productionTime) ? long.MaxValue : productionTime;
						transportTime = transportTime < 0 || double.IsNaN(transportTime) ? long.MaxValue : transportTime;
						returningExpoTime = returningExpoTime < 0 || double.IsNaN(returningExpoTime) ? long.MaxValue : returningExpoTime;
						returningExpoOriginTime = returningExpoOriginTime < 0 || double.IsNaN(returningExpoOriginTime) ? long.MaxValue : returningExpoOriginTime;
						transportOriginTime = transportOriginTime < 0 || double.IsNaN(transportOriginTime) ? long.MaxValue : transportOriginTime;

						interval = Math.Min(Math.Min(Math.Min(Math.Min(productionTime, transportTime), returningExpoTime), returningExpoOriginTime), transportOriginTime);
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"AutoMineCelestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				return interval + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			}
			if (interval < 0)
				interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			if (interval == long.MaxValue)
				return interval;
			return interval + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
		}
	}
}
