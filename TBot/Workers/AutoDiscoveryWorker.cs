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
			var rand = new Random();
			try {
				if (!_tbotInstance.UserData.isSleeping) {
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

					while (_tbotInstance.UserData.fleets.Where(s => s.Mission == Missions.Discovery).Count() < (int) _tbotInstance.InstanceSettings.AutoDiscovery.MaxSlots) {
						Coordinate dest = new();
						dest.Galaxy = _tbotInstance.InstanceSettings.AutoDiscovery.Range.Galaxy;
						dest.System = rand.Next(_tbotInstance.InstanceSettings.AutoDiscovery.Range.StartSystem, _tbotInstance.InstanceSettings.AutoDiscovery.Range.EndSystem + 1);
						dest.Position = rand.Next(1, 16);

						if (_tbotInstance.UserData.discoveryBlackList.ContainsKey(dest)) {
							if (_tbotInstance.UserData.discoveryBlackList[dest] > DateTime.Now) {
								DoLog(LogLevel.Information, $"Skipping {dest} because it's blacklisted until {_tbotInstance.UserData.discoveryBlackList[dest]}");
								continue;
							} else {
								_tbotInstance.UserData.discoveryBlackList.Remove(dest);
							}
						}
						
						var result = await _ogameService.SendDiscovery(origin, dest);

						if (!result) {
							failures++;
							DoLog(LogLevel.Warning, $"Failed to send discovery fleet to {dest.ToString()} from {origin.ToString()}.");
						}
						else {
							DoLog(LogLevel.Information, $"Sent discovery fleet to {dest.ToString()} from {origin.ToString()}.");
						}
						_tbotInstance.UserData.discoveryBlackList.Add(dest, DateTime.Now.AddDays(1));

						if (failures >= _tbotInstance.InstanceSettings.AutoDiscovery.MaxFailures) {
							DoLog(LogLevel.Warning, $"AutoDiscoveryWorker: Max failures reached, stopping");
							break;
						}
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();

						if (_tbotInstance.UserData.slots.Free <= 1) {
							DoLog(LogLevel.Warning, $"AutoDiscoveryWorker: No slots left, dealying");
							delay = true;
							break;
						}
					}
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
					long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoDiscovery.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoDiscovery.CheckIntervalMax);
					if (delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					}
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					var time = await _tbotOgameBridge.GetDateTime();
					var newTime = time.AddMilliseconds(interval);
					ChangeWorkerPeriod(interval);
					DoLog(LogLevel.Information, $"Next AutoDiscovery check at {newTime.ToString()}");
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
