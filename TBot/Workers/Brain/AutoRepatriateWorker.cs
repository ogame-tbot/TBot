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
	public class AutoRepatriateWorker : WorkerBase {
		public AutoRepatriateWorker(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) :
			base(parentInstance, fleetScheduler, helpersService) {
		}

		public AutoRepatriateWorker(ITBotMain parentInstance) :
			base(parentInstance) {
		}

		protected override async Task Execute(CancellationToken ct) {
			bool stop = false;
			bool delay = false;
			try {
				DoLog(LogLevel.Information, "Repatriating resources...");

				if (_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, "Skipping: Sleep Mode Active!");
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
								DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial in exclude list.");
								continue;
							}
							if (celestial.Coordinate.IsSame(destinationCoordinate)) {
								DoLog(LogLevel.Information, $"Skipping {celestial.ToString()}: celestial is the target.");
								continue;
							}

							var tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, celestial, UpdateTypes.Fast);

							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							if ((bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.SkipIfIncomingTransport && _helpersService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets) && (!timers.TryGetValue("TelegramCollect", out Timer value2))) {
								DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
								continue;
							}
							if (celestial.Coordinate.Type == Celestials.Moon && (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.ExcludeMoons) {
								DoLog(LogLevel.Information, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
								continue;
							}

							tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Resources);
							tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Ships);

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
								DoLog(LogLevel.Warning, $"Skipping {tempCelestial.ToString()}: there are no {preferredShip.ToString()}");
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
						DoLog(LogLevel.Warning, "Skipping autorepatriate: unable to parse custom destination");
					}
				} else {
					DoLog(LogLevel.Information, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"Unable to complete repatriate: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (timers.TryGetValue("TelegramCollect", out Timer val)) {
						val.Dispose();
						timers.Remove("TelegramCollect");
					} else {
						if (stop) {
							DoLog(LogLevel.Information, $"Stopping feature.");
						} else if (delay) {
							DoLog(LogLevel.Information, $"Delaying...");
							_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
							var time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
							long interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							ChangeWorkerPeriod(interval);
							DoLog(LogLevel.Information, $"Next repatriate check at {newTime.ToString()}");
						} else {
							var time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
							var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CheckIntervalMax);
							if (interval <= 0)
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							ChangeWorkerPeriod(interval);
							DoLog(LogLevel.Information, $"Next repatriate check at {newTime.ToString()}");
						}
					}
					await TBotOgamedBridge.CheckCelestials(_tbotInstance);
				}
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
