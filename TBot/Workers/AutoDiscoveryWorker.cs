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
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public class AutoDiscoveryWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ICalculationService _calculationService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public AutoDiscoveryWorker(ITBotMain parentInstance,
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

		protected override async Task Execute() {
			bool delay = false;
			bool stop = false;
			int failures = 0;
			int skips = 0;
			var rand = new Random();
			try {
				if (_tbotInstance.UserData.discoveryBlackList == null) {
					_tbotInstance.UserData.discoveryBlackList = new Dictionary<Coordinate, DateTime>();
				}
				if (!_tbotInstance.UserData.isSleeping) {
					DoLog(LogLevel.Information, $"Starting AutoDiscovery...");
					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();

					Celestial origin = _tbotInstance.UserData.celestials
						.Unique()
						.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.AutoDiscovery.Origin.Galaxy)
						.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.AutoDiscovery.Origin.System)
						.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.AutoDiscovery.Origin.Position)
						.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.AutoDiscovery.Origin.Type))
						.SingleOrDefault() ?? new() { ID = 0 };
					if (origin.ID == 0) {
						stop = true;
						DoLog(LogLevel.Warning, "Unable to parse AutoDiscovery origin");
						return;
					}

					int modulo = 0;
					int step = 0;
					int system = (int) _tbotInstance.InstanceSettings.AutoDiscovery.Origin.System;
					int position = 0;
					Coordinate dest = new();

					while (_tbotInstance.UserData.fleets.Where(s => s.Mission == Missions.Discovery).Count() < (int) _tbotInstance.InstanceSettings.AutoDiscovery.MaxSlots && _tbotInstance.UserData.slots.Free >= 1) {
						
						if ((bool) _tbotInstance.InstanceSettings.AutoDiscovery.ProgressiveRange) {
							position++;
							if (position > 15) {
								if (modulo%2 == 0)
									step++;
								
								modulo++;
								position = 1;
							}
							if (modulo%2 > 0)
								system = (int) _tbotInstance.InstanceSettings.AutoDiscovery.Origin.System - step;
							else if (modulo%2 == 0)
								system = (int) _tbotInstance.InstanceSettings.AutoDiscovery.Origin.System + step;
							
							if (system < 1)
								system = 499 + system;
							else if (system > 499)
								system = 1 + (system - 499);

							dest.Galaxy = (int) _tbotInstance.InstanceSettings.AutoDiscovery.Origin.Galaxy;
							dest.System = system;
							dest.Position = position;
						} else {
							dest.Galaxy = (int) _tbotInstance.InstanceSettings.AutoDiscovery.Range.Galaxy;
							dest.System = rand.Next((int) _tbotInstance.InstanceSettings.AutoDiscovery.Range.StartSystem, (int) _tbotInstance.InstanceSettings.AutoDiscovery.Range.EndSystem + 1);
							dest.Position = rand.Next(1, 16);
						}
						Coordinate blacklistedCoord = _tbotInstance.UserData.discoveryBlackList.Keys
							.Where(c => c.Galaxy == dest.Galaxy)
							.Where(c => c.System == dest.System)
							.Where(c => c.Position == dest.Position)
							.SingleOrDefault() ?? null;
						if (blacklistedCoord != null) {
							if (_tbotInstance.UserData.discoveryBlackList.Single(d => d.Key.Galaxy == dest.Galaxy && d.Key.System == dest.System && d.Key.Position == dest.Position).Value > DateTime.Now) {
								DoLog(LogLevel.Information, $"Skipping {dest.ToString()} because it's blacklisted until {_tbotInstance.UserData.discoveryBlackList[blacklistedCoord].ToString()}");
								skips++;
								if ((bool) _tbotInstance.InstanceSettings.AutoDiscovery.ProgressiveRange)
									position = 16;
								if ((skips >= ((int) _tbotInstance.InstanceSettings.AutoDiscovery.Range.EndSystem - (int) _tbotInstance.InstanceSettings.AutoDiscovery.Range.StartSystem) * 15) && !((bool) _tbotInstance.InstanceSettings.AutoDiscovery.ProgressiveRange)) {
									DoLog(LogLevel.Information, $"Range depleted: stopping");
									stop = true;
									break;
								} else {
									continue;
								}
							} else {
								_tbotInstance.UserData.discoveryBlackList.Remove(blacklistedCoord);
							}
						}
						var result = await _ogameService.SendDiscovery(origin, dest);
						if (!result) {
							failures++;
							DoLog(LogLevel.Warning, $"Failed to send discovery fleet to {dest.ToString()} from {origin.ToString()}.");
							if (modulo%2 == 0)
								step++;							
							modulo++;
							position = 0;
						} else {
							DoLog(LogLevel.Information, $"Sent discovery fleet to {dest.ToString()} from {origin.ToString()}.");
						}

						_tbotInstance.UserData.discoveryBlackList.Add(dest, DateTime.Now.AddDays(1));
						
						dest = new();

						if (failures >= (int) _tbotInstance.InstanceSettings.AutoDiscovery.MaxFailures) {
							DoLog(LogLevel.Warning, $"Max failures reached");
							break;
						}
						
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
						if (_tbotInstance.UserData.slots.Free <= 1 && (int) _tbotInstance.UserData.fleets.Where(fleet => fleet.Mission == Missions.Discovery).Count() < (int) _tbotInstance.InstanceSettings.AutoDiscovery.MaxSlots) {
							DoLog(LogLevel.Information, $"AutoDiscoveryWorker: No slots left, dealying");
							delay = true;
							break;
						}
					}
					
					List<Fleet> orderedFleets = _tbotInstance.UserData.fleets
						.Where(fleet => fleet.Mission == Missions.Discovery)
						.OrderByDescending(fleet => fleet.BackIn)
						.ToList();
					long interval = (int) ((1000 * orderedFleets.First().BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.AboutFiveMinutes));
					DateTime time = await _tbotOgameBridge.GetDateTime();
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.AboutFiveMinutes);
					DateTime newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
					await _tbotOgameBridge.CheckCelestials();
				}
				else {
					stop = true;
				}
			} catch (Exception ex) {
				DoLog(LogLevel.Error, "AutoDiscovery exception");
				DoLog(LogLevel.Warning, ex.ToString());
			}
			finally {
				if (stop) {
					DoLog(LogLevel.Information, $"Stopping feature.");
					await EndExecution();
				} else {
					if (delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval;
						try {
							interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoDiscovery.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoDiscovery.CheckIntervalMax);
						}
					
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var time = await _tbotOgameBridge.GetDateTime();
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						DoLog(LogLevel.Information, $"Next AutoDiscovery check at {newTime.ToString()}");
					}
				}
				await _tbotOgameBridge.CheckCelestials();
			}			
		}

		public override bool IsWorkerEnabledBySettings() {
			try {
				return 
					(bool) _tbotInstance.InstanceSettings.AutoDiscovery.Active
				;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "AutoDiscovery";
		}
		public override Feature GetFeature() {
			return Feature.AutoDiscovery;
		}

		public override LogSender GetLogSender() {
			return LogSender.AutoDiscovery;
		}
	}
}
