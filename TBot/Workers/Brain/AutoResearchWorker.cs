using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using Tbot.Helpers;
using TBot.Model;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Tbot.Includes;
using Tbot.Services;
using TBot.Ogame.Infrastructure;

namespace Tbot.Workers.Brain {
	public class AutoResearchWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;

		public AutoResearchWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_calculationService = calculationService;
			_fleetScheduler = fleetScheduler;
			_tbotOgameBridge = tbotOgameBridge;
		}
		protected override async Task Execute() {
			int fleetId = (int) SendFleetCode.GenericError;
			bool stop = false;
			bool delay = false;
			long delayResearch = 0;
			try {
				DoLog(LogLevel.Information, "Running autoresearch...");

				_tbotInstance.UserData.researches = await _ogameService.GetResearches();
				_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.LFBonuses);
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
					DoLog(LogLevel.Warning, "Unable to parse Brain.AutoResearch.Target. Falling back to planet with biggest Research Lab");
					_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.Facilities);
					celestial = _tbotInstance.UserData.celestials
						.Where(c => c.Coordinate.Type == Celestials.Planet)
						.OrderByDescending(c => c.Facilities.ResearchLab)
						.First() as Planet;
				}

				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities) as Planet;
				if (celestial.Facilities.ResearchLab == 0) {
					DoLog(LogLevel.Information, "Skipping AutoResearch: Research Lab is missing on target planet.");
					return;
				}
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions) as Planet;
				if (celestial.Constructions.ResearchID != 0) {
					delayResearch = (long) celestial.Constructions.ResearchCountdown * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DoLog(LogLevel.Information, "Skipping AutoResearch: there is already a research in progress.");
					return;
				}
				if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab) {
					DoLog(LogLevel.Information, "Skipping AutoResearch: the Research Lab is upgrading.");
					return;
				}
				_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Facilities) as Planet;
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources) as Planet;
				celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.ResourcesProduction) as Planet;

				Buildables research;

				if ((bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeAstrophysics || (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizePlasmaTechnology || (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeEnergyTechnology || (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork) {
					List<Celestial> planets = new();
					foreach (var p in _tbotInstance.UserData.celestials) {
						if (p.Coordinate.Type == Celestials.Planet) {
							var newPlanet = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Facilities);
							newPlanet = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.Buildings);
							newPlanet = await _tbotOgameBridge.UpdatePlanet(p, UpdateTypes.LFBonuses);
							planets.Add(newPlanet);
						}
					}
					var plasmaDOIR = _calculationService.CalcNextPlasmaTechDOIR(planets.Where(c => c is Planet).Cast<Planet>().ToList<Planet>(), _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
					DoLog(LogLevel.Debug, $"Next Plasma tech DOIR: {Math.Round(plasmaDOIR, 2).ToString()}");
					var astroDOIR = _calculationService.CalcNextAstroDOIR(planets.Where(c => c is Planet).Cast<Planet>().ToList<Planet>(), _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData.Speed, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull);
					DoLog(LogLevel.Debug, $"Next Astro DOIR: {Math.Round(astroDOIR, 2).ToString()}");

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
					} else if ((bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeEnergyTechnology && _calculationService.ShouldResearchEnergyTech(planets.Where(c => c.Coordinate.Type == Celestials.Planet).Cast<Planet>().ToList<Planet>(), _tbotInstance.UserData.researches, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEnergyTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.IsFull)) {
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
						research = _calculationService.GetNextResearchToBuild(celestial as Planet, _tbotInstance.UserData.researches, (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.PrioritizeRobotsAndNanites, _tbotInstance.UserData.slots, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEnergyTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxLaserTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIonTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxPlasmaTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxCombustionDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxImpulseDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxHyperspaceDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEspionageTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxComputerTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxAstrophysics, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxWeaponsTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxShieldingTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxArmourTechnology, (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.OptimizeForStart, (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.EnsureExpoSlots, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.Admiral);
					}
				} else {
					research = _calculationService.GetNextResearchToBuild(celestial as Planet, _tbotInstance.UserData.researches, (bool) _tbotInstance.InstanceSettings.Brain.AutoMine.PrioritizeRobotsAndNanites, _tbotInstance.UserData.slots, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEnergyTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxLaserTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIonTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxPlasmaTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxCombustionDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxImpulseDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxHyperspaceDrive, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxEspionageTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxComputerTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxAstrophysics, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxWeaponsTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxShieldingTechnology, (int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxArmourTechnology, (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.OptimizeForStart, (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.EnsureExpoSlots, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Geologist, _tbotInstance.UserData.staff.Admiral);
				}

				if (
					(bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork &&
					research != Buildables.Null &&
					research != Buildables.IntergalacticResearchNetwork &&
					celestial.Facilities.ResearchLab >= 10 &&
					_tbotInstance.UserData.researches.ComputerTechnology >= 8 &&
					_tbotInstance.UserData.researches.HyperspaceTechnology >= 8 &&
					(int) _tbotInstance.InstanceSettings.Brain.AutoResearch.MaxIntergalacticResearchNetwork >= _calculationService.GetNextLevel(_tbotInstance.UserData.researches, Buildables.IntergalacticResearchNetwork) &&
					_tbotInstance.UserData.celestials.Any(c => c.Facilities != null)
				) {
					var cumulativeLabLevel = _calculationService.CalcCumulativeLabLevel(_tbotInstance.UserData.celestials, _tbotInstance.UserData.researches);
					var researchTime = _calculationService.CalcProductionTime(research, _calculationService.GetNextLevel(_tbotInstance.UserData.researches, research), _tbotInstance.UserData.serverData.SpeedResearch, celestial.Facilities, cumulativeLabLevel, _tbotInstance.UserData.userInfo.Class == CharacterClass.Discoverer, _tbotInstance.UserData.staff.Technocrat);
					var irnTime = _calculationService.CalcProductionTime(Buildables.IntergalacticResearchNetwork, _calculationService.GetNextLevel(_tbotInstance.UserData.researches, Buildables.IntergalacticResearchNetwork), _tbotInstance.UserData.serverData.SpeedResearch, celestial.Facilities, cumulativeLabLevel, _tbotInstance.UserData.userInfo.Class == CharacterClass.Discoverer, _tbotInstance.UserData.staff.Technocrat);
					if (irnTime < researchTime) {
						research = Buildables.IntergalacticResearchNetwork;
					}
				}

				int level = _calculationService.GetNextLevel(_tbotInstance.UserData.researches, research);
				if (research != Buildables.Null) {
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources) as Planet;
					Resources cost = _calculationService.CalcPrice(research, level);
					if (celestial.Resources.IsEnoughFor(cost)) {
						try {
							await _ogameService.BuildCancelable(celestial, research);
							DoLog(LogLevel.Information, $"Research {research.ToString()} level {level.ToString()} started on {celestial.ToString()}");
						} catch {
							DoLog(LogLevel.Warning, $"Research {research.ToString()} level {level.ToString()} could not be started on {celestial.ToString()}");
						}
					} else {
						DoLog(LogLevel.Information, $"Not enough resources to build: {research.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {cost.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.Transports.Active && (bool) _tbotInstance.InstanceSettings.Brain.Transports.Active) {
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
								fleetId = await _fleetScheduler.HandleMinerTransport(origin, celestial, cost, Buildables.Null);

								if (fleetId == (int) SendFleetCode.NotEnoughRessources) {
									origin = _tbotInstance.UserData.celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Galaxy)
										.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.System)
										.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Position)
										.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.Transports.Origin.Type))
										.SingleOrDefault() ?? new() { ID = 0 };
									fleetId = await _fleetScheduler.HandleMinerTransport(origin, celestial, cost, Buildables.Null);
								}
								if (fleetId == (int) SendFleetCode.AfterSleepTime) {
									stop = true;
								}
								if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
									delay = true;
								}
							} else {
								DoLog(LogLevel.Information, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
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
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"AutoResearch Exception: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						DoLog(LogLevel.Information, $"Stopping feature.");
						await EndExecution();
					} else if (delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						var time = await _tbotOgameBridge.GetDateTime();
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval;
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoResearch.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoResearch.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						DoLog(LogLevel.Information, $"Next AutoResearch check at {newTime.ToString()}");
					} else if (delayResearch > 0) {
						if (delayResearch >= int.MaxValue)
							delayResearch = int.MaxValue;
						var time = await _tbotOgameBridge.GetDateTime();
						var newTime = time.AddMilliseconds(delayResearch);
						ChangeWorkerPeriod(delayResearch);
						DoLog(LogLevel.Information, $"Next AutoResearch check at {newTime.ToString()}");
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
						var time = await _tbotOgameBridge.GetDateTime();
						if (celestial.ID != 0) {
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions) as Planet;
							var incomingFleets = _calculationService.GetIncomingFleets(celestial, _tbotInstance.UserData.fleets);
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
						ChangeWorkerPeriod(interval);
						DoLog(LogLevel.Information, $"Next AutoResearch check at {newTime.ToString()}");
					}
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return (
					(bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoResearch.Active
				);
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "AutoResearch";
		}
		public override Feature GetFeature() {
			return Feature.BrainAutoResearch;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoResearch;
		}
	}
}
