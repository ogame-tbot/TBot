using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Services;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using TBot.Model;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public class TBotBrainWorker : ITBotMultiFeatureWorker {
		public TBotBrainWorker(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) :
			base(parentInstance, fleetScheduler, helpersService) {
		}

		public override string GetWorkerName() {
			return "Brain";
		}
		public override LogSender GetLogSenderFromFeature(Feature feat) {
			LogSender sender = LogSender.Brain;

			sender = feat switch {
				_ => LogSender.Brain
			};
			return sender;
		}


		public override async Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {
			await InitializeFeature(Feature.BrainAutobuildCargo,	AutoBuildCargo, dueTime, period, ct);
			await InitializeFeature(Feature.BrainAutoRepatriate,	AutoRepatriate, dueTime, period, ct);
			await InitializeFeature(Feature.BrainAutoMine,			AutoMine, dueTime, period, ct);
			await InitializeFeature(Feature.BrainAutoResearch,		AutoResearch, dueTime, period, ct);

			await InitializeFeature(Feature.BrainLifeformAutoMine,		LifeformAutoMine, dueTime, period, ct);
			await InitializeFeature(Feature.BrainLifeformAutoResearch,	LifeformAutoResearch, dueTime, period, ct);
		}




		private async Task AutoBuildCargo(CancellationToken ct) {
			bool stop = false;
			try {
				DoLog(LogLevel.Information, LogSender.Brain, "Running autocargo...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					return;
				}

				if ((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.Active) {
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
					List<Celestial> celestialsToExclude = _helpersService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoCargo.Exclude, _tbotInstance.UserData.celestials);

					foreach (Celestial celestial in (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.RandomOrder ? _tbotInstance.UserData.celestials.Shuffle().ToList() : _tbotInstance.UserData.celestials) {
						if (celestialsToExclude.Has(celestial)) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}

						var tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Fast);

						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.SkipIfIncomingTransport && _helpersService.IsThereTransportTowardsCelestial(tempCelestial, _tbotInstance.UserData.fleets)) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
							continue;
						}

						tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Productions);
						if (tempCelestial.HasProduction()) {
							DoLog(LogLevel.Warning, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is already a production ongoing.");
							foreach (Production production in tempCelestial.Productions) {
								Buildables productionType = (Buildables) production.ID;
								DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are already in production.");
							}
							continue;
						}
						tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Constructions);
						if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
							Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
							DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");

						}

						tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Ships);
						tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Resources);
						var capacity = _helpersService.CalcFleetCapacity(tempCelestial.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						if (tempCelestial.Coordinate.Type == Celestials.Moon && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.ExcludeMoons) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
							continue;
						}
						long neededCargos;
						Buildables preferredCargoShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoCargo.CargoType, true, out preferredCargoShip)) {
							DoLog(LogLevel.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredCargoShip = Buildables.SmallCargo;
						}
						if (capacity <= tempCelestial.Resources.TotalResources && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.LimitToCapacity) {
							long difference = tempCelestial.Resources.TotalResources - capacity;
							int oneShipCapacity = _helpersService.CalcShipCapacity(preferredCargoShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
							neededCargos = (long) Math.Round((float) difference / (float) oneShipCapacity, MidpointRounding.ToPositiveInfinity);
							DoLog(LogLevel.Information, LogSender.Brain, $"{difference.ToString("N0")} more capacity is needed, {neededCargos} more {preferredCargoShip.ToString()} are needed.");
						} else {
							neededCargos = (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToKeep - tempCelestial.Ships.GetAmount(preferredCargoShip);
						}
						if (neededCargos > 0) {
							if (neededCargos > (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToBuild)
								neededCargos = (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToBuild;

							if (tempCelestial.Ships.GetAmount(preferredCargoShip) + neededCargos > (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToKeep)
								neededCargos = (long) _tbotInstance.InstanceSettings.Brain.AutoCargo.MaxCargosToKeep - tempCelestial.Ships.GetAmount(preferredCargoShip);

							var cost = _helpersService.CalcPrice(preferredCargoShip, (int) neededCargos);
							if (tempCelestial.Resources.IsEnoughFor(cost))
								DoLog(LogLevel.Information, LogSender.Brain, $"{tempCelestial.ToString()}: Building {neededCargos}x{preferredCargoShip.ToString()}");
							else {
								var buildableCargos = _helpersService.CalcMaxBuildableNumber(preferredCargoShip, tempCelestial.Resources);
								DoLog(LogLevel.Information, LogSender.Brain, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{preferredCargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
								neededCargos = buildableCargos;
							}

							if (neededCargos > 0) {
								try {
									await _tbotInstance.OgamedInstance.BuildShips(tempCelestial, preferredCargoShip, neededCargos);
									DoLog(LogLevel.Information, LogSender.Brain, "Production succesfully started.");
								} catch {
									DoLog(LogLevel.Warning, LogSender.Brain, "Unable to start ship production.");
								}
							}

							tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Productions);
							foreach (Production production in tempCelestial.Productions) {
								Buildables productionType = (Buildables) production.ID;
								DoLog(LogLevel.Information, LogSender.Brain, $"{tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are in production.");
							}
						} else {
							DoLog(LogLevel.Information, LogSender.Brain, $"{tempCelestial.ToString()}: No ships will be built.");
						}

						newCelestials.Remove(celestial);
						newCelestials.Add(tempCelestial);
					}
					_tbotInstance.UserData.celestials = newCelestials;
				} else {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"Unable to complete autocargo: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						DoLog(LogLevel.Information, LogSender.Brain, $"Stopping feature.");
					} else {
						var time = await ITBotHelper.GetDateTime(_tbotInstance);
						var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoCargo.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoCargo.CheckIntervalMax);
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("CapacityTimer").Change(interval, Timeout.Infinite);
						DoLog(LogLevel.Information, LogSender.Brain, $"Next capacity check at {newTime.ToString()}");
						await ITBotHelper.CheckCelestials(_tbotInstance);
					}
				}
			}
		}

		public async Task AutoRepatriate(CancellationToken ct) {
			bool stop = false;
			bool delay = false;
			try {
				DoLog(LogLevel.Information, LogSender.Brain, "Repatriating resources...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					return;
				}
				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Active) || (timers.TryGetValue("TelegramCollect", out Timer value))) {
					//DoLog(LogLevel.Information, LogSender.Telegram, $"Telegram collect initated..");
					if (_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target) {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long TotalMet = 0;
						long TotalCri = 0;
						long TotalDeut = 0;
						Coordinate destinationCoordinate = new(
						(int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Galaxy,
							(int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.System,
							(int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Position,
							Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Type)
						);
						List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
						List<Celestial> celestialsToExclude = _helpersService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Exclude, _tbotInstance.UserData.celestials);

						foreach (Celestial celestial in (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.RandomOrder ? _tbotInstance.UserData.celestials.Shuffle().ToList() : _tbotInstance.UserData.celestials.OrderBy(c => _helpersService.CalcDistance(c.Coordinate, destinationCoordinate, _tbotInstance.UserData.serverData)).ToList()) {
							if (celestialsToExclude.Has(celestial)) {
								DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
								continue;
							}
							if (celestial.Coordinate.IsSame(destinationCoordinate)) {
								DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial is the target.");
								continue;
							}

							var tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Fast);

							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							if ((bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.SkipIfIncomingTransport && _helpersService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets) && (!timers.TryGetValue("TelegramCollect", out Timer value2))) {
								DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
								continue;
							}
							if (celestial.Coordinate.Type == Celestials.Moon && (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.ExcludeMoons) {
								DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
								continue;
							}

							tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Resources);
							tempCelestial = await ITBotHelper.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Ships);

							Buildables preferredShip = Buildables.SmallCargo;
							if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
								DoLog(LogLevel.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
								preferredShip = Buildables.SmallCargo;
							}
							Resources payload = tempCelestial.Resources;

							if ((long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave > 0) {
								if ((bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.LeaveDeut.OnlyOnMoons) {
									if (tempCelestial.Coordinate.Type == Celestials.Moon) {
										payload = payload.Difference(new(0, 0, (long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave));
									}
								} else {
									payload = payload.Difference(new(0, 0, (long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave));
								}
							}

							if (payload.TotalResources < (long) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.MinimumResources || payload.IsEmpty()) {
								DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: resources under set limit");
								continue;
							}

							long idealShips = _helpersService.CalcShipNumberForPayload(payload, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

							Ships ships = new();
							if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
								if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
									ships.Add(preferredShip, idealShips);
								} else {
									ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
								}
								payload = _helpersService.CalcMaxTransportableResources(ships, payload, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

								if (payload.TotalResources > 0) {
									var fleetId = await _fleetScheduler.SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
									if (fleetId == (int) SendFleetCode.AfterSleepTime) {
										stop = true;
										return;
									}
									if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
										delay = true;
										return;
									}
									TotalMet += payload.Metal;
									TotalCri += payload.Crystal;
									TotalDeut += payload.Deuterium;
								}
							} else {
								DoLog(LogLevel.Warning, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there are no {preferredShip.ToString()}");
							}

							newCelestials.Remove(celestial);
							newCelestials.Add(tempCelestial);
						}
						_tbotInstance.UserData.celestials = newCelestials;
						//send notif only if sent via telegram
						if (timers.TryGetValue("TelegramCollect", out Timer value1)) {
							if ((TotalMet > 0) || (TotalCri > 0) || (TotalDeut > 0)) {
								await _tbotInstance.SendTelegramMessage($"Resources sent!:\n{TotalMet} Metal\n{TotalCri} Crystal\n{TotalDeut} Deuterium");
							} else {
								await _tbotInstance.SendTelegramMessage("No resources sent");
							}
						}
					} else {
						DoLog(LogLevel.Warning, LogSender.Brain, "Skipping autorepatriate: unable to parse custom destination");
					}
				} else {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, LogSender.Brain, $"Unable to complete repatriate: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (timers.TryGetValue("TelegramCollect", out Timer val)) {
						val.Dispose();
						timers.Remove("TelegramCollect");
					} else {
						if (stop) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Stopping feature.");
						} else if (delay) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Delaying...");
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							var time = await ITBotHelper.GetDateTime(_tbotInstance);
							long interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
							DoLog(LogLevel.Information, LogSender.Brain, $"Next repatriate check at {newTime.ToString()}");
						} else {
							var time = await ITBotHelper.GetDateTime(_tbotInstance);
							var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMax);
							if (interval <= 0)
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
							DoLog(LogLevel.Information, LogSender.Brain, $"Next repatriate check at {newTime.ToString()}");
						}
					}
					await ITBotHelper.CheckCelestials(_tbotInstance);
				}
			}
		}


		private async Task AutoMine(CancellationToken ct) {
			try {
				DoLog(LogLevel.Information, LogSender.Brain, "Running automine...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					return;
				}

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Active) && (timers.TryGetValue("AutoMineTimer", out Timer value))) {
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

					List<Celestial> celestialsToExclude = _helpersService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoMine.Exclude, _tbotInstance.UserData.celestials);
					List<Celestial> celestialsToMine = new();
					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						var cel = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Buildings);
						var nextMine = _helpersService.GetNextMineToBuild(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 100, 100, 100, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull, true, int.MaxValue);
						var lv = _helpersService.GetNextLevel(cel, nextMine);
						var DOIR = _helpersService.CalcNextDaysOfInvestmentReturn(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						DoLog(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Mine: {nextMine.ToString()} lv {lv.ToString()}; DOIR: {DOIR.ToString()}.");
						if (DOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
							_tbotInstance.UserData.nextDOIR = DOIR;
						}
						celestialsToMine.Add(cel);
					}
					celestialsToMine = celestialsToMine.OrderBy(cel => _helpersService.CalcNextDaysOfInvestmentReturn(cel as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull)).ToList();
					celestialsToMine.AddRange(_tbotInstance.UserData.celestials.Where(c => c is Moon));

					foreach (Celestial celestial in (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.RandomOrder ? celestialsToMine.Shuffle().ToList() : celestialsToMine) {
						if (celestialsToExclude.Has(celestial)) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}

						await AutoMineCelestial(celestial, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
					}
				} else {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await ITBotHelper.CheckCelestials(_tbotInstance);
				}
			}
		}
		private async Task AutoMineCelestial(Celestial celestial, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			int fleetId = (int) SendFleetCode.GenericError;
			Buildables buildable = Buildables.Null;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			long delayBuilding = 0;
			bool delayProduction = false;
			try {
				DoLog(LogLevel.Information, LogSender.Brain, $"Running AutoMine on {celestial.ToString()}");
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Fast);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Resources);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.ResourcesProduction);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.ResourceSettings);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Buildings);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Facilities);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Productions);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Ships);
				if (
					(!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.AutoMine, "BuildCrawlers") || (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.BuildCrawlers) &&
					celestial.Coordinate.Type == Celestials.Planet &&
					_tbotInstance.UserData.userInfo.Class == CharacterClass.Collector &&
					celestial.Facilities.Shipyard >= 5 &&
					_tbotInstance.UserData.researches.CombustionDrive >= 4 &&
					_tbotInstance.UserData.researches.ArmourTechnology >= 4 &&
					_tbotInstance.UserData.researches.LaserTechnology >= 4 &&
					!celestial.Productions.Any(p => p.ID == (int) Buildables.Crawler) &&
					celestial.Constructions.BuildingID != (int) Buildables.Shipyard &&
					celestial.Constructions.BuildingID != (int) Buildables.NaniteFactory &&
					celestial.Ships.Crawler < _helpersService.CalcMaxCrawlers(celestial as Planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist) &&
					_helpersService.CalcOptimalCrawlers(celestial as Planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData) > celestial.Ships.Crawler
				) {
					buildable = Buildables.Crawler;
					level = _helpersService.CalcOptimalCrawlers(celestial as Planet, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData);
				} else {
					if (celestial.Fields.Free == 0) {
						DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: not enough fields available.");
						return;
					}
					if (celestial.Constructions.BuildingID != 0) {
						DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a building in production.");
						if (
							celestial is Planet && (
								celestial.Constructions.BuildingID == (int) Buildables.MetalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.CrystalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.DeuteriumSynthesizer
							)
						) {
							var buildingBeingBuilt = (Buildables) celestial.Constructions.BuildingID;

							var levelBeingBuilt = _helpersService.GetNextLevel(celestial, buildingBeingBuilt);
							var DOIR = _helpersService.CalcDaysOfInvestmentReturn(celestial as Planet, buildingBeingBuilt, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
							if (DOIR > _tbotInstance.UserData.lastDOIR) {
								_tbotInstance.UserData.lastDOIR = DOIR;
							}
						}
						delayBuilding = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						return;
					}

					if (celestial is Planet) {

						buildable = _helpersService.GetNextBuildingToBuild(celestial as Planet, _tbotInstance.UserData.researches, maxBuildings, maxFacilities, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.serverData, autoMinerSettings);
						level = _helpersService.GetNextLevel(celestial as Planet, buildable, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
					} else {
						buildable = _helpersService.GetNextLunarFacilityToBuild(celestial as Moon, _tbotInstance.UserData.researches, maxLunarFacilities);
						level = _helpersService.GetNextLevel(celestial as Moon, buildable, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
					}
				}

				if (buildable != Buildables.Null && level > 0) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
					if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
						float DOIR = _helpersService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						DoLog(LogLevel.Debug, LogSender.Brain, $"Days of investment return: {Math.Round(DOIR, 2).ToString()} days.");
					}

					Resources xCostBuildable = _helpersService.CalcPrice(buildable, level);
					if (celestial is Moon)
						xCostBuildable.Deuterium += (long) autoMinerSettings.DeutToLeaveOnMoons;

					if (buildable == Buildables.Terraformer) {
						if (xCostBuildable.Energy > celestial.ResourcesProduction.Energy.CurrentProduction) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Not enough energy to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							buildable = Buildables.SolarSatellite;
							level = _helpersService.CalcNeededSolarSatellites(celestial as Planet, xCostBuildable.Energy - celestial.ResourcesProduction.Energy.CurrentProduction, _tbotInstance.UserData.userInfo.Class == CharacterClass.Collector, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
							xCostBuildable = _helpersService.CalcPrice(buildable, level);
						}
					}

					if (celestial.Resources.IsEnoughFor(xCostBuildable)) {
						bool result = false;
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							if (!celestial.HasProduction()) {
								DoLog(LogLevel.Information, LogSender.Brain, $"Building {level.ToString()} x {buildable.ToString()} on {celestial.ToString()}");
								try {
									await _tbotInstance.OgamedInstance.BuildShips(celestial, buildable, level);
									result = true;
								} catch { }
							} else {
								DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: There is already a production ongoing.");
								delayProduction = true;
							}
						} else {
							DoLog(LogLevel.Information, LogSender.Brain, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							try {
								await _tbotInstance.OgamedInstance.BuildConstruction(celestial, buildable);
								result = true;
							} catch { }
						}

						if (result) {
							if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
								float DOIR = _helpersService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
								if (DOIR > _tbotInstance.UserData.lastDOIR) {
									_tbotInstance.UserData.lastDOIR = DOIR;
								}
							}
							if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
								celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Productions);
								try {
									if (celestial.Productions.First().ID == (int) buildable) {
										started = true;
										DoLog(LogLevel.Information, LogSender.Brain, $"{celestial.Productions.First().Nbr.ToString()}x {buildable.ToString()} succesfully started.");
									} else {
										celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Resources);
										if (celestial.Resources.Energy >= 0) {
											started = true;
											DoLog(LogLevel.Information, LogSender.Brain, $"{level.ToString()}x {buildable.ToString()} succesfully built");
										} else {
											DoLog(LogLevel.Warning, LogSender.Brain, $"Unable to start {level.ToString()}x {buildable.ToString()} construction: an unknown error has occurred");
										}
									}
								} catch {
									started = true;
									DoLog(LogLevel.Information, LogSender.Brain, $"Unable to determine if the production has started.");
								}
							} else {
								celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.BuildingID == (int) buildable) {
									started = true;
									DoLog(LogLevel.Information, LogSender.Brain, "Building succesfully started.");
								} else {
									celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Buildings);
									celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Facilities);
									if (celestial.GetLevel(buildable) != level)
										DoLog(LogLevel.Warning, LogSender.Brain, "Unable to start building construction: an unknown error has occurred");
									else {
										started = true;
										DoLog(LogLevel.Information, LogSender.Brain, "Building succesfully started.");
									}
								}
							}
						} else if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler)
							DoLog(LogLevel.Warning, LogSender.Brain, "Unable to start building construction: a network error has occurred");
					} else {
						if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
							float DOIR = _helpersService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
							if (DOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
								_tbotInstance.UserData.nextDOIR = DOIR;
							}
						}
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {level.ToString()}x {buildable.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");

						} else {
							DoLog(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
						}
						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Active) {
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							if (!_helpersService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets)) {
								Celestial origin = _tbotInstance.UserData.celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Galaxy)
										.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.System)
										.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Position)
										.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Type))
										.SingleOrDefault() ?? new() { ID = 0 };
								fleetId = await HandleMinerTransport(origin, celestial, xCostBuildable, buildable, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);

								if (fleetId == (int) SendFleetCode.AfterSleepTime) {
									stop = true;
									return;
								}
								if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
									delay = true;
									return;
								}
							} else {
								DoLog(LogLevel.Information, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
							}
						}
					}
				} else {
					DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build.");
					if (celestial.Coordinate.Type == Celestials.Planet) {
						var nextDOIR = _helpersService.CalcNextDaysOfInvestmentReturn(celestial as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						if (
							(celestial as Planet).HasFacilities(maxFacilities) && (
								(celestial as Planet).HasMines(maxBuildings) ||
								nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn
							)
						) {
							if (nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn) {
								var nextMine = _helpersService.GetNextMineToBuild(celestial as Planet, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 100, 100, 100, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull, autoMinerSettings.OptimizeForStart, float.MaxValue);
								var nexMineLevel = _helpersService.GetNextLevel(celestial, nextMine);
								if (nextDOIR < _tbotInstance.UserData.nextDOIR || _tbotInstance.UserData.nextDOIR == 0) {
									_tbotInstance.UserData.nextDOIR = nextDOIR;
								}
								DoLog(LogLevel.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine.MaxDaysOfInvestmentReturn to at least {Math.Round(nextDOIR, 2, MidpointRounding.ToPositiveInfinity).ToString()}.");
								DoLog(LogLevel.Debug, LogSender.Brain, $"Next mine to build: {nextMine.ToString()} lv {nexMineLevel.ToString()}.");

							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								DoLog(LogLevel.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine mines max levels");
							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								DoLog(LogLevel.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine facilities max levels");
							}
							stop = true;
						}
					} else if (celestial.Coordinate.Type == Celestials.Moon) {
						if ((celestial as Moon).HasLunarFacilities(maxLunarFacilities)) {
							DoLog(LogLevel.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine lunar facilities max levels");
						}
						stop = true;
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"AutoMineCelestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await ITBotHelper.GetDateTime(_tbotInstance);
				string autoMineTimer = $"AutoMineTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}.");
					if (timers.TryGetValue($"AutoMineTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Productions);
					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Facilities);
					DoLog(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await ITBotHelper.GetDateTime(_tbotInstance);
					long interval;
					try {
						interval = _helpersService.CalcProductionTime((Buildables) celestial.Productions.First().ID, celestial.Productions.First().Nbr, _tbotInstance.UserData.serverData, celestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await ITBotHelper.GetDateTime(_tbotInstance);
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					long interval;
					try {
						interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (started) {
					long interval = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();

					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (_tbotInstance.UserData.lastDOIR >= _tbotInstance.UserData.nextDOIR) {
						_tbotInstance.UserData.nextDOIR = 0;
					}
				} else if (delayBuilding > 0) {
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();

					newTime = time.AddMilliseconds(delayBuilding);
					ChangeFeaturePeriod(Feature.BrainAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(delayBuilding));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else {
					long interval = await CalcAutoMineTimer(celestial, buildable, level, started, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);

					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var transportfleet = _tbotInstance.UserData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);

					} else {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
					}

					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();

					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (_tbotInstance.UserData.lastDOIR >= _tbotInstance.UserData.nextDOIR) {
						_tbotInstance.UserData.nextDOIR = 0;
					}
					//DoLog(LogLevel.Debug, LogSender.Brain, $"Last DOIR: {Math.Round(_tbotInstance.UserData.lastDOIR, 2)}");
					//DoLog(LogLevel.Debug, LogSender.Brain, $"Next DOIR: {Math.Round(_tbotInstance.UserData.nextDOIR, 2)}");

				}
			}
		}
		private async Task<long> CalcAutoMineTimer(Celestial celestial, Buildables buildable, int level, bool started, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoMine.CheckIntervalMax);
			try {
				if (celestial.Fields.Free == 0) {
					interval = long.MaxValue;
					DoLog(LogLevel.Information, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}: not enough fields available.");
				}

				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);
				if (started) {
					if (buildable == Buildables.SolarSatellite) {
						celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Productions);
						celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Facilities);
						interval = _helpersService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, celestial.Facilities) * 1000;
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
					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Buildings);
					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Facilities);

					if (buildable != Buildables.Null) {
						var price = _helpersService.CalcPrice(buildable, level);
						var productionTime = long.MaxValue;
						var transportTime = long.MaxValue;
						var returningExpoTime = long.MaxValue;
						var transportOriginTime = long.MaxValue;
						var returningExpoOriginTime = long.MaxValue;

						celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.ResourcesProduction);
						DateTime now = await ITBotHelper.GetDateTime(_tbotInstance);
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
								//DoLog(LogLevel.Debug, LogSender.Brain, $"The required resources will be produced by {now.AddMilliseconds(productionTime).ToString()}");
							}
						}

						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var incomingFleets = _helpersService.GetIncomingFleetsWithResources(celestial, _tbotInstance.UserData.fleets);
						if (incomingFleets.Any()) {
							var fleet = incomingFleets.First();
							transportTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
							//DoLog(LogLevel.Debug, LogSender.Brain, $"Next fleet with resources arriving by {now.AddMilliseconds(transportTime).ToString()}");
						}

						var returningExpo = _helpersService.GetFirstReturningExpedition(celestial.Coordinate, _tbotInstance.UserData.fleets);
						if (returningExpo != null) {
							returningExpoTime = (long) (returningExpo.BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
							//DoLog(LogLevel.Debug, LogSender.Brain, $"Next expedition returning by {now.AddMilliseconds(returningExpoTime).ToString()}");
						}

						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Active) {
							Celestial origin = _tbotInstance.UserData.celestials
									.Unique()
									.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Galaxy)
									.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.System)
									.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Position)
									.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Type))
									.SingleOrDefault() ?? new() { ID = 0 };
							var returningExpoOrigin = _helpersService.GetFirstReturningExpedition(origin.Coordinate, _tbotInstance.UserData.fleets);
							if (returningExpoOrigin != null) {
								returningExpoOriginTime = (long) (returningExpoOrigin.BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								//DoLog(LogLevel.Debug, LogSender.Brain, $"Next expedition returning in transport origin celestial by {now.AddMilliseconds(returningExpoOriginTime).ToString()}");
							}

							var incomingOriginFleets = _helpersService.GetIncomingFleetsWithResources(origin, _tbotInstance.UserData.fleets);
							if (incomingOriginFleets.Any()) {
								var fleet = incomingOriginFleets.First();
								transportOriginTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
								//DoLog(LogLevel.Debug, LogSender.Brain, $"Next fleet with resources arriving in transport origin celestial by {DateTime.Now.AddMilliseconds(transportOriginTime).ToString()}");
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
				DoLog(LogLevel.Error, LogSender.Brain, $"AutoMineCelestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
				return interval + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			}
			if (interval < 0)
				interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			if (interval == long.MaxValue)
				return interval;
			return interval + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
		}

		private async Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Resources resources, Buildables buildable = Buildables.Null, Buildings maxBuildings = null, Facilities maxFacilities = null, Facilities maxLunarFacilities = null, AutoMinerSettings autoMinerSettings = null) {
			try {
				if (origin.ID == destination.ID) {
					DoLog(LogLevel.Warning, LogSender.Brain, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					DoLog(LogLevel.Warning, LogSender.Brain, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = resources.Difference(destination.Resources);
					Resources resToLeave = new(0, 0, 0);
					if ((long) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.DeutToLeave > 0)
						resToLeave.Deuterium = (long) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.DeutToLeave;

					origin = await ITBotHelper.UpdatePlanet(_tbotInstance, origin, UpdateTypes.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = await ITBotHelper.UpdatePlanet(_tbotInstance, origin, UpdateTypes.Ships);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.CargoType, true, out preferredShip)) {
							DoLog(LogLevel.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}

						long idealShips = _helpersService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						Ships ships = new();
						Ships tempShips = new();
						tempShips.Add(preferredShip, 1);
						var flightPrediction = _helpersService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, tempShips, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class);
						long flightTime = flightPrediction.Time;
						idealShips = _helpersService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						var availableShips = origin.Ships.GetAmount(preferredShip);
						if (buildable != Buildables.Null) {
							int level = _helpersService.GetNextLevel(destination, buildable);
							long buildTime = _helpersService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, destination.Facilities);
							if (maxBuildings != null && maxFacilities != null && maxLunarFacilities != null && autoMinerSettings != null) {
								var tempCelestial = destination;
								while (flightTime * 2 >= buildTime && idealShips <= availableShips) {
									tempCelestial.SetLevel(buildable, level);
									if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler && buildable != Buildables.SpaceDock) {
										tempCelestial.Fields.Built += 1;
									}
									var nextBuildable = Buildables.Null;
									if (tempCelestial.Coordinate.Type == Celestials.Planet) {
										tempCelestial.Resources.Energy += _helpersService.GetProductionEnergyDelta(buildable, level, _tbotInstance.UserData.researches.EnergyTechnology, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
										tempCelestial.ResourcesProduction.Energy.Available += _helpersService.GetProductionEnergyDelta(buildable, level, _tbotInstance.UserData.researches.EnergyTechnology, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
										tempCelestial.Resources.Energy -= _helpersService.GetRequiredEnergyDelta(buildable, level);
										tempCelestial.ResourcesProduction.Energy.Available -= _helpersService.GetRequiredEnergyDelta(buildable, level);
										nextBuildable = _helpersService.GetNextBuildingToBuild(tempCelestial as Planet, _tbotInstance.UserData.researches, maxBuildings, maxFacilities, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.serverData, autoMinerSettings, 1);
									} else {
										nextBuildable = _helpersService.GetNextLunarFacilityToBuild(tempCelestial as Moon, _tbotInstance.UserData.researches, maxLunarFacilities);
									}
									if ((nextBuildable != Buildables.Null) && (buildable != Buildables.SolarSatellite)) {
										var nextLevel = _helpersService.GetNextLevel(tempCelestial, nextBuildable);
										var newMissingRes = missingResources.Sum(_helpersService.CalcPrice(nextBuildable, nextLevel));

										if (origin.Resources.IsEnoughFor(newMissingRes, resToLeave)) {
											var newIdealShips = _helpersService.CalcShipNumberForPayload(newMissingRes, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
											if (newIdealShips <= origin.Ships.GetAmount(preferredShip)) {
												idealShips = newIdealShips;
												missingResources = newMissingRes;
												buildTime += _helpersService.CalcProductionTime(nextBuildable, nextLevel, _tbotInstance.UserData.serverData, tempCelestial.Facilities);
												DoLog(LogLevel.Information, LogSender.Brain, $"Sending resources for {nextBuildable.ToString()} level {nextLevel} too");
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
							idealShips = _helpersService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						}

						if (idealShips <= origin.Ships.GetAmount(preferredShip)) {
							ships.Add(preferredShip, idealShips);

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = await ITBotHelper.UpdatePlanet(_tbotInstance, destination, UpdateTypes.ResourceSettings);
								destination = await ITBotHelper.UpdatePlanet(_tbotInstance, destination, UpdateTypes.Buildings);
								destination = await ITBotHelper.UpdatePlanet(_tbotInstance, destination, UpdateTypes.ResourcesProduction);

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
									DoLog(LogLevel.Information, LogSender.Brain, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
									return await _fleetScheduler.SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
								} else {
									DoLog(LogLevel.Information, LogSender.Brain, "Skipping transport: it is quicker to wait for production.");
									return 0;
								}
							} else {
								DoLog(LogLevel.Information, LogSender.Brain, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
								return await _fleetScheduler.SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
							}
						} else {
							DoLog(LogLevel.Information, LogSender.Brain, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						DoLog(LogLevel.Information, LogSender.Brain, $"Skipping transport: not enough resources in origin. Needed: {missingResources.TransportableResources} - Available: {origin.Resources.TransportableResources}");
						return 0;
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"HandleMinerTransport Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}



		private async Task AutoResearch(CancellationToken ct) {
			int fleetId = (int) SendFleetCode.GenericError;
			bool stop = false;
			bool delay = false;
			long delayResearch = 0;
			try {
				DoLog(LogLevel.Information, LogSender.Brain, "Running autoresearch...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					return;
				}

				if ((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.Active) {
					_tbotInstance.UserData.researches = await _tbotInstance.OgamedInstance.GetResearches();
					Planet celestial;
					var parseSucceded = _tbotInstance.UserData.celestials
						.Any(c => c.HasCoords(new(
							(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.Galaxy,
							(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.System,
							(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.Position,
							Celestials.Planet
						))
					);
					if (parseSucceded) {
						celestial = _tbotInstance.UserData.celestials
							.Unique()
							.Single(c => c.HasCoords(new(
								(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.Galaxy,
								(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.System,
								(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.Position,
								Celestials.Planet
								)
							)) as Planet;
					} else {
						DoLog(LogLevel.Warning, LogSender.Brain, "Unable to parse Brain.AutoResearch.Target. Falling back to planet with biggest Research Lab");
						_tbotInstance.UserData.celestials = await ITBotHelper.UpdatePlanets(_tbotInstance, UpdateTypes.Facilities);
						celestial = _tbotInstance.UserData.celestials
							.Where(c => c.Coordinate.Type == Celestials.Planet)
							.OrderByDescending(c => c.Facilities.ResearchLab)
							.First() as Planet;
					}

					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Facilities) as Planet;
					if (celestial.Facilities.ResearchLab == 0) {
						DoLog(LogLevel.Information, LogSender.Brain, "Skipping AutoResearch: Research Lab is missing on target planet.");
						return;
					}
					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions) as Planet;
					if (celestial.Constructions.ResearchID != 0) {
						delayResearch = (long) celestial.Constructions.ResearchCountdown * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						DoLog(LogLevel.Information, LogSender.Brain, "Skipping AutoResearch: there is already a research in progress.");
						return;
					}
					if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab) {
						DoLog(LogLevel.Information, LogSender.Brain, "Skipping AutoResearch: the Research Lab is upgrading.");
						return;
					}
					_tbotInstance.UserData.slots = await ITBotHelper.UpdateSlots(_tbotInstance);
					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Facilities) as Planet;
					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Resources) as Planet;
					celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.ResourcesProduction) as Planet;

					Buildables research;

					if ((bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeAstrophysics || (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizePlasmaTechnology || (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeEnergyTechnology || (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork) {
						List<Celestial> planets = new();
						foreach (var p in _tbotInstance.UserData.celestials) {
							if (p.Coordinate.Type == Celestials.Planet) {
								var newPlanet = await ITBotHelper.UpdatePlanet(_tbotInstance, p, UpdateTypes.Facilities);
								newPlanet = await ITBotHelper.UpdatePlanet(_tbotInstance, p, UpdateTypes.Buildings);
								planets.Add(newPlanet);
							}
						}
						var plasmaDOIR = _helpersService.CalcNextPlasmaTechDOIR(planets.Where(c => c is Planet).Cast<Planet>().ToList<Planet>(), _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						DoLog(LogLevel.Debug, LogSender.Brain, $"Next Plasma tech DOIR: {Math.Round(plasmaDOIR, 2).ToString()}");
						var astroDOIR = _helpersService.CalcNextAstroDOIR(planets.Where(c => c is Planet).Cast<Planet>().ToList<Planet>(), _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
						DoLog(LogLevel.Debug, LogSender.Brain, $"Next Astro DOIR: {Math.Round(astroDOIR, 2).ToString()}");

						if (
							(bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizePlasmaTechnology &&
							_tbotInstance.UserData.lastDOIR > 0 &&
							plasmaDOIR <= _tbotInstance.UserData.lastDOIR &&
							plasmaDOIR <= (float) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxDaysOfInvestmentReturn &&
							(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxPlasmaTechnology >= _tbotInstance.UserData.researches.PlasmaTechnology + 1 &&
							celestial.Facilities.ResearchLab >= 4 &&
							_tbotInstance.UserData.researches.EnergyTechnology >= 8 &
							_tbotInstance.UserData.researches.LaserTechnology >= 10 &&
							_tbotInstance.UserData.researches.IonTechnology >= 5
						) {
							research = Buildables.PlasmaTechnology;
						} else if ((bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeEnergyTechnology && _helpersService.ShouldResearchEnergyTech(planets.Where(c => c.Coordinate.Type == Celestials.Planet).Cast<Planet>().ToList<Planet>(), _tbotInstance.UserData.researches, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEnergyTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull)) {
							research = Buildables.EnergyTechnology;
						} else if (
							(bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeAstrophysics &&
							_tbotInstance.UserData.lastDOIR > 0 &&
							(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxAstrophysics >= (_tbotInstance.UserData.researches.Astrophysics % 2 == 0 ? _tbotInstance.UserData.researches.Astrophysics + 1 : _tbotInstance.UserData.researches.Astrophysics + 2) &&
							astroDOIR <= (float) _tbotInstance.InstanceSettings.Brain.AutoMine.MaxDaysOfInvestmentReturn &&
							astroDOIR <= _tbotInstance.UserData.lastDOIR &&
							celestial.Facilities.ResearchLab >= 3 &&
							_tbotInstance.UserData.researches.EspionageTechnology >= 4 &&
							_tbotInstance.UserData.researches.ImpulseDrive >= 3
						) {
							research = Buildables.Astrophysics;
						} else {
							research = _helpersService.GetNextResearchToBuild(celestial as Planet, _tbotInstance.UserData.researches, (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.PrioritizeRobotsAndNanites, _tbotInstance.UserData.slots, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEnergyTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxLaserTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIonTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxPlasmaTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxCombustionDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxImpulseDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxHyperspaceDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEspionageTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxComputerTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxAstrophysics, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxWeaponsTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxShieldingTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxArmourTechnology, (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.OptimizeForStart, (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.EnsureExpoSlots, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.Admiral);
						}
					} else {
						research = _helpersService.GetNextResearchToBuild(celestial as Planet, _tbotInstance.UserData.researches, (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.PrioritizeRobotsAndNanites, _tbotInstance.UserData.slots, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEnergyTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxLaserTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIonTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxPlasmaTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxCombustionDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxImpulseDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxHyperspaceDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEspionageTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxComputerTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxAstrophysics, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxWeaponsTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxShieldingTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxArmourTechnology, (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.OptimizeForStart, (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.EnsureExpoSlots, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.Admiral);
					}

					if (
						(bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork &&
						research != Buildables.Null &&
						research != Buildables.IntergalacticResearchNetwork &&
						celestial.Facilities.ResearchLab >= 10 &&
						_tbotInstance.UserData.researches.ComputerTechnology >= 8 &&
						_tbotInstance.UserData.researches.HyperspaceTechnology >= 8 &&
						(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIntergalacticResearchNetwork >= _helpersService.GetNextLevel(_tbotInstance.UserData.researches, Buildables.IntergalacticResearchNetwork) &&
						_tbotInstance.UserData.celestials.Any(c => c.Facilities != null)
					) {
						var cumulativeLabLevel = _helpersService.CalcCumulativeLabLevel(_tbotInstance.UserData.celestials, _tbotInstance.UserData.researches);
						var researchTime = _helpersService.CalcProductionTime(research, _helpersService.GetNextLevel(_tbotInstance.UserData.researches, research), _tbotInstance.UserData.serverData.SpeedResearch, celestial.Facilities, cumulativeLabLevel, _tbotInstance.UserData.userInfo.Class == CharacterClass.Discoverer, _tbotInstance.UserData.staff.Technocrat);
						var irnTime = _helpersService.CalcProductionTime(Buildables.IntergalacticResearchNetwork, _helpersService.GetNextLevel(_tbotInstance.UserData.researches, Buildables.IntergalacticResearchNetwork), _tbotInstance.UserData.serverData.SpeedResearch, celestial.Facilities, cumulativeLabLevel, _tbotInstance.UserData.userInfo.Class == CharacterClass.Discoverer, _tbotInstance.UserData.staff.Technocrat);
						if (irnTime < researchTime) {
							research = Buildables.IntergalacticResearchNetwork;
						}
					}

					int level = _helpersService.GetNextLevel(_tbotInstance.UserData.researches, research);
					if (research != Buildables.Null) {
						celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Resources) as Planet;
						Resources cost = _helpersService.CalcPrice(research, level);
						if (celestial.Resources.IsEnoughFor(cost)) {
							try {
								await _tbotInstance.OgamedInstance.BuildCancelable(celestial, research);
								DoLog(LogLevel.Information, LogSender.Brain, $"Research {research.ToString()} level {level.ToString()} started on {celestial.ToString()}");
							} catch {
								DoLog(LogLevel.Warning, LogSender.Brain, $"Research {research.ToString()} level {level.ToString()} could not be started on {celestial.ToString()}");
							}
						} else {
							DoLog(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {research.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {cost.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
							if ((bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.Transports.Active) {
								_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
								if (!_helpersService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets)) {
									Celestial origin = _tbotInstance.UserData.celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Transports.Origin.Galaxy)
										.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Transports.Origin.System)
										.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Transports.Origin.Position)
										.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoResearch.Transports.Origin.Type))
										.SingleOrDefault() ?? new() { ID = 0 };
									fleetId = await HandleMinerTransport(origin, celestial, cost);
									if (fleetId == (int) SendFleetCode.AfterSleepTime) {
										stop = true;
									}
									if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
										delay = true;
									}
								} else {
									DoLog(LogLevel.Information, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
									fleetId = (_tbotInstance.UserData.fleets
										.Where(f => f.Mission == Missions.Transport)
										.Where(f => f.Resources.TotalResources > 0)
										.Where(f => f.ReturnFlight == false)
										.Where(f => f.Destination.Galaxy == celestial.Coordinate.Galaxy)
										.Where(f => f.Destination.System == celestial.Coordinate.System)
										.Where(f => f.Destination.Position == celestial.Coordinate.Position)
										.Where(f => f.Destination.Type == celestial.Coordinate.Type)
										.OrderByDescending(f => f.ArriveIn)
										.FirstOrDefault() ?? new() { ID = 0 })
										.ID;
								}
							}
						}
					}
				} else {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"AutoResearch Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						DoLog(LogLevel.Information, LogSender.Brain, $"Stopping feature.");
					} else if (delay) {
						DoLog(LogLevel.Information, LogSender.Brain, $"Delaying...");
						var time = await ITBotHelper.GetDateTime(_tbotInstance);
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval;
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoResearch.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoResearch.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoResearchTimer").Change(interval, Timeout.Infinite);
						DoLog(LogLevel.Information, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					} else if (delayResearch > 0) {
						var time = await ITBotHelper.GetDateTime(_tbotInstance);
						var newTime = time.AddMilliseconds(delayResearch);
						timers.GetValueOrDefault("AutoResearchTimer").Change(delayResearch, Timeout.Infinite);
						DoLog(LogLevel.Information, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					} else {
						long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoResearch.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.CheckIntervalMax);
						Planet celestial = _tbotInstance.UserData.celestials
							.Unique()
							.SingleOrDefault(c => c.HasCoords(new(
								(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.Galaxy,
								(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.System,
								(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.Target.Position,
								Celestials.Planet
								)
							)) as Planet ?? new Planet() { ID = 0 };
						var time = await ITBotHelper.GetDateTime(_tbotInstance);
						if (celestial.ID != 0) {
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions) as Planet;
							var incomingFleets = _helpersService.GetIncomingFleets(celestial, _tbotInstance.UserData.fleets);
							if (celestial.Constructions.ResearchCountdown != 0)
								interval = (long) ((long) celestial.Constructions.ResearchCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							else if (fleetId > (int) SendFleetCode.GenericError) {
								var fleet = _tbotInstance.UserData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
								interval = (fleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							} else if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab)
								interval = (long) ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							else if (incomingFleets.Count() > 0) {
								var fleet = incomingFleets
									.OrderBy(f => (f.Mission == Missions.Transport || f.Mission == Missions.Deploy) ? f.ArriveIn : f.BackIn)
									.First();
								interval = (((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							} else {
								interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoResearch.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.CheckIntervalMax);
							}
						}
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoResearchTimer").Change(interval, Timeout.Infinite);
						DoLog(LogLevel.Information, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					}
					await ITBotHelper.CheckCelestials(_tbotInstance);
				}
			}
		}


		private async Task LifeformAutoMine(CancellationToken ct) {
			try {
				DoLog(LogLevel.Information, LogSender.Brain, "Running Lifeform automine...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					return;
				}

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.Active) && (timers.TryGetValue("LifeformAutoMineTimer", out Timer value))) {
					AutoMinerSettings autoMinerSettings = new() {
						DeutToLeaveOnMoons = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DeutToLeaveOnMoons
					};

					List<Celestial> celestialsToMine = new();
					LFBuildings maxLFBuildings = new();
					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						var cel = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Buildings);

						if ((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.StartFromCrystalMineLvl > (int) cel.Buildings.CrystalMine) {
							DoLog(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()} did not reach required CrystalMine level. Skipping..");
							continue;
						}
						int maxTechFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseTechBuilding;
						int maxPopuFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBaseFoodBuilding;
						int maxFoodFactory = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.MaxBasePopulationBuilding;

						cel = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFBuildings);
						cel = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.ResourcesProduction);
						var nextLFBuilding = await _helpersService.GetNextLFBuildingToBuild(cel, maxPopuFactory, maxFoodFactory, maxTechFactory);
						if (nextLFBuilding != LFBuildables.None) {
							var lv = _helpersService.GetNextLevel(celestial, nextLFBuilding);
							DoLog(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Mine: {nextLFBuilding.ToString()} lv {lv.ToString()}.");

							celestialsToMine.Add(celestial);
						} else {
							DoLog(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: No Next Lifeform building to build found.");
						}
					}

					foreach (Celestial celestial in celestialsToMine) {
						await LifeformAutoMineCelestial(celestial);
					}
				} else {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"Lifeform AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await ITBotHelper.CheckCelestials(_tbotInstance);
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

				DoLog(LogLevel.Information, LogSender.Brain, $"Running Lifeform AutoMine on {celestial.ToString()}");
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Resources);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.ResourcesProduction);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFBuildings);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Buildings);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);

				if (celestial.Constructions.LFBuildingID != 0 || celestial.Constructions.BuildingID == (int) Buildables.RoboticsFactory || celestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a building (LF, robotic or nanite) in production.");
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
							DoLog(LogLevel.Information, LogSender.Brain, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
							Resources xCostBuildable = await _tbotInstance.OgamedInstance.GetPrice(buildable, level);

							if (celestial.Resources.IsBuildable(xCostBuildable)) {
								bool result = false;
								DoLog(LogLevel.Information, LogSender.Brain, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
								try {
									await _tbotInstance.OgamedInstance.BuildCancelable(celestial, buildable);
									celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);
									if (celestial.Constructions.LFBuildingID == (int) buildable) {
										started = true;
										DoLog(LogLevel.Information, LogSender.Brain, "Building succesfully started.");
									} else {
										celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFBuildings);
										if (celestial.GetLevel(buildable) != level)
											DoLog(LogLevel.Warning, LogSender.Brain, "Unable to start building construction: an unknown error has occurred");
										else {
											started = true;
											DoLog(LogLevel.Information, LogSender.Brain, "Building succesfully started.");
										}
									}

								} catch {
									DoLog(LogLevel.Warning, LogSender.Brain, "Unable to start building construction: a network error has occurred");
								}
							} else {
								DoLog(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.LFBuildingCostResources.ToString()} - Available: {celestial.Resources.LFBuildingCostResources.ToString()}");

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
										fleetId = await HandleMinerTransport(origin, celestial, xCostBuildable);
										if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										}
										if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											delay = true;
											return;
										}
									} else {
										DoLog(LogLevel.Information, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
									}
								}
							}
						} else {
							DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build. Check max Lifeform base building max level in _tbotInstance.InstanceSettings file?");
							stop = true;
						}
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"LifeformAutoMine Celestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await ITBotHelper.GetDateTime(_tbotInstance);
				string autoMineTimer = $"LifeformAutoMineTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Stopping Lifeform AutoMine check for {celestial.ToString()}.");
					if (timers.TryGetValue($"LifeformAutoMineTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await ITBotHelper.GetDateTime(_tbotInstance);
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					newTime = time.AddMilliseconds(delayTime);
					ChangeFeaturePeriod(Feature.BrainLifeformAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(delayTime));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await ITBotHelper.GetDateTime(_tbotInstance);
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					try {
						interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainLifeformAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (started) {
					interval = ((long) celestial.Constructions.LFBuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();

					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainLifeformAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (delayTime > 0) {
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(delayTime);
					ChangeFeaturePeriod(Feature.BrainLifeformAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(delayTime));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

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
					ChangeFeaturePeriod(Feature.BrainLifeformAutoMine, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}


		private async Task LifeformAutoResearch(CancellationToken ct) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				DoLog(LogLevel.Information, LogSender.Brain, "Running Lifeform autoresearch...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					return;
				}

				if (((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Active) && (timers.TryGetValue("LifeformAutoResearchTimer", out Timer value))) {
					AutoMinerSettings autoMinerSettings = new() {
						DeutToLeaveOnMoons = (int) _tbotInstance.InstanceSettings.Brain.AutoMine.DeutToLeaveOnMoons
					};
					int maxResearchLevel = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxResearchLevel;
					List<Celestial> celestialsToMine = new();
					LFBuildings maxLFBuildings = new();

					foreach (Celestial celestial in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
						var cel = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFBuildings);
						cel = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFTechs);
						cel = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Resources);

						if (cel.LFtype == LFTypes.None) {
							DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {cel.ToString()}: No Lifeform active on this planet.");
							continue;
						}
						var nextLFTechToBuild = _helpersService.GetNextLFTechToBuild(cel, maxResearchLevel);
						if (nextLFTechToBuild != LFTechno.None) {
							var level = _helpersService.GetNextLevel(cel, nextLFTechToBuild);
							Resources nextLFTechCost = await _tbotInstance.OgamedInstance.GetPrice(nextLFTechToBuild, level);
							var isLessCostLFTechToBuild = await _helpersService.GetLessExpensiveLFTechToBuild(cel, nextLFTechCost, maxResearchLevel);
							if (isLessCostLFTechToBuild != LFTechno.None) {
								level = _helpersService.GetNextLevel(cel, isLessCostLFTechToBuild);
								nextLFTechToBuild = isLessCostLFTechToBuild;
							}

							DoLog(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Lifeform Research: {nextLFTechToBuild.ToString()} lv {level.ToString()}.");
							celestialsToMine.Add(celestial);
						} else {
							DoLog(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: No Next Lifeform technoDoLogy to build found. All research reached _tbotInstance.InstanceSettings MaxResearchLevel ?");
						}

					}
					foreach (Celestial celestial in celestialsToMine) {
						await LifeformAutoResearchCelestial(celestial);
					}
				} else {
					DoLog(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"Lifeform AutoMine Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					await ITBotHelper.CheckCelestials(_tbotInstance);
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
				DoLog(LogLevel.Information, LogSender.Brain, $"Running Lifeform AutoResearch on {celestial.ToString()}");
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Fast);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Resources);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFTechs);
				celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);

				if (celestial.Constructions.LFResearchID != 0) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a Lifeform research in production.");
					delayProduction = true;
					delayTime = (long) celestial.Constructions.LFResearchCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					return;
				}
				int maxResearchLevel = (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.MaxResearchLevel;
				if (celestial is Planet) {
					buildable = _helpersService.GetNextLFTechToBuild(celestial, maxResearchLevel);

					if (buildable != LFTechno.None) {
						level = _helpersService.GetNextLevel(celestial, buildable);
						Resources nextLFTechCost = await _tbotInstance.OgamedInstance.GetPrice(buildable, level);
						var isLessCostLFTechToBuild = await _helpersService.GetLessExpensiveLFTechToBuild(celestial, nextLFTechCost, maxResearchLevel);
						if (isLessCostLFTechToBuild != LFTechno.None) {
							level = _helpersService.GetNextLevel(celestial, isLessCostLFTechToBuild);
							buildable = isLessCostLFTechToBuild;
						}
						DoLog(LogLevel.Information, LogSender.Brain, $"Best Lifeform Research for {celestial.ToString()}: {buildable.ToString()}");

						Resources xCostBuildable = await _tbotInstance.OgamedInstance.GetPrice(buildable, level);

						if (celestial.Resources.IsEnoughFor(xCostBuildable)) {
							bool result = false;
							DoLog(LogLevel.Information, LogSender.Brain, $"Lifeform Research {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							try {
								await _tbotInstance.OgamedInstance.BuildCancelable(celestial, (LFTechno) buildable);
								celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.LFResearchID == (int) buildable) {
									started = true;
									DoLog(LogLevel.Information, LogSender.Brain, "Lifeform Research succesfully started.");
								} else {
									celestial = await ITBotHelper.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.LFTechs);
									if (celestial.GetLevel(buildable) != level)
										DoLog(LogLevel.Warning, LogSender.Brain, "Unable to start Lifeform Research construction: an unknown error has occurred");
									else {
										started = true;
										DoLog(LogLevel.Information, LogSender.Brain, "Lifeform Research succesfully started.");
									}
								}

							} catch {
								DoLog(LogLevel.Warning, LogSender.Brain, "Unable to start Lifeform Research: a network error has occurred");
							}
						} else {
							DoLog(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.LFBuildingCostResources.ToString()} - Available: {celestial.Resources.LFBuildingCostResources.ToString()}");

							if ((bool) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.Transports.Active) {
								_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
								if (!_helpersService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets)) {
									Celestial origin = _tbotInstance.UserData.celestials
											.Unique()
											.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Galaxy)
											.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.System)
											.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Position)
											.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoMine.Transports.Origin.Type))
											.SingleOrDefault() ?? new() { ID = 0 };
									fleetId = await HandleMinerTransport(origin, celestial, xCostBuildable);
									if (fleetId == (int) SendFleetCode.AfterSleepTime) {
										stop = true;
										return;
									}
									if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
										delay = true;
										return;
									}
								} else {
									DoLog(LogLevel.Information, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
								}
							}
						}
					} else {
						DoLog(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build. All research reached _tbotInstance.InstanceSettings MaxResearchLevel ?");
						stop = true;
					}
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, LogSender.Brain, $"LifeformAutoResearch Celestial Exception: {e.Message}");
				DoLog(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await ITBotHelper.GetDateTime(_tbotInstance);
				string autoMineTimer = $"LifeformAutoResearchTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Stopping Lifeform AutoResearch check for {celestial.ToString()}.");
					if (timers.TryGetValue($"LifeformAutoResearchTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await ITBotHelper.GetDateTime(_tbotInstance);
					newTime = time.AddMilliseconds(delayTime);
					ChangeFeaturePeriod(Feature.BrainLifeformAutoResearch, TimeSpan.Zero, TimeSpan.FromMilliseconds(delayTime));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform Research check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					DoLog(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await ITBotHelper.GetDateTime(_tbotInstance);
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					try {
						interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.CheckIntervalMax);
					}
					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainLifeformAutoResearch, TimeSpan.Zero, TimeSpan.FromMilliseconds(interval));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (started) {
					interval = ((long) celestial.Constructions.LFResearchCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoResearch.CheckIntervalMax);

					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainLifeformAutoResearch, TimeSpan.Zero, TimeSpan.FromMilliseconds(delayTime));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");
				} else {
					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						var transportfleet = _tbotInstance.UserData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} else {
						interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.LifeformAutoMine.CheckIntervalMax);
					}

					newTime = time.AddMilliseconds(interval);
					ChangeFeaturePeriod(Feature.BrainLifeformAutoResearch, TimeSpan.Zero, TimeSpan.FromMilliseconds(delayTime));
					DoLog(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}
	}
}
