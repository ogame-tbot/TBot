using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using Tbot.Helpers;
using TBot.Model;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using Tbot.Includes;
using System.Timers;
using TBot.Ogame.Infrastructure;
using Tbot.Common.Settings;

namespace Tbot.Workers {
	internal class DefenderWorker : WorkerBase {
		private static readonly ConcurrentDictionary<int, DateTime> _handledAttackIds = new();
		private static readonly TimeSpan _handledAttackTtl = TimeSpan.FromMinutes(60);

		private readonly IFleetScheduler _fleetScheduler;
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public DefenderWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ITBotOgamedBridge tbotOgameBridge)
			: base(parentInstance) {
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
		}

		protected override async Task Execute() {
			try {
				DoLog(LogLevel.Information, "Checking attacks...");

				await FakeActivity();
				_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
				bool isUnderAttack = await _ogameService.IsUnderAttack();
				DateTime time = await _tbotOgameBridge.GetDateTime();
				if (isUnderAttack) {
					if ((bool) _tbotInstance.InstanceSettings.Defender.Alarm.Active)
						await Task.Run(() => ConsoleHelpers.PlayAlarm(), _ct);
					DoLog(LogLevel.Warning, "ENEMY ACTIVITY!!!");
					_tbotInstance.UserData.attacks = await _ogameService.GetAttacks();
					foreach (AttackerFleet attack in _tbotInstance.UserData.attacks) {
						await HandleAttack(attack);
					}
				} else {
					DoLog(LogLevel.Information, "Your empire is safe");
				}
				long interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Defender.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Defender.CheckIntervalMax);
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);

				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(TimeSpan.FromMilliseconds(interval));
				DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			} catch (Exception e) {
				DoLog(LogLevel.Warning, $"An error has occurred while checking for attacks: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(TimeSpan.FromMilliseconds(interval));
				DoLog(LogLevel.Information, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			} finally {

			}
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (bool) _tbotInstance.InstanceSettings.Defender.Active;
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "Defender";
		}
		public override Feature GetFeature() {
			return Feature.Defender;
		}

		public override LogSender GetLogSender() {
			return LogSender.Defender;
		}


		private async Task FakeActivity() {

			Celestial celestial;
			Celestial randomCelestial;
			var randomActivity = (bool) _tbotInstance.InstanceSettings.Defender.RandomActivity;

			if (randomActivity == false) {
				celestial = _tbotInstance.UserData.celestials
				.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) _tbotInstance.InstanceSettings.Defender.Home.Galaxy)
				.Where(c => c.Coordinate.System == (int) _tbotInstance.InstanceSettings.Defender.Home.System)
				.Where(c => c.Coordinate.Position == (int) _tbotInstance.InstanceSettings.Defender.Home.Position)
				.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Defender.Home.Type))
				.SingleOrDefault() ?? new() { ID = 0 };

				if (celestial.ID != 0) {
					DoLog(LogLevel.Information, $"Check from Home ({celestial.Coordinate.Galaxy}:{celestial.Coordinate.System}:{celestial.Coordinate.Position} {celestial.Coordinate.Type})");
					celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Defences);
				}
			} else {
				randomCelestial = _tbotInstance.UserData.celestials.Shuffle().FirstOrDefault() ?? new() { ID = 0 };

				if (randomCelestial.ID != 0) {
					DoLog(LogLevel.Information, $"Check from Random Celestial");
					randomCelestial = await _tbotOgameBridge.UpdatePlanet(randomCelestial, UpdateTypes.Defences);
				}
			}
			return;
		}

		private async Task HandleAttack(AttackerFleet attack) {
			try {
				var nowUtc = DateTime.UtcNow;
				foreach (var kv in _handledAttackIds.ToArray()) {
					if (nowUtc - kv.Value > _handledAttackTtl)
						_handledAttackIds.TryRemove(kv.Key, out _);
				}
				if (attack != null && attack.ID != 0 &&
					_handledAttackIds.TryGetValue(attack.ID, out var seenAt) &&
					(nowUtc - seenAt) <= _handledAttackTtl) {
					DoLog(LogLevel.Information, $"Attack {attack.ID} already handled recently; skipping duplicate actions.");
					return;
				}
				if (attack != null && attack.ID != 0) {
					_handledAttackIds[attack.ID] = nowUtc;
				}

			if (_tbotInstance.UserData.celestials.Count() == 0) {
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(TimeSpan.FromMilliseconds(interval));
				DoLog(LogLevel.Warning, "Unable to handle attack at the moment: bot is still getting account info.");
				DoLog(LogLevel.Information,  $"Next check at {newTime.ToString()}");
				return;
			}

			Celestial attackedCelestial = _tbotInstance.UserData.celestials.Unique().FirstOrDefault(planet => planet.HasCoords(attack.Destination));
			if (attackedCelestial == null) {
				DoLog(LogLevel.Warning, $"Unable to handle attack {attack.ID}: attacked celestial not found in account data.");
				return;
			}
			attackedCelestial = await _tbotOgameBridge.UpdatePlanet(attackedCelestial, UpdateTypes.Ships);
			try {
				if ((bool)_tbotInstance.InstanceSettings.Defender.IgnoreAttackIfIHave.Active) {
					attackedCelestial = await _tbotOgameBridge.UpdatePlanet(attackedCelestial, UpdateTypes.Resources);
				}
			} catch {
			}


			try {
				var wlObj = _tbotInstance.InstanceSettings.Defender.WhiteList;
				IEnumerable<long> whiteListIds = wlObj switch {
					long[] a => a,
					int[] a => a.Select(x => (long)x),
					IEnumerable<long> e => e,
					IEnumerable<int> e => e.Select(x => (long)x),
					_ => Enumerable.Empty<long>()
				};

				if (!whiteListIds.Any() && wlObj != null) {
					DoLog(LogLevel.Debug, $"Defender WhiteList present but unsupported type: {wlObj.GetType().FullName}");
				}

				foreach (var playerId in whiteListIds) {
					if (attack.AttackerID == playerId) {
						DoLog(LogLevel.Information, $"Attack {attack.ID.ToString()} skipped: attacker {attack.AttackerName} whitelisted.");
						return;
					}
				}
			} catch (Exception ex) {
				DoLog(LogLevel.Warning, $"An error has occurred while checking Defender WhiteList: {ex.Message}");
			}

			try {
				if (attack.MissionType == Missions.MissileAttack) {
					if (_tbotInstance.IsNotificationEnabled(_tbotInstance.InstanceSettings.Defender)) {
						await _tbotInstance.SendTelegramMessage($"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} with IPM!");
					}
					DoLog(LogLevel.Information, $"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} with IPM!");
					if (
						!SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender, "DefendFromMissiles") ||
						(SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender, "DefendFromMissiles") && (bool) _tbotInstance.InstanceSettings.Defender.DefendFromMissiles)
					) {
						Celestial defenderCelestial = attackedCelestial;
						if (attackedCelestial.Coordinate.Type == Celestials.Moon) {
							 defenderCelestial = _tbotInstance.UserData.celestials.Unique().SingleOrDefault(planet => planet.HasCoords(new Coordinate {
								 Galaxy = attackedCelestial.Coordinate.Galaxy,
								 System = attackedCelestial.Coordinate.System,
								 Position = attackedCelestial.Coordinate.Position,
								 Type = Celestials.Planet
							}));
						}
						if (defenderCelestial == null) {
							DoLog(LogLevel.Warning, $"Missile attack detected on {attack.Destination.ToString()} but planet celestial was not found in account data. Skipping missile defence.");
							return;
						}
						defenderCelestial = await _tbotOgameBridge.UpdatePlanet(defenderCelestial, UpdateTypes.Facilities);
						if (defenderCelestial.Facilities.MissileSilo >= 2) {
							defenderCelestial = await _tbotOgameBridge.UpdatePlanet(defenderCelestial, UpdateTypes.Defences);
							defenderCelestial = await _tbotOgameBridge.UpdatePlanet(defenderCelestial, UpdateTypes.Productions);
							if (defenderCelestial.Productions.Count == 0) {
								var availableSpace = defenderCelestial.Facilities.MissileSilo - defenderCelestial.Defences.AntiBallisticMissiles - (2 * defenderCelestial.Defences.InterplanetaryMissiles);
								defenderCelestial = await _tbotOgameBridge.UpdatePlanet(defenderCelestial, UpdateTypes.Resources);
								if (availableSpace > 0) {
									DoLog(LogLevel.Information, $"Building {availableSpace} AntiBallisticMissiles on {defenderCelestial.ToString()}");
									await _ogameService.BuildDefences(defenderCelestial, Buildables.AntiBallisticMissiles, availableSpace);
								}
								else {
									DoLog(LogLevel.Information, $"Unable to build AntiBallisticMissiles on {defenderCelestial.ToString()}: there is no space");
								}
							}
							else {
								DoLog(LogLevel.Information, $"Unable to build AntiBallisticMissiles on {defenderCelestial.ToString()}: a production is ongoing");
							}
						}
						else {
							DoLog(LogLevel.Information, $"No MissileSilo level >= 2 on {defenderCelestial.ToString()}");
						}
					}
					return;
				}
				if (attack.Ships != null && _tbotInstance.UserData.researches.EspionageTechnology >= 8) {
					if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Defender, "IgnoreProbes") && (bool) _tbotInstance.InstanceSettings.Defender.IgnoreProbes && attack.IsOnlyProbes()) {
						if (attack.MissionType == Missions.Spy)
							DoLog(LogLevel.Information, "Attacker sent only Probes! Espionage action skipped.");
						else
							DoLog(LogLevel.Information, $"Attack {attack.ID.ToString()} skipped: only Espionage Probes.");

						return;
					}
					if (
						(bool) _tbotInstance.InstanceSettings.Defender.IgnoreWeakAttack &&
						attack.Ships.GetFleetPoints() < (attackedCelestial.Ships.GetFleetPoints() / (int) _tbotInstance.InstanceSettings.Defender.WeakAttackRatio)
					) {
						DoLog(LogLevel.Information, $"Attack {attack.ID.ToString()} skipped: weak attack.");
						return;
					}
				} else {
					DoLog(LogLevel.Information, "Unable to detect fleet composition.");
				}
				var ignoreAttackIfIHaveActive = (bool) _tbotInstance.InstanceSettings.Defender.IgnoreAttackIfIHave.Active;
				var totalResources = attackedCelestial.Resources?.TotalResources ?? 0;
				var fleetPoints = attackedCelestial.Ships?.GetFleetPoints() ?? 0;

				if (
					ignoreAttackIfIHaveActive &&
					totalResources < (long) _tbotInstance.InstanceSettings.Defender.IgnoreAttackIfIHave.MinResourcesToSave &&
					(fleetPoints * 1000) < (long) _tbotInstance.InstanceSettings.Defender.IgnoreAttackIfIHave.MinFleetToSave
				) {
					DoLog(LogLevel.Information, $"Attack {attack.ID.ToString()} skipped: it's not worth it.");
					return;
				}
			} catch {
				DoLog(LogLevel.Warning, "An error has occurred while checking attacker fleet composition");
			}

			if (_tbotInstance.IsNotificationEnabled(_tbotInstance.InstanceSettings.Defender)) {
				await _tbotInstance.SendTelegramMessage($"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} arriving at {attack.ArrivalTime.ToString()}");
				if (attack.Ships != null) { 
					await Task.Delay(1000, _ct);
					await _tbotInstance.SendTelegramMessage($"The attack is composed by: {attack.Ships.ToString()}");
				}
			}
			DoLog(LogLevel.Warning, $"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attackedCelestial.ToString()} arriving at {attack.ArrivalTime.ToString()}");
			if (attack.Ships != null) {
				await Task.Delay(1000, _ct);
				DoLog(LogLevel.Warning, $"The attack is composed by: {attack.Ships.ToString()}");
			}

			if ((bool) _tbotInstance.InstanceSettings.Defender.SpyAttacker.Active) {
				_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
				if (attackedCelestial.Ships.EspionageProbe == 0) {
					DoLog(LogLevel.Warning, "Could not spy attacker: no probes available.");
				} else {
					try {
						Coordinate destination = attack.Origin;
						Ships ships = new() { EspionageProbe = (int) _tbotInstance.InstanceSettings.Defender.SpyAttacker.Probes };
						int fleetId = await _fleetScheduler.SendFleet(attackedCelestial, ships, destination, Missions.Spy, Speeds.HundredPercent, new Resources(), _tbotInstance.UserData.userInfo.Class);
						var fleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleetId);
						if (fleet == null) {
							DoLog(LogLevel.Warning, $"SpyAttacker: SendFleet returned id={fleetId}, but fleet was not found in current fleet list (send may have failed or list not updated yet).");
						} else {
							DoLog(LogLevel.Information, $"Spying attacker from {attackedCelestial.ToString()} to {destination.ToString()} with {_tbotInstance.InstanceSettings.Defender.SpyAttacker.Probes} probes. Arrival at {fleet.ArrivalTime.ToString()}");
						}
					} catch (Exception e) {
						DoLog(LogLevel.Error, $"Could not spy attacker: an exception has occurred: {e.Message}");
						DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
					}
				}
			}

			if ((bool) _tbotInstance.InstanceSettings.Defender.MessageAttacker.Active) {
				try {
					if (attack.AttackerID != 0) {
						Random random = new();
						string[] messages = _tbotInstance.InstanceSettings.Defender.MessageAttacker.Messages;
						string message = messages.ToList().Shuffle().First();
						DoLog(LogLevel.Information, $"Sending message \"{message}\" to attacker {attack.AttackerName}");
						try {
							await _ogameService.SendMessage(attack.AttackerID, message);
							DoLog(LogLevel.Information, "Message succesfully sent.");
						} catch {
							DoLog(LogLevel.Warning, "Unable send message.");
						}
					} else {
						DoLog(LogLevel.Warning, "Unable send message.");
					}

				} catch (Exception e) {
					DoLog(LogLevel.Error, $"Could not message attacker: an exception has occurred: {e.Message}");
					DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				}
			}

			if ((bool) _tbotInstance.InstanceSettings.Defender.Autofleet.Active) {
				try {
					var minFlightTime = attack.ArriveIn + (attack.ArriveIn / 100 * 30) + (RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds) / 1000);
					await _fleetScheduler.AutoFleetSave(attackedCelestial, false, minFlightTime);
				} catch (Exception e) {
					DoLog(LogLevel.Error, $"Could not fleetsave: an exception has occurred: {e.Message}");
					DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
				}
			}
			} catch (Exception e) {
				DoLog(LogLevel.Error, $"HandleAttack error for attack {attack?.ID}: {e.Message}");
				DoLog(LogLevel.Warning, $"Stacktrace: {e.StackTrace}");
			}
		}
	}
}