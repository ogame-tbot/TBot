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
using Tbot.Common.Settings;

namespace Tbot.Workers {
	public class AutoFarmWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public AutoFarmWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.AutoFarm.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "AutoFarm";
		}
		public override Feature GetFeature() {
			return Feature.AutoFarm;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoFarm;
		}

		private async Task PruneOldReports() {
			var newTime = await _tbotOgameBridge.GetDateTime();
			var removeReports = _tbotInstance.UserData.farmTargets.Where(t => t.State == FarmState.AttackSent || (t.Report != null && DateTime.Compare(t.Report.Date.AddMinutes((double) _tbotInstance.InstanceSettings.AutoFarm.KeepReportFor), newTime) < 0)).ToList();
			foreach (var remove in removeReports) {
				var updateReport = remove;
				updateReport.State = FarmState.ProbesPending;
				updateReport.Report = null;
				_tbotInstance.UserData.farmTargets.Remove(remove);
				_tbotInstance.UserData.farmTargets.Add(updateReport);
			}
		}

		private async Task<Dictionary<int, long>> GetCelestialProbes() {
			var localCelestials = await _tbotOgameBridge.UpdateCelestials();
			Dictionary<int, long> celestialProbes = new Dictionary<int, long>();
			foreach (var celestial in localCelestials) {
				Celestial tempCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);
				tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);
				celestialProbes.Add(tempCelestial.ID, tempCelestial.Ships.EspionageProbe);
			}
			return celestialProbes;
		}

		private bool ShouldExcludeSystem(int galaxy, int system) {
			bool excludeSystem = false;
			foreach (var exclude in _tbotInstance.InstanceSettings.AutoFarm.Exclude) {
				bool hasPosition = false;
				foreach (var value in exclude.Keys)
					if (value == "Position")
						hasPosition = true;
				if ((int) exclude.Galaxy == galaxy && (int) exclude.System == system && !hasPosition) {
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping system {system.ToString()}: system in exclude list.");
					excludeSystem = true;
					break;
				}
			}
			return excludeSystem;
		}

		private async Task<List<Celestial>> GetScannedTargetsFromGalaxy(int galaxy, int system) {
			var galaxyInfo = await _ogameService.GetGalaxyInfo(galaxy, system);
			var planets = galaxyInfo.Planets.Where(p => p != null && p.Inactive && !p.Administrator && !p.Banned && !p.Vacation);
			List<Celestial> scannedTargets = planets.Cast<Celestial>().ToList();
			await _fleetScheduler.UpdateFleets();
			//Remove all targets that are currently under attack (necessary if bot or instance is restarted)
			scannedTargets.RemoveAll(t => _tbotInstance.UserData.fleets.Any(f => f.Destination.IsSame(t.Coordinate) && f.Mission == Missions.Attack));
			return scannedTargets;
		}

		private bool IsTargetInMinimumRank(Celestial planet, List<Celestial> scannedTargets) {
			if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinimumPlayerRank") && _tbotInstance.InstanceSettings.AutoFarm.MinimumPlayerRank != 0) {
				int rank = 1;
				if (planet.Coordinate.Type == Celestials.Planet) {
					rank = (planet as Planet).Player.Rank;
				} else {
					if (scannedTargets.Any(t => t.HasCoords(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet)))) {
						rank = (scannedTargets.Single(t => t.HasCoords(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet))) as Planet).Player.Rank;
					}
				}
				if ((int) _tbotInstance.InstanceSettings.AutoFarm.MinimumPlayerRank < rank) {

					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Skipping {planet.ToString()}: player has rank {rank} that is less than minimum configured {(int) _tbotInstance.InstanceSettings.AutoFarm.MinimumPlayerRank}.");
					return false;
				}
			}
			return true;
		}

		private bool ShouldExcludePlanet(Celestial planet) {
			bool excludePlanet = false;
			foreach (var exclude in _tbotInstance.InstanceSettings.AutoFarm.Exclude) {
				bool hasPosition = false;
				foreach (var value in exclude.Keys)
					if (value == "Position")
						hasPosition = true;
				if ((int) exclude.Galaxy == planet.Coordinate.Galaxy && (int) exclude.System == planet.Coordinate.System && hasPosition && (int) exclude.Position == planet.Coordinate.Position) {
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {planet.ToString()}: celestial in exclude list.");
					excludePlanet = true;
					break;
				}
			}
			return excludePlanet;
		}

		private void AddMoons(List<Celestial> scannedTargets) {
			foreach (var t in scannedTargets.ToList()) {
				var planet = t as Planet;
				if (planet == null)
					continue;
				if (planet.Moon != null) {
					Celestial tempCelestial = planet.Moon;
					tempCelestial.Coordinate = t.Coordinate;
					tempCelestial.Coordinate.Type = Celestials.Moon;
					scannedTargets.Add(tempCelestial);
				}
			}
		}

		private FarmTarget CheckDuplicatesAndGetExisting(Celestial planet) {
			var exists = _tbotInstance.UserData.farmTargets.Where(t => t != null && t.Celestial.HasCoords(planet.Coordinate)).ToList();
			if (exists.Count() > 1) {
				// It can exist if was processed in a previous execution
				//Remove all except the first to be able to continue
				var firstExisting = exists.First();
				_tbotInstance.UserData.farmTargets.RemoveAll(c => c.Celestial.HasCoords(planet.Coordinate));
				_tbotInstance.UserData.farmTargets.Add(firstExisting);
				return firstExisting;
			}
			else if (exists.Count() == 1) {
				return exists.First();
			}
			return null;
		}

		private FarmTarget GetFarmTarget(Celestial planet) {
			// Check if planet with coordinates exists already in _tbotInstance.UserData.farmTargets list.
			var target = CheckDuplicatesAndGetExisting(planet);

			if (target == null) {
				// Does not exist, add to _tbotInstance.UserData.farmTargets list, set state to probes pending.
				target = new(planet, FarmState.ProbesPending);
				_tbotInstance.UserData.farmTargets.Add(target);
			} else {
				// Already exists, update _tbotInstance.UserData.farmTargets list with updated planet.
				target.Celestial = planet;

				if (target.State == FarmState.Idle)
					target.State = FarmState.ProbesPending;

				// If target marked not suitable based on a non-expired espionage report, skip probing.
				if (target.State == FarmState.NotSuitable && target.Report != null) {
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Target {planet.ToString()} marked as Not Suitable. Skipping...");
					return null;
				}

				// If probes are already sent or if an attack is pending, skip probing.
				if (target.State == FarmState.ProbesSent || target.State == FarmState.AttackPending) {
					_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Target {planet.ToString()} marked as {target.State.ToString()}. Skipping...");
					return null;
				}
			}
			return target;
		}

		private int GetNeededProbes(FarmTarget target) {
			int neededProbes = (int) _tbotInstance.InstanceSettings.AutoFarm.NumProbes;
			if (target.State == FarmState.ProbesRequired)
				neededProbes *= 3;
			if (target.State == FarmState.FailedProbesRequired)
				neededProbes *= 9;
			return neededProbes;
		}

		private class SpyOriginResult {
			public SpyOriginResult(Celestial origin, int freeSlots) {
				Origin = origin;
				BackIn = 0;
				FreeSlots = freeSlots;
			}

			public SpyOriginResult(Celestial origin, int backin, int freeSlots) {
				Origin = origin;
				BackIn = backin;
				FreeSlots = freeSlots;
			}
			public Celestial Origin { get; set; }
			public int BackIn { get; set; }
			public int FreeSlots { get; set; }
		}

		private async Task<SpyOriginResult> GetBestOrigin(List<Celestial> closestCelestials,
			Dictionary<int, long> celestialProbes,
			FarmTarget target,
			int neededProbes,
			int slotsToLeaveFree,
			int freeSlots) {
			//Set first celestial as default best Origin
			SpyOriginResult bestOrigin = new SpyOriginResult(closestCelestials.First(), int.MaxValue, freeSlots);
			foreach (var closest in closestCelestials) {
				// Update ships of the current planet
				var tempCelestial = await _tbotOgameBridge.UpdatePlanet(closest, UpdateTypes.Ships);
				celestialProbes.Remove(closest.ID);
				celestialProbes.Add(closest.ID, tempCelestial.Ships.EspionageProbe);

				if (celestialProbes[closest.ID] >= neededProbes) {
					//There are enough probes so it's the best origin and we can stop searching
					bestOrigin = new SpyOriginResult(closest, freeSlots);
					break;
				}

				// No probes available in this celestial
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();

				// If there are no free slots, update the minimum time to wait for current missions return.
				// If there are no free slots, wait for probes to come back to current celestial.
				if (freeSlots <= slotsToLeaveFree) {
					var espionageMissions = _calculationService.GetMissionsInProgress(closest.Coordinate, Missions.Spy, _tbotInstance.UserData.fleets);
					if (espionageMissions.Any()) {
						var returningProbes = espionageMissions.Sum(f => f.Ships.EspionageProbe);
						if (celestialProbes[closest.ID] + returningProbes >= neededProbes) {
							var returningFleets = espionageMissions.OrderBy(f => f.BackIn).ToArray();
							long probesCount = 0;
							for (int i = 0; i < returningFleets.Length; i++) {
								probesCount += returningFleets[i].Ships.EspionageProbe;
								if (probesCount >= neededProbes) {
									if (bestOrigin.BackIn > returningFleets[i].BackIn)
										bestOrigin = new SpyOriginResult(closest, returningFleets[i].BackIn ?? int.MaxValue, freeSlots);
									break;
								}
							}
						}
					}
				} else {
					//If no bestOrigin detected, the total number of probes is not enough but there are free slots, then calculate if can be built from this celestial
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Cannot spy {target.Celestial.Coordinate.ToString()} from {closest.Coordinate.ToString()}, insufficient probes ({celestialProbes[closest.ID]}/{neededProbes}).");
					if (bestOrigin.BackIn < int.MaxValue)
						continue;

					//If there is no bestOrigin, check if can be a good origin (it has enough resources to build probes)
					tempCelestial = await _tbotOgameBridge.UpdatePlanet(closest, UpdateTypes.Constructions);
					if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
						Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");
						continue;
					}
					await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds), _ct);
					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Productions);
					if (tempCelestial.Productions.Any(p => p.ID == (int) Buildables.EspionageProbe)) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: Probes already building.");
						continue;
					}

					await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds), _ct);
					var buildProbes = neededProbes - celestialProbes[closest.ID];
					var cost = _calculationService.CalcPrice(Buildables.EspionageProbe, (int) buildProbes);
					tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
					if (tempCelestial.Resources.IsEnoughFor(cost)) {
						bestOrigin = new SpyOriginResult(closest, int.MaxValue, freeSlots);
					}
				}
			}

			if (bestOrigin.BackIn != 0) {
				if (bestOrigin.BackIn != int.MaxValue) {
					int interval = (int) ((1000 * bestOrigin.BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds));
					if (interval < 0)
						interval = 1000;
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Not enough free slots {freeSlots}/{slotsToLeaveFree}. Waiting {TimeSpan.FromMilliseconds(interval)} for probes to return...");
					await Task.Delay(interval, _ct);
					bestOrigin.Origin = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Ships);
					bestOrigin.FreeSlots++;
				}
			}

			return bestOrigin;
		}

		private async Task<int> WaitForFreeSlots(int freeSlots, int slotsToLeaveFree) {
			if (freeSlots <= slotsToLeaveFree) {
				_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
				freeSlots = _tbotInstance.UserData.slots.Free;
			}

			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			while (freeSlots <= slotsToLeaveFree) {
				// No slots available, wait for first fleet of any mission type to return.
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				if (_tbotInstance.UserData.fleets.Any()) {
					int interval = (int) ((1000 * _tbotInstance.UserData.fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));
					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Out of fleet slots. Waiting {TimeSpan.FromMilliseconds(interval)} for fleet to return...");
					await Task.Delay(interval, _ct);
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					freeSlots = _tbotInstance.UserData.slots.Free;
				} else {
					_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
					throw new Exception("No fleet slots available and no fleets returning!"); //TODO: Create custom exception
				}
			}
			return freeSlots;
		}

		protected override async Task Execute() {
			bool stop = false;
			try {

				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Running autofarm...");

				if ((bool) _tbotInstance.InstanceSettings.AutoFarm.Active) {
					// If not enough slots are free, the farmer cannot run.
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();

					int freeSlots = _tbotInstance.UserData.slots.Free;
					int slotsToLeaveFree = (int) _tbotInstance.InstanceSettings.AutoFarm.SlotsToLeaveFree;
					if (freeSlots <= slotsToLeaveFree) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to start auto farm, no slots available");
						return;
					}

					try {
						// Prune all reports older than KeepReportFor and all reports of state AttackSent: information no longer actual.
						await PruneOldReports();

						// Keep local record of _tbotInstance.UserData.celestials, to be updated by autofarmer itself, to reduce ogamed calls.
						var celestialProbes = await GetCelestialProbes();

						// Keep track of number of targets probed.
						int numProbed = 0;

						/// Galaxy scanning + target probing.
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Detecting farm targets...");
						bool stopAutoFarm = false;

						foreach (var range in _tbotInstance.InstanceSettings.AutoFarm.ScanRange) {
							if (stopAutoFarm)
								break;
							if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "TargetsProbedBeforeAttack") && ((int) _tbotInstance.InstanceSettings.AutoFarm.TargetsProbedBeforeAttack != 0) && numProbed >= (int) _tbotInstance.InstanceSettings.AutoFarm.TargetsProbedBeforeAttack) {
								break;
							}

							int galaxy = (int) range.Galaxy;
							int startSystem = (int) range.StartSystem;
							int endSystem = (int) range.EndSystem;

							// Loop from start to end system.
							for (var system = startSystem; system <= endSystem; system++) {

								if (stopAutoFarm)
									break;
								if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "TargetsProbedBeforeAttack") && ((int) _tbotInstance.InstanceSettings.AutoFarm.TargetsProbedBeforeAttack != 0) && numProbed >= (int) _tbotInstance.InstanceSettings.AutoFarm.TargetsProbedBeforeAttack) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Maximum number of targets to probe reached, proceeding to attack.");
									break;
								}

								// Check excluded system.
								bool excludeSystem = ShouldExcludeSystem(galaxy, system);
								if (excludeSystem)
									continue;

								var scannedTargets = await GetScannedTargetsFromGalaxy(galaxy, system);

								_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Found {scannedTargets.Count} targets on System {galaxy}:{system}");

								if (!scannedTargets.Any())
									continue;

								if ((bool) _tbotInstance.InstanceSettings.AutoFarm.ExcludeMoons == false) {
									AddMoons(scannedTargets);
								}

								// Add each planet that has inactive status to _tbotInstance.UserData.farmTargets.
								foreach (Celestial planet in scannedTargets) {
									if (stopAutoFarm)
										break;
									// Check if target is below set minimum rank.
									if (!IsTargetInMinimumRank(planet, scannedTargets)) {
										continue;
									}

									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "TargetsProbedBeforeAttack") &&
										_tbotInstance.InstanceSettings.AutoFarm.TargetsProbedBeforeAttack != 0 && numProbed >= (int) _tbotInstance.InstanceSettings.AutoFarm.TargetsProbedBeforeAttack) {
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Maximum number of targets to probe reached, proceeding to attack.");
										break;
									}

									// Check excluded planet.
									if (ShouldExcludePlanet(planet))
										continue;

									// Manage existing Target or generate a new one. If should not be processed returns null
									var target = GetFarmTarget(planet);
									if (target == null)
										continue;

									// Send spy probe from closest celestial with available probes to the target.
									List<Celestial> tempCelestials = (_tbotInstance.InstanceSettings.AutoFarm.Origin.Length > 0) ? _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.AutoFarm.Origin, _tbotInstance.UserData.celestials) : _tbotInstance.UserData.celestials;
									List<Celestial> closestCelestials = tempCelestials
										.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
										.OrderBy(c => _calculationService.CalcDistance(c.Coordinate, target.Celestial.Coordinate, _tbotInstance.UserData.serverData)).ToList();


									int neededProbes = GetNeededProbes(target);

									await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds), _ct);

									_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
									var probesInMission = _tbotInstance.UserData.fleets.Select(c => c.Ships).Sum(c => c.EspionageProbe);

									//Calculate best origin
									var bestOrigin = await GetBestOrigin(closestCelestials,
										celestialProbes,
										target,
										neededProbes,
										slotsToLeaveFree,
										freeSlots);

									freeSlots = bestOrigin.FreeSlots;

									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Best origin found: {bestOrigin.Origin.Name} ({bestOrigin.Origin.Coordinate.ToString()})");

									if (_calculationService.GetMissionsInProgress(bestOrigin.Origin.Coordinate, Missions.Spy, _tbotInstance.UserData.fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate))) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Probes already on route towards {target.ToString()}.");
										continue;
									}
									if (_calculationService.GetMissionsInProgress(bestOrigin.Origin.Coordinate, Missions.Attack, _tbotInstance.UserData.fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate) && f.ReturnFlight == false)) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Attack already on route towards {target.ToString()}.");
										continue;
									}

									// If local record indicate not enough espionage probes are available, update record to make sure this is correct.
									if (celestialProbes[bestOrigin.Origin.ID] < neededProbes) {
										var tempCelestial = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Ships);
										celestialProbes.Remove(bestOrigin.Origin.ID);
										celestialProbes.Add(bestOrigin.Origin.ID, tempCelestial.Ships.EspionageProbe);
									}

									if (celestialProbes[bestOrigin.Origin.ID] < neededProbes) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Insufficient probes ({celestialProbes[bestOrigin.Origin.ID]}/{neededProbes}).");
										if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "BuildProbes") && _tbotInstance.InstanceSettings.AutoFarm.BuildProbes == true) {
											//Check if probes can be built
											var tempCelestial = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Constructions);
											if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
												Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");
												break;
											}

											tempCelestial = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Productions);
											if (tempCelestial.Productions.Any(p => p.ID == (int) Buildables.EspionageProbe)) {
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: Probes already building.");
												break;
											}

											var buildProbes = neededProbes - celestialProbes[bestOrigin.Origin.ID];
											var cost = _calculationService.CalcPrice(Buildables.EspionageProbe, (int) buildProbes);
											tempCelestial = await _tbotOgameBridge.UpdatePlanet(bestOrigin.Origin, UpdateTypes.Resources);
											if (tempCelestial.Resources.IsEnoughFor(cost)) {
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {buildProbes}x{Buildables.EspionageProbe.ToString()}");

												try {
													await _ogameService.BuildShips(tempCelestial, Buildables.EspionageProbe, buildProbes);
													tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
													int interval = (int) (_calculationService.CalcProductionTime(Buildables.EspionageProbe, (int) buildProbes, _tbotInstance.UserData.serverData, tempCelestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
													_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Production succesfully started. Waiting {TimeSpan.FromMilliseconds(interval)} for build order to finish...");
													await Task.Delay(interval, _ct);
												} catch {
													_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to start ship production.");
												}
											} else {
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Not enough resources to build probes.");
												_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
												var spyMissions = _calculationService.GetMissionsInProgress(bestOrigin.Origin.Coordinate, Missions.Spy, _tbotInstance.UserData.fleets);
												if (spyMissions.Any()) {
													var spyMissionToWait = spyMissions.OrderBy(c => c.BackIn).First();
													int interval = (int) ((1000 * spyMissionToWait.BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));
													_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Waiting {TimeSpan.FromMilliseconds(interval)} for spy mission to return...");
													await Task.Delay(interval);
												} else {
													_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"There are not enough probes or resources to build them. Skipping this AutoFarm Execution.");
													stopAutoFarm = true;
													break;
												}
											}
										}
									}

									if (celestialProbes[bestOrigin.Origin.ID] >= neededProbes) {

										Ships ships = new();
										ships.Add(Buildables.EspionageProbe, neededProbes);

										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Spying {target.ToString()} from {bestOrigin.Origin.ToString()} with {neededProbes} probes.");

										var fleetId = (int) SendFleetCode.GenericError;
										int retryCount = 0;
										int maxRetryCount = 5;
										do {
											_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
											freeSlots = _tbotInstance.UserData.slots.Free;
											freeSlots = await WaitForFreeSlots(freeSlots, slotsToLeaveFree);
											fleetId = await _fleetScheduler.SendFleet(bestOrigin.Origin, ships, target.Celestial.Coordinate, Missions.Spy, Speeds.HundredPercent);
											if (fleetId == (int)SendFleetCode.NotEnoughSlots) {
												_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Another worker took the slot, waiting again for a free slot... Retry count: {retryCount}/{maxRetryCount}");
											}
											retryCount++;
										} while (fleetId == (int) SendFleetCode.NotEnoughSlots && retryCount <= maxRetryCount);

										if (fleetId > (int) SendFleetCode.GenericError) {
											freeSlots--;
											numProbed++;
											celestialProbes[bestOrigin.Origin.ID] -= neededProbes;

											if (target.State == FarmState.ProbesRequired || target.State == FarmState.FailedProbesRequired)
												continue;

											_tbotInstance.UserData.farmTargets.Remove(target);
											target.State = FarmState.ProbesSent;
											_tbotInstance.UserData.farmTargets.Add(target);

											continue;
										} else if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										} else if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Unable to achieve a free slot after {retryCount} retries.");
											continue;
										} else {
											continue;
										}
									}
								}
							}
						}
					} catch (Exception e) {
						_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Exception: {e.Message}");
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to parse scan range");
					}

					// Wait for all espionage fleets to return.
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					Fleet firstReturning = _calculationService.GetLastReturningEspionage(_tbotInstance.UserData.fleets);
					if (firstReturning != null) {
						int interval = (int) ((1000 * firstReturning.BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Waiting {TimeSpan.FromMilliseconds(interval)} for all probes to return...");
						await Task.Delay(interval, _ct);
					}

					_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Processing espionage reports of found inactives...");

					/// Process reports.
					await AutoFarmProcessReports();

					/// Send attacks.
					List<FarmTarget> attackTargets;
					if (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Metal")
						attackTargets = _tbotInstance.UserData.farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(_tbotInstance.UserData.userInfo.Class).Metal).ToList();
					else if (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Crystal")
						attackTargets = _tbotInstance.UserData.farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(_tbotInstance.UserData.userInfo.Class).Crystal).ToList();
					else if (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Deuterium")
						attackTargets = _tbotInstance.UserData.farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(_tbotInstance.UserData.userInfo.Class).Deuterium).ToList();
					else
						attackTargets = _tbotInstance.UserData.farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources).ToList();

					if (attackTargets.Count() > 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "Attacking suitable farm targets...");
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, "No suitable targets found.");
						return;
					}

					Buildables cargoShip = Buildables.LargeCargo;
					if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.AutoFarm.CargoType, true, out cargoShip)) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to parse cargoShip. Falling back to default LargeCargo");
						cargoShip = Buildables.LargeCargo;
					}
					if (cargoShip == Buildables.Null) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip is Null");
						return;
					}
					if (cargoShip == Buildables.EspionageProbe && _tbotInstance.UserData.serverData.ProbeCargo == 0) {
						_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip set to EspionageProbe, but this universe does not have probe cargo.");
						return;
					}

					_tbotInstance.UserData.researches = await _tbotOgameBridge.UpdateResearches();
					_tbotInstance.UserData.celestials = await _tbotOgameBridge.UpdateCelestials();
					int attackTargetsCount = 0;
					decimal lootFuelRatio = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinLootFuelRatio") ? (decimal) _tbotInstance.InstanceSettings.AutoFarm.MinLootFuelRatio : (decimal) 0.0001;
					decimal speed = 0;
					foreach (FarmTarget target in attackTargets) {
						attackTargetsCount++;
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attacking target {attackTargetsCount}/{attackTargets.Count()} at {target.Celestial.Coordinate.ToString()} for {target.Report.Loot(_tbotInstance.UserData.userInfo.Class).TransportableResources}.");
						var loot = target.Report.Loot(_tbotInstance.UserData.userInfo.Class);
						var numCargo = _calculationService.CalcShipNumberForPayload(loot, cargoShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "CargoSurplusPercentage") && (double) _tbotInstance.InstanceSettings.AutoFarm.CargoSurplusPercentage > 0) {
							numCargo = (long) Math.Round(numCargo + (numCargo / 100 * (double) _tbotInstance.InstanceSettings.AutoFarm.CargoSurplusPercentage), 0);
						}
						var attackingShips = new Ships().Add(cargoShip, numCargo);

						List<Celestial> tempCelestials = (_tbotInstance.InstanceSettings.AutoFarm.Origin.Length > 0) ? _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.AutoFarm.Origin, _tbotInstance.UserData.celestials) : _tbotInstance.UserData.celestials;
						List<Celestial> closestCelestials = tempCelestials
							.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
							.OrderBy(c => _calculationService.CalcDistance(c.Coordinate, target.Celestial.Coordinate, _tbotInstance.UserData.serverData))
							.ToList();

						Celestial fromCelestial = null;
						foreach (var c in closestCelestials) {
							var tempCelestial = await _tbotOgameBridge.UpdatePlanet(c, UpdateTypes.Ships);
							tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
							if (tempCelestial.Ships != null && tempCelestial.Ships.GetAmount(cargoShip) >= (numCargo + _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep)) {
								// TODO Future: If fleet composition is changed, update ships passed to CalcFlightTime.
								speed = 0;
								if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinLootFuelRatio") && _tbotInstance.InstanceSettings.AutoFarm.MinLootFuelRatio != 0) {
									long maxFlightTime = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime") ? (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime : 86400;
									var optimalSpeed = _calculationService.CalcOptimalFarmSpeed(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(_tbotInstance.UserData.userInfo.Class), lootFuelRatio, maxFlightTime, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class);
									if (optimalSpeed == 0) {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

									} else {
										_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
										speed = optimalSpeed;
									}
								}
								if (speed == 0) {
									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "FleetSpeed") && _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed > 0) {
										speed = (int) _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed / 10;
										if (!_calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Any(s => s == speed)) {
											_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
											speed = Speeds.HundredPercent;
										}
									} else {
										speed = Speeds.HundredPercent;
									}
								}
								FleetPrediction prediction = _calculationService.CalcFleetPrediction(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class);

								if (
									(
										!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime") ||
										(long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime == 0 ||
										prediction.Time <= (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime
									) &&
									prediction.Fuel <= tempCelestial.Resources.Deuterium
								) {
									fromCelestial = tempCelestial;
									break;
								}
							}
						}

						if (fromCelestial == null) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"No origin celestial available near destination {target.Celestial.ToString()} with enough cargo ships.");
							// TODO Future: If prefered cargo ship is not available or not sufficient capacity, combine with other cargo type.
							foreach (var closest in closestCelestials) {
								Celestial tempCelestial = closest;
								tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);
								tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
								// TODO Future: If fleet composition is changed, update ships passed to CalcFlightTime.
								speed = 0;
								if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "FleetSpeed") && _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed > 0) {
									speed = (int) _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed / 10;
									if (!_calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Any(s => s == speed)) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
										speed = Speeds.HundredPercent;
									}
								} else {
									speed = 0;
									if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinLootFuelRatio") && _tbotInstance.InstanceSettings.AutoFarm.MinLootFuelRatio != 0) {
										long maxFlightTime = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime") ? (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime : 86400;
										var optimalSpeed = _calculationService.CalcOptimalFarmSpeed(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(_tbotInstance.UserData.userInfo.Class), lootFuelRatio, maxFlightTime, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class);
										if (optimalSpeed == 0) {
											_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

										} else {
											_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
											speed = optimalSpeed;
										}
									}
									if (speed == 0) {
										if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "FleetSpeed") && _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed > 0) {
											speed = (int) _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed / 10;
											if (!_calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Any(s => s == speed)) {
												_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
												speed = Speeds.HundredPercent;
											}
										} else {
											speed = Speeds.HundredPercent;
										}
									}
								}
								FleetPrediction prediction = _calculationService.CalcFleetPrediction(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class);

								if (
									tempCelestial.Ships.GetAmount(cargoShip) < numCargo + (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep &&
									tempCelestial.Resources.Deuterium >= prediction.Fuel &&
									(
										!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime") ||
										(long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime == 0 ||
										prediction.Time <= (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime
									)
								) {
									if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "BuildCargos") && _tbotInstance.InstanceSettings.AutoFarm.BuildCargos == true) {
										var neededCargos = numCargo + (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep - tempCelestial.Ships.GetAmount(cargoShip);
										var cost = _calculationService.CalcPrice(cargoShip, (int) neededCargos);
										if (tempCelestial.Resources.IsEnoughFor(cost)) {
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {neededCargos}x{cargoShip.ToString()}");
										} else {
											var buildableCargos = _calculationService.CalcMaxBuildableNumber(cargoShip, tempCelestial.Resources);
											_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{cargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
											neededCargos = buildableCargos;
										}

										try {
											await _ogameService.BuildShips(tempCelestial, cargoShip, neededCargos);
											tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
											int interval = (int) (_calculationService.CalcProductionTime(cargoShip, (int) neededCargos, _tbotInstance.UserData.serverData, tempCelestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
											_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Production succesfully started. Waiting {TimeSpan.FromMilliseconds(interval)} for build order to finish...");
											await Task.Delay(interval, _ct);
										} catch {
											_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, "Unable to start ship production.");
										}
									}

									if (tempCelestial.Ships.GetAmount(cargoShip) - (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep < (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToSend) {
										_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Insufficient {cargoShip.ToString()} on {tempCelestial.Coordinate}, require {numCargo + (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep} {cargoShip.ToString()}.");
										continue;
									}

									numCargo = tempCelestial.Ships.GetAmount(cargoShip) - (long) _tbotInstance.InstanceSettings.AutoFarm.MinCargosToKeep;
									fromCelestial = tempCelestial;
									break;
								}
							}
						}

						if (fromCelestial == null) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. No suitable origin celestial available near the destination.");
							continue;
						}

						// Only execute update slots if our local copy indicates we have run out.
						if (freeSlots <= slotsToLeaveFree) {
							_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
							freeSlots = _tbotInstance.UserData.slots.Free;
						}

						while (freeSlots <= slotsToLeaveFree) {
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							// No slots free, wait for first fleet to come back.
							if (_tbotInstance.UserData.fleets.Any()) {
								int interval = (int) ((1000 * _tbotInstance.UserData.fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
								if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxWaitTime") && (int) _tbotInstance.InstanceSettings.AutoFarm.MaxWaitTime != 0 && interval > (int) _tbotInstance.InstanceSettings.AutoFarm.MaxWaitTime * 1000) {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Out of fleet slots. Time to wait greater than set {(int) _tbotInstance.InstanceSettings.AutoFarm.MaxWaitTime} seconds. Stopping autofarm.");
									return;
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Out of fleet slots. Waiting {TimeSpan.FromMilliseconds(interval)} for first fleet to return...");
									await Task.Delay(interval, _ct);
									_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
									freeSlots = _tbotInstance.UserData.slots.Free;
								}
							} else {
								_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
								return;
							}
						}

						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						List<Fleet> slotUsed = _tbotInstance.UserData.fleets
							.Where(fleet => fleet.Mission == Missions.Attack)
							.ToList();

						if (_tbotInstance.UserData.slots.Free > slotsToLeaveFree && slotUsed.Count() < (int) _tbotInstance.InstanceSettings.AutoFarm.MaxSlots) {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attacking {target.ToString()} from {fromCelestial} with {numCargo} {cargoShip.ToString()}.");
							Ships ships = new();

							speed = 0;
							if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MinLootFuelRatio") && _tbotInstance.InstanceSettings.AutoFarm.MinLootFuelRatio != 0) {
								long maxFlightTime = SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "MaxFlightTime") ? (long) _tbotInstance.InstanceSettings.AutoFarm.MaxFlightTime : 86400;
								var optimalSpeed = _calculationService.CalcOptimalFarmSpeed(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(_tbotInstance.UserData.userInfo.Class), lootFuelRatio, maxFlightTime, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class);
								if (optimalSpeed == 0) {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

								} else {
									_tbotInstance.log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
									speed = optimalSpeed;
								}
							}
							if (speed == 0) {
								if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.AutoFarm, "FleetSpeed") && _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed > 0) {
									speed = (int) _tbotInstance.InstanceSettings.AutoFarm.FleetSpeed / 10;
									if (!_calculationService.GetValidSpeedsForClass(_tbotInstance.UserData.userInfo.Class).Any(s => s == speed)) {
										_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
										speed = Speeds.HundredPercent;
									}
								} else {
									speed = Speeds.HundredPercent;
								}
							}

							var fleetId = await _fleetScheduler.SendFleet(fromCelestial, attackingShips, target.Celestial.Coordinate, Missions.Attack, speed);

							if (fleetId > (int) SendFleetCode.GenericError) {
								freeSlots--;
							} else if (fleetId == (int) SendFleetCode.AfterSleepTime) {
								stop = true;
								return;
							}

							_tbotInstance.UserData.farmTargets.Remove(target);
							target.State = FarmState.AttackSent;
							_tbotInstance.UserData.farmTargets.Add(target);
						} else {
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. {_tbotInstance.UserData.slots.Free} free, {slotsToLeaveFree} required.");
							return;
						}
					}
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, $"AutoFarm Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
			} finally {
				_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attacked targets: {_tbotInstance.UserData.farmTargets.Where(t => t.State == FarmState.AttackSent).Count()}");
				_tbotInstance.UserData.farmTargets.RemoveAll(t => t.State == FarmState.ProbesSent); //At this point no ProbesSent should remain in farmTargets
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Stopping feature.");
						await EndExecution();
					} else {
						var time = await _tbotOgameBridge.GetDateTime();
						var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoFarm.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoFarm.CheckIntervalMax);
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Next autofarm check at {newTime.ToString()}");
						await _tbotOgameBridge.CheckCelestials();
					}
				}
			}
		}

		/// <summary>
		/// Checks all received espionage reports and updates _tbotInstance.UserData.farmTargets to reflect latest data retrieved from reports.
		/// </summary>
		private async Task AutoFarmProcessReports() {
			// TODO Future: Read espionage reports in separate thread (concurently with probing itself).
			// TODO Future: Check if probes were destroyed, blacklist target if so to avoid additional kills.
			List<EspionageReportSummary> summaryReports = await _ogameService.GetEspionageReports();
			foreach (var summary in summaryReports) {
				if (summary.Type == EspionageReportType.Action)
					continue;

				try {
					var report = await _ogameService.GetEspionageReport(summary.ID);
					if (DateTime.Compare(report.Date.AddMinutes((double) _tbotInstance.InstanceSettings.AutoFarm.KeepReportFor), await _tbotOgameBridge.GetDateTime()) < 0) {
						await _ogameService.DeleteReport(report.ID);
						continue;
					}

					if (_tbotInstance.UserData.farmTargets.Any(t => t.HasCoords(report.Coordinate))) {
						FarmTarget target;
						var matchingTarget = _tbotInstance.UserData.farmTargets.Where(t => t.HasCoords(report.Coordinate));
						if (matchingTarget.Count() == 0) {
							// Report received of planet not in _tbotInstance.UserData.farmTargets. If inactive: add, otherwise: ignore.
							if (!report.IsInactive)
								continue;
							//Get corresponding planet. Add to target list.
							var galaxyInfo = await _ogameService.GetGalaxyInfo(report.Coordinate.Galaxy, report.Coordinate.System);
							var planet = galaxyInfo.Planets.FirstOrDefault(p => p != null && p.Inactive && !p.Administrator && !p.Banned && !p.Vacation && p.HasCoords(report.Coordinate));
							if (planet != null) {
								target = GetFarmTarget(planet);
								if (target == null)
									continue;
							} else {
								continue;
							}
						} else {
							target = matchingTarget.First();
						}
						var newFarmTarget = target;

						if (target.Report != null && DateTime.Compare(report.Date, target.Report.Date) < 0) {
							// Target has a more recent report. Delete report.
							await _ogameService.DeleteReport(report.ID);
							continue;
						}

						newFarmTarget.Report = report;
						if (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Metal" && report.Loot(_tbotInstance.UserData.userInfo.Class).Metal > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources
							|| _tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Crystal" && report.Loot(_tbotInstance.UserData.userInfo.Class).Crystal > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources
							|| _tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "Deuterium" && report.Loot(_tbotInstance.UserData.userInfo.Class).Deuterium > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources
							|| (_tbotInstance.InstanceSettings.AutoFarm.PreferedResource == "" && report.Loot(_tbotInstance.UserData.userInfo.Class).TotalResources > _tbotInstance.InstanceSettings.AutoFarm.MinimumResources)) {
							if (!report.HasFleetInformation || !report.HasDefensesInformation) {
								if (target.State == FarmState.ProbesRequired)
									newFarmTarget.State = FarmState.FailedProbesRequired;
								else if (target.State == FarmState.FailedProbesRequired)
									newFarmTarget.State = FarmState.NotSuitable;
								else
									newFarmTarget.State = FarmState.ProbesRequired;

								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Need more probes on {report.Coordinate}. Loot: {report.Loot(_tbotInstance.UserData.userInfo.Class)}");
							} else if (report.IsDefenceless()) {
								newFarmTarget.State = FarmState.AttackPending;
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Attack pending on {report.Coordinate}. Loot: {report.Loot(_tbotInstance.UserData.userInfo.Class)}");
							} else {
								newFarmTarget.State = FarmState.NotSuitable;
								_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - defences present.");
							}
						} else {
							newFarmTarget.State = FarmState.NotSuitable;
							_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - insufficient loot ({report.Loot(_tbotInstance.UserData.userInfo.Class)})");
						}

						_tbotInstance.UserData.farmTargets.Remove(target);
						_tbotInstance.UserData.farmTargets.Add(newFarmTarget);
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not scanned by TBot, ignoring...");
					}
				} catch (Exception e) {
					_tbotInstance.log(LogLevel.Error, LogSender.AutoFarm, $"AutoFarmProcessReports Exception: {e.Message}");
					_tbotInstance.log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
					continue;
				}
			}

			await _ogameService.DeleteAllEspionageReports();

		}
	}
}
