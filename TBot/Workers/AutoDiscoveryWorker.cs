using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
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
					RankSlotsPriority presentFeature = AutoDiscoveryRank;
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
					if (reservedSlots + otherSlots > _tbotInstance.UserData.slots.Total - (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree) {
						DoLog(LogLevel.Information, $"Unable to send fleet for {presentFeature.Feature.ToString()}, too many slots are already used/reserved");
						MaxSlots = 0;
					} else if (MaxSlots > tempsValue) {
						MaxSlots = tempsValue;
						DoLog(LogLevel.Information, $"Less slots available than {presentFeature.Feature.ToString()}, many slots are already used/reserved -> steping back to {MaxSlots} instead of {presentFeature.MaxSlots}");
					}


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
					
					if ((bool) _tbotInstance.InstanceSettings.SleepMode.Active) {
						DateTime.TryParse((string) _tbotInstance.InstanceSettings.SleepMode.GoToSleep, out DateTime goToSleep);
						DateTime.TryParse((string) _tbotInstance.InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp);
						DateTime time = await _tbotOgameBridge.GetDateTime();
						if (GeneralHelper.ShouldSleep(time, goToSleep, wakeUp)) {
							DoLog(LogLevel.Warning, "Unable to send discovery fleet: bed time has passed");
							stop = true;
							return;
						}
					}

					List<Coordinate> possibleDestinations = new();
					for (int i = 1; i <= _tbotInstance.UserData.serverData.Systems; i++) {
						for (int j = 1; j <= 15; j++) {
							possibleDestinations.Add(new Coordinate() {
								Galaxy = origin.Coordinate.Galaxy,
								System = i,
								Position = j
							});
						}
					}
					possibleDestinations = possibleDestinations
						.Shuffle()
						.OrderBy(c => c.Position)
						.OrderBy(c => c.System)
						.OrderBy(c => _calculationService.CalcDistance(origin.Coordinate, c, _tbotInstance.UserData.serverData))
						.ToList();

					while (possibleDestinations.Count > 0 && _tbotInstance.UserData.fleets.Where(s => s.Mission == Missions.Discovery).Count() < MaxSlots && _tbotInstance.UserData.slots.Free > (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree) {
						Coordinate dest = possibleDestinations.First();
						possibleDestinations.Remove(dest);

						Coordinate blacklistedCoord = _tbotInstance.UserData.discoveryBlackList.Keys
							.Where(c => c.Galaxy == dest.Galaxy)
							.Where(c => c.System == dest.System)
							.Where(c => c.Position == dest.Position)
							.SingleOrDefault() ?? null;
						if (blacklistedCoord != null) {
							if (_tbotInstance.UserData.discoveryBlackList.Single(d => d.Key.Galaxy == dest.Galaxy && d.Key.System == dest.System && d.Key.Position == dest.Position).Value > DateTime.Now) {
								//DoLog(LogLevel.Information, $"Skipping {dest.ToString()} because it's blacklisted until {_tbotInstance.UserData.discoveryBlackList[blacklistedCoord].ToString()}");
								skips++;
								if (skips >= _tbotInstance.UserData.serverData.Systems * 15) {
									DoLog(LogLevel.Information, $"Galaxy depleted: stopping");
									stop = true;
									break;
								} else {
									continue;
								}
							} else {
								_tbotInstance.UserData.discoveryBlackList.Remove(blacklistedCoord);
							}
						}

						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
						if (!origin.Resources.IsEnoughFor(new Resources { Metal = 5000, Crystal = 1000, Deuterium = 500 })) {
							DoLog(LogLevel.Warning, $"Failed to send discovery fleet from {origin.ToString()}: not enough resources.");
							return;
						}
						
						var result = await _ogameService.SendDiscovery(origin, dest);
						if (!result) {
							failures++;
							DoLog(LogLevel.Warning, $"Failed to send discovery fleet to {dest.ToString()} from {origin.ToString()}.");
							_tbotInstance.UserData.discoveryBlackList.Add(dest, DateTime.Now.AddDays(1));
						}
						else {
							DoLog(LogLevel.Information, $"Sent discovery fleet to {dest.ToString()} from {origin.ToString()}.");
							_tbotInstance.UserData.discoveryBlackList.Add(dest, DateTime.Now.AddDays(7));
						}						

						if (failures >= (int) _tbotInstance.InstanceSettings.AutoDiscovery.MaxFailures) {
							DoLog(LogLevel.Warning, $"Max failures reached");
							break;
						}
						
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
						if (_tbotInstance.UserData.slots.Free <= _tbotInstance.InstanceSettings.General.SlotsToLeaveFree) {
							DoLog(LogLevel.Information, $"AutoDiscoveryWorker: No slots left, dealying");
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
					long interval = (_tbotInstance.UserData.fleets.Where(fleet => fleet.Mission == Missions.Discovery).OrderByDescending(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					if (delay) {
						DoLog(LogLevel.Information, $"Delaying...");
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						interval = (_tbotInstance.UserData.fleets.Where(fleet => fleet.Mission == Missions.Discovery).OrderByDescending(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
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
