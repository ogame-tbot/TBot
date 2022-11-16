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
	public class AutoRepatriateWorker : WorkerBase, IAutoRepatriateWorker {
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public AutoRepatriateWorker(ITBotMain parentInstance,
			IFleetScheduler fleetScheduler,
			ICalculationService calculationService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_calculationService = calculationService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override async Task Execute() {
			await CollectImpl(false);
		}

		public async Task Collect() {
			await CollectImpl(true);
		}

		public async Task CollectDeut(long MinAmount = 0) {
			_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
			long TotalDeut = 0;
			Coordinate destinationCoordinate;

			Celestial cel = _tbotInstance.UserData.celestials
					.Unique()
					.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Galaxy)
					.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.System)
					.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Position)
					.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Type))
					.SingleOrDefault() ?? new() { ID = 0 };

			if (cel.ID == 0) {
				await _tbotInstance.SendTelegramMessage("Error! Could not parse auto repatriate Celestial from JSON InstanceSettings. Need <code>/editsettings</code>");
				return;
			} else {
				destinationCoordinate = cel.Coordinate;
			}

			foreach (Celestial celestial in _tbotInstance.UserData.celestials.ToList()) {
				if (celestial.Coordinate.IsSame(destinationCoordinate)) {
					continue;
				}
				if (celestial is Moon) {
					continue;
				}

				var tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Fast);
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();

				tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Resources);
				tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Ships);

				Buildables preferredShip = Buildables.LargeCargo;
				if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
					preferredShip = Buildables.LargeCargo;
				}
				Resources payload = tempCelestial.Resources;
				payload.Metal = 0;
				payload.Crystal = 0;
				payload.Food = 0;

				if ((long) tempCelestial.Resources.Deuterium < (long) MinAmount || payload.IsEmpty()) {
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

					if ((long) payload.TotalResources >= (long) MinAmount) {
						var fleetId = await _fleetScheduler.SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
						if (fleetId == (int) SendFleetCode.AfterSleepTime) {
							continue;
						}
						if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
							continue;
						}

						TotalDeut += payload.Deuterium;
					}
				} else {
					continue;
				}
			}

			if (TotalDeut > 0) {
				await _tbotInstance.SendTelegramMessage($"{TotalDeut} Deuterium sent.");
			} else {
				await _tbotInstance.SendTelegramMessage("No resources sent");
			}
		}

		public async Task CollectImpl(bool fromTelegram) {
			bool stop = false;
			bool delay = false;
			try {
				DoLog(LogLevel.Information, "Repatriating resources...");

				if (fromTelegram) {
					DoLog(LogLevel.Information, $"Telegram collect initated..");
				}
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
					List<Celestial> celestialsToExclude = _calculationService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Exclude, _tbotInstance.UserData.celestials);

						foreach (Celestial celestial in (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.RandomOrder ? _tbotInstance.UserData.celestials.Shuffle().ToList() : _tbotInstance.UserData.celestials.OrderBy(c => _calculationService.CalcDistance(c.Coordinate, destinationCoordinate, _tbotInstance.UserData.serverData)).ToList()) {
							if (celestialsToExclude.Has(celestial)) {
								DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
								continue;
							}
							if (celestial.Coordinate.IsSame(destinationCoordinate)) {
								DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial is the target.");
								continue;
							}

							var tempCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);

						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.SkipIfIncomingTransport && _calculationService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets) && (!fromTelegram)) {
							DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
							continue;
						}
						if (celestial.Coordinate.Type == Celestials.Moon && (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.ExcludeMoons) {
							DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
							continue;
						}

							tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
							tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);

						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
							DoLog(LogLevel.Warning, "Unable to parse CargoType. Falling back to default SmallCargo");
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
							DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: resources under set limit");
							continue;
						}

							long idealShips = _calculationService.CalcShipNumberForPayload(payload, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

							Ships ships = new();
							if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
								if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
									ships.Add(preferredShip, idealShips);
								} else {
									ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
								}
								payload = _calculationService.CalcMaxTransportableResources(ships, payload, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

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
							DoLog(LogLevel.Warning, $"Skipping {tempCelestial.ToString()}: there are no {preferredShip.ToString()}");
						}

						newCelestials.Remove(celestial);
						newCelestials.Add(tempCelestial);
					}
					_tbotInstance.UserData.celestials = newCelestials;
					//send notif only if sent via telegram
					if (fromTelegram) {
						if ((TotalMet > 0) || (TotalCri > 0) || (TotalDeut > 0)) {
							await _tbotInstance.SendTelegramMessage($"Resources sent!:\n{TotalMet} Metal\n{TotalCri} Crystal\n{TotalDeut} Deuterium");
						} else {
							await _tbotInstance.SendTelegramMessage("No resources sent");
						}
					}
				} else {
					DoLog(LogLevel.Warning, "Skipping autorepatriate: unable to parse custom destination");
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Unable to complete repatriate: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (fromTelegram) {

					} else {
						if (stop) {
							DoLog(LogLevel.Information, $"Stopping feature.");
							await EndExecution();
						} else if (delay) {
							DoLog(LogLevel.Information, $"Delaying...");
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							var time = await _tbotOgameBridge.GetDateTime();
							long interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							ChangeWorkerPeriod(interval);
							DoLog(LogLevel.Information, $"Next repatriate check at {newTime.ToString()}");
						} else {
							var time = await _tbotOgameBridge.GetDateTime();
							var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMax);
							if (interval <= 0)
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							ChangeWorkerPeriod(interval);
							DoLog(LogLevel.Information, $"Next repatriate check at {newTime.ToString()}");
						}
					}
					await _tbotOgameBridge.CheckCelestials();
				}
			}
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return (
					(bool) _tbotInstance.InstanceSettings.Brain.Active &&
					(bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Active
				);
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "AutoRepatriate";
		}
		public override Feature GetFeature() {
			return Feature.BrainAutoRepatriate;
		}

		public override LogSender GetLogSender() {
			return LogSender.Brain;
		}
	}
}
