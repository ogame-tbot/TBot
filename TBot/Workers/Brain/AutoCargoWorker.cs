using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers.Brain {
	public class AutoCargoWorker : WorkerBase {
		public AutoCargoWorker(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) :
			base(parentInstance, fleetScheduler, helpersService) {
		}

		public AutoCargoWorker(ITBotMain parentInstance) :
			base(parentInstance) {
		}

		protected override async Task Execute() {
			bool stop = false;
			try {
				DoLog(LogLevel.Information, "Running autocargo...");

				if ((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.Active) {
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
					List<Celestial> celestialsToExclude = _helpersService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoCargo.Exclude, _tbotInstance.UserData.celestials);

					foreach (Celestial celestial in (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.RandomOrder ? _tbotInstance.UserData.celestials.Shuffle().ToList() : _tbotInstance.UserData.celestials) {
						if (celestialsToExclude.Has(celestial)) {
							DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}

						var tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Fast);

						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.SkipIfIncomingTransport && _helpersService.IsThereTransportTowardsCelestial(tempCelestial, _tbotInstance.UserData.fleets)) {
							DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
							continue;
						}

						tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Productions);
						if (tempCelestial.HasProduction()) {
							DoLog(LogLevel.Warning, $"Skipping {tempCelestial.ToString()}: there is already a production ongoing.");
							foreach (Production production in tempCelestial.Productions) {
								Buildables productionType = (Buildables) production.ID;
								DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are already in production.");
							}
							continue;
						}
						tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Constructions);
						if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
							Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
							DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");

						}

						tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Ships);
						tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Resources);
						var capacity = _helpersService.CalcFleetCapacity(tempCelestial.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						if (tempCelestial.Coordinate.Type == Celestials.Moon && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.ExcludeMoons) {
							DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
							continue;
						}
						long neededCargos;
						Buildables preferredCargoShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoCargo.CargoType, true, out preferredCargoShip)) {
							DoLog(LogLevel.Warning, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredCargoShip = Buildables.SmallCargo;
						}
						if (capacity <= tempCelestial.Resources.TotalResources && (bool) _tbotInstance.InstanceSettings.Brain.AutoCargo.LimitToCapacity) {
							long difference = tempCelestial.Resources.TotalResources - capacity;
							int oneShipCapacity = _helpersService.CalcShipCapacity(preferredCargoShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
							neededCargos = (long) Math.Round((float) difference / (float) oneShipCapacity, MidpointRounding.ToPositiveInfinity);
							DoLog(LogLevel.Information, $"{difference.ToString("N0")} more capacity is needed, {neededCargos} more {preferredCargoShip.ToString()} are needed.");
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
								DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: Building {neededCargos}x{preferredCargoShip.ToString()}");
							else {
								var buildableCargos = _helpersService.CalcMaxBuildableNumber(preferredCargoShip, tempCelestial.Resources);
								DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{preferredCargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
								neededCargos = buildableCargos;
							}

							if (neededCargos > 0) {
								try {
									await _tbotInstance.OgamedInstance.BuildShips(tempCelestial, preferredCargoShip, neededCargos);
									DoLog(LogLevel.Information, "Production succesfully started.");
								} catch {
									DoLog(LogLevel.Warning, "Unable to start ship production.");
								}
							}

							tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Productions);
							foreach (Production production in tempCelestial.Productions) {
								Buildables productionType = (Buildables) production.ID;
								DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are in production.");
							}
						} else {
							DoLog(LogLevel.Information, $"{tempCelestial.ToString()}: No ships will be built.");
						}

						newCelestials.Remove(celestial);
						newCelestials.Add(tempCelestial);
					}
					_tbotInstance.UserData.celestials = newCelestials;
				} else {
					DoLog(LogLevel.Information, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"Unable to complete autocargo: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						DoLog(LogLevel.Information, $"Stopping feature.");
						await EndExecution();
					} else {
						var time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
						var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoCargo.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoCargo.CheckIntervalMax);
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						DoLog(LogLevel.Information, $"Next capacity check at {newTime.ToString()}");
						await TBotOgamedBridge.CheckCelestials(_tbotInstance);
					}
				}
			}
		}

		public override string GetWorkerName() {
			return "AutoCargo";
		}
		public override Feature GetFeature() {
			return Feature.BrainAutobuildCargo;
		}

		public override LogSender GetLogSender() {
			return LogSender.Brain;
		}
	}
}
