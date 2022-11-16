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

namespace Tbot.Workers {
	public class ColonizeWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public ColonizeWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;	
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.AutoColonize.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "Colonize";
		}
		public override Feature GetFeature() {
			return Feature.Colonize;
		}

		public override LogSender GetLogSender() {
			return LogSender.Colonize;
		}

		protected override async Task Execute() {
			bool stop = false;
			bool delay = false;
			try {

				if ((bool) _tbotInstance.InstanceSettings.AutoColonize.Active) {
					long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMax);
					_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "Checking if a new planet is needed...");

					_tbotInstance.UserData.researches = await _tbotOgameBridge.UpdateResearches();
					var maxPlanets = _calculationService.CalcMaxPlanets(_tbotInstance.UserData.researches);
					var currentPlanets = _tbotInstance.UserData.celestials.Where(c => c.Coordinate.Type == Celestials.Planet).Count();
					var slotsToLeaveFree = (int) (_tbotInstance.InstanceSettings.AutoColonize.SlotsToLeaveFree ?? 0);
					if (currentPlanets + slotsToLeaveFree < maxPlanets) {
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "A new planet is needed.");

						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Colonize && !f.ReturnFlight)) {
							_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "Colony Ship(s) already in flight.");
							interval = _tbotInstance.UserData.fleets
								.OrderBy(f => f.ArriveIn)
							.Last(f => !f.ReturnFlight)
								.ArriveIn * 1000;
						} else {
							Coordinate originCoords = new(
								(int) _tbotInstance.InstanceSettings.AutoColonize.Origin.Galaxy,
							(int) _tbotInstance.InstanceSettings.AutoColonize.Origin.System,
								(int) _tbotInstance.InstanceSettings.AutoColonize.Origin.Position,
								Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.AutoColonize.Origin.Type)
							);
							Celestial origin = _tbotInstance.UserData.celestials.Single(c => c.HasCoords(originCoords));
							await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);

							var neededColonizers = maxPlanets - currentPlanets - slotsToLeaveFree;

							if (origin.Ships.ColonyShip >= neededColonizers) {
								List<Coordinate> targets = new();
								foreach (var t in _tbotInstance.InstanceSettings.AutoColonize.Targets) {
									Coordinate targetCoords = new(
										(int) t.Galaxy,
										(int) t.System,
										(int) t.Position,
										Celestials.Planet
									);
									targets.Add(targetCoords);
								}
								List<Coordinate> filteredTargets = new();
								foreach (Coordinate t in targets) {
									if (_tbotInstance.UserData.celestials.Any(c => c.HasCoords(t))) {
										continue;
									}
									GalaxyInfo galaxy = await _ogameService.GetGalaxyInfo(t);
									if (galaxy.Planets.Any(p => p != null && p.HasCoords(t))) {
										continue;
									}
									filteredTargets.Add(t);
								}
								if (filteredTargets.Count() > 0) {
									filteredTargets = filteredTargets
										.OrderBy(t => _calculationService.CalcDistance(origin.Coordinate, t, _tbotInstance.UserData.serverData))
										.Take(maxPlanets - currentPlanets)
										.ToList();
									foreach (var target in filteredTargets) {
										Ships ships = new() { ColonyShip = 1 };
										var fleetId = await _fleetScheduler.SendFleet(origin, ships, target, Missions.Colonize, Speeds.HundredPercent);

										if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										}
										if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											delay = true;
											return;
										}
									}
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "No valid coordinate in target list.");
								}
							} else {
								await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Productions);
								await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Facilities);
								if (origin.Productions.Any()) {
									_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed. {origin.Productions.Where(p => p.ID == (int) Buildables.ColonyShip).Sum(p => p.Nbr)} colony ship(s) already in production.");
									foreach (var prod in origin.Productions) {
										if (prod == origin.Productions.First()) {
											interval += (int) _calculationService.CalcProductionTime((Buildables) prod.ID, prod.Nbr - 1, _tbotInstance.UserData.serverData, origin.Facilities) * 1000;
										} else {
											interval += (int) _calculationService.CalcProductionTime((Buildables) prod.ID, prod.Nbr, _tbotInstance.UserData.serverData, origin.Facilities) * 1000;
										}
										if (prod.ID == (int) Buildables.ColonyShip) {
											break;
										}
									}
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed.");
									await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
									var cost = _calculationService.CalcPrice(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip);
									if (origin.Resources.IsEnoughFor(cost)) {
										await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Constructions);
										if (origin.HasConstruction() && (origin.Constructions.BuildingID == (int) Buildables.Shipyard || origin.Constructions.BuildingID == (int) Buildables.NaniteFactory)) {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Unable to build colony ship: {((Buildables) origin.Constructions.BuildingID).ToString()} is in construction");
											interval = (long) origin.Constructions.BuildingCountdown * (long) 1000;
										} else if (origin.HasProduction()) {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Unable to build colony ship: there is already something in production");
											interval = (long) _calculationService.CalcProductionTime((Buildables) origin.Productions.First().ID, origin.Productions.First().Nbr - 1, _tbotInstance.UserData.serverData, origin.Facilities) * 1000;
										} else if (origin.Facilities.Shipyard >= 4 && _tbotInstance.UserData.researches.ImpulseDrive >= 3) {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Building {neededColonizers - origin.Ships.ColonyShip}....");
											await _ogameService.BuildShips(origin, Buildables.ColonyShip, neededColonizers - origin.Ships.ColonyShip);
											interval = (int) _calculationService.CalcProductionTime(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip, _tbotInstance.UserData.serverData, origin.Facilities) * 1000;
										} else {
											_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Requirements to build colony ship not met");
										}
									} else {
										_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Not enough resources to build {neededColonizers} colony ship(s). Needed: {cost.TransportableResources} - Available: {origin.Resources.TransportableResources}");
									}
								}
							}
						}
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, "No new planet is needed.");
					}

					DateTime time = await _tbotOgameBridge.GetDateTime();
					if (interval <= 0) {
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					}

					DateTime newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.Colonize, $"HandleColonize exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Colonize, $"Stacktrace: {e.StackTrace}");
				long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMax);
				DateTime time = await _tbotOgameBridge.GetDateTime();
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(interval);
				_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Stopping feature.");
						await EndExecution();
					}
					if (delay) {
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Delaying...");
						var time = await _tbotOgameBridge.GetDateTime();
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval;
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoColonize.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						_tbotInstance.log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
					}
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}
	}
}
