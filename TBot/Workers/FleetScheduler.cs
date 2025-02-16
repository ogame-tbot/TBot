using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using Tbot.Includes;
using TBot.Common.Logging;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using System.ComponentModel.Design;
using TBot.Model;
using System.Threading;
using System.Globalization;
using TBot.Ogame.Infrastructure;
using Tbot.Common.Settings;
using System.Numerics;

namespace Tbot.Workers {
	public class FleetScheduler : IFleetScheduler {
		private readonly object _fleetLock = new();
		private ITBotMain _tbotInstance = null;
		private ITBotOgamedBridge _tbotOgameBridge;
		private readonly IOgameService _ogameService;
		private readonly ICalculationService _calcService;
		

		private Dictionary<string, Timer> timers = new();
		public FleetScheduler(
			ICalculationService helpService,
			IOgameService ogameService) {
			_calcService = helpService;
			_ogameService = ogameService;
		}

		public void SetTBotInstance(ITBotMain tbotInstance) {
			_tbotInstance = tbotInstance;
		}

		public void SetTBotOgameBridge(ITBotOgamedBridge tbotOgameBridge) {
			_tbotOgameBridge = tbotOgameBridge;
		}

		public async Task SpyCrash(Celestial fromCelestial, Coordinate target = null) {
			decimal speed = Speeds.HundredPercent;
			fromCelestial = await _tbotOgameBridge.UpdatePlanet(fromCelestial, UpdateTypes.Ships);
			fromCelestial = await _tbotOgameBridge.UpdatePlanet(fromCelestial, UpdateTypes.Resources);
			var payload = fromCelestial.Resources;
			Random random = new Random();

			if (fromCelestial.Ships.EspionageProbe == 0 || payload.Deuterium < 1) {
				_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"No probes or no Fuel on {fromCelestial.Coordinate.ToString()}!");
				await _tbotInstance.SendTelegramMessage($"No probes or no Fuel on {fromCelestial.Coordinate.ToString()}!");
				return;
			}
			// spycrash auto part
			if (target == null) {
				List<Coordinate> spycrash = new();
				int playerid = _tbotInstance.UserData.userInfo.PlayerID;
				int sys = 0;
				for (sys = fromCelestial.Coordinate.System - 2; sys <= fromCelestial.Coordinate.System + 2; sys++) {
					sys = GeneralHelper.ClampSystem(sys);
					GalaxyInfo galaxyInfo = await _ogameService.GetGalaxyInfo(fromCelestial.Coordinate.Galaxy, sys);
					foreach (var planet in galaxyInfo.Planets) {
						try {
							if (planet != null && !planet.Administrator && !planet.Inactive && !planet.StrongPlayer && !planet.Newbie && !planet.Banned && !planet.Vacation) {
								if (planet.Player.ID != playerid) { //exclude player planet
									spycrash.Add(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet));
								}
							}
						} catch (NullReferenceException) {
							continue;
						}
					}
				}

				if (spycrash.Count() == 0) {
					await _tbotInstance.SendTelegramMessage($"No planet to spycrash on could be found over system -2 -> +2");
					return;
				} else {
					target = spycrash[random.Next(spycrash.Count())];
				}
			}
			var attackingShips = new Ships().Add(Buildables.EspionageProbe, 1);

			int fleetId = await SendFleet(fromCelestial, attackingShips, target, Missions.Attack, speed);

			if (fleetId != (int) SendFleetCode.GenericError ||
				fleetId != (int) SendFleetCode.AfterSleepTime ||
				fleetId != (int) SendFleetCode.NotEnoughSlots) {
				_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"EspionageProbe sent to crash on {target.ToString()}");

				await _tbotInstance.SendTelegramMessage($"EspionageProbe sent to crash on {target.ToString()}");
			}
			return;
		}

		public async Task AutoFleetSave(Celestial celestial, bool isSleepTimeFleetSave = false, long minDuration = 0, bool WaitFleetsReturn = false, Missions TelegramMission = Missions.None, bool fromTelegram = false, bool saveall = false) {
			DateTime departureTime = await _tbotOgameBridge.GetDateTime();
			_tbotInstance.SleepDuration = minDuration;

			if (WaitFleetsReturn) {

				_tbotInstance.UserData.fleets = await UpdateFleets();
				long interval;
				try {
					interval = (_tbotInstance.UserData.fleets
						.Where(f => f.Mission != Missions.Discovery)
						.OrderBy(f => f.BackIn)
						.Last()
						.BackIn ?? 0
					) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				} catch {
					interval = 0;
				}

				if (interval > 0 && (!timers.TryGetValue("GhostSleepTimer", out Timer value))) {
					//Stop features which are sending fleets
					List<Feature> features = new List<Feature> {
						Feature.Colonize,
						Feature.BrainAutoRepatriate,
						Feature.BrainAutoMine,
						Feature.BrainAutoResearch,
						Feature.BrainAutoMine,
						Feature.BrainLifeformAutoMine,
						Feature.BrainLifeformAutoResearch,
						Feature.AutoFarm,
						Feature.Harvest,
						Feature.Expeditions,
						Feature.AutoDiscovery
					};
					foreach(var feat in features) {
						await _tbotInstance.StopFeature(feat);
					}
					

					interval += RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime TimeToGhost = departureTime.AddMilliseconds(interval);
					_tbotInstance.NextWakeUpTime = TimeToGhost.AddMilliseconds(minDuration * 1000);

					if (saveall)
						timers.Add("GhostSleepTimer", new Timer(GhostandSleepAfterFleetsReturnAll, null, interval, Timeout.Infinite));
					else
						timers.Add("GhostSleepTimer", new Timer(GhostandSleepAfterFleetsReturn, null, interval, Timeout.Infinite));

					_tbotInstance.log(LogLevel.Information, LogSender.SleepMode, $"Fleets active, Next check at {TimeToGhost.ToString()}");
					await _tbotInstance.SendTelegramMessage($"Waiting for fleets return, delaying ghosting at {TimeToGhost.ToString()}");

					return;
				} else if (interval == 0 && (!timers.TryGetValue("GhostSleepTimer", out Timer value2))) {

					_tbotInstance.log(LogLevel.Information, LogSender.SleepMode, $"No fleets active, Ghosting now.");
					_tbotInstance.NextWakeUpTime = departureTime.AddMilliseconds(minDuration * 1000);
					if (saveall)
						GhostandSleepAfterFleetsReturnAll(null);
					else
						GhostandSleepAfterFleetsReturn(null);

					return;
				} else if (timers.TryGetValue("GhostSleepTimer", out Timer value3)) {
					await _tbotInstance.SendTelegramMessage($"GhostSleep already planned, try /cancelghostsleep");
					return;
				}
			}

			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fleet to save!");
				if (fromTelegram)
					await _tbotInstance.SendTelegramMessage($"{celestial.ToString()}: there is no fleet!");
				return;
			}

			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			Celestial destination = new() { ID = 0 };

			if (celestial.Resources.Deuterium == 0) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fuel!");
				if (fromTelegram)
					await _tbotInstance.SendTelegramMessage($"{celestial.ToString()}: there is no fuel!");
				return;
			}

			long maxDeuterium = celestial.Resources.Deuterium;

			if (isSleepTimeFleetSave) {
				if (DateTime.TryParse((string) _tbotInstance.InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp)) {
					if (departureTime >= wakeUp)
						wakeUp = wakeUp.AddDays(1);
					minDuration = (long) wakeUp.Subtract(departureTime).TotalSeconds;
				} else {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Could not plan fleetsave from {celestial.ToString()}: unable to parse comeback time");
					return;
				}
			}

			var payload = celestial.Resources;
			if ((long) _tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DeutToLeave > 0)
				payload.Deuterium -= (long) _tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DeutToLeave;
			if (payload.Deuterium < 0)
				payload.Deuterium = 0;

			FleetHypotesis possibleFleet = new();
			int fleetId = (int) SendFleetCode.GenericError;
			bool AlreadySent = false; //permit to swith to Harvest mission if not enough fuel to Deploy if celestial far away

			//Doing DefaultMission or telegram /ghostto mission
			Missions mission;
			if (!Missions.TryParse(_tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.DefaultMission, out mission)) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Error: Could not parse 'DefaultMission' from settings, value set to Harvest.");
				mission = Missions.Harvest;
			}

			if (TelegramMission != Missions.None)
				mission = TelegramMission;

			List<FleetHypotesis> fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
			if (fleetHypotesis.Count() > 0) {
				foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
					if (CheckFuel(fleet, celestial)) {
						fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, _tbotInstance.UserData.userInfo.Class, true);

						if (fleetId != (int) SendFleetCode.GenericError ||
							fleetId != (int) SendFleetCode.AfterSleepTime ||
							fleetId != (int) SendFleetCode.NotEnoughSlots) {
							possibleFleet = fleet;
							AlreadySent = true;
							break;
						}
					}
				}
			}

			//If /ghostto -> leaving function if failed
			if (fromTelegram && !AlreadySent && mission == Missions.Harvest && fleetHypotesis.Count() == 0) {
				await _tbotInstance.SendTelegramMessage($"No debris field found for {mission}, try to /spycrash.");
				return;
			} else if (fromTelegram && !AlreadySent && fleetHypotesis.Count() >= 0) {
				await _tbotInstance.SendTelegramMessage($"Available fuel: {celestial.Resources.Deuterium}\nNo destination found for {mission}, try to reduce ghost time.");
				return;
			}

			//Doing Deploy
			if (!AlreadySent) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} possible, checking next mission..");
				if (mission == Missions.Harvest) { mission = Missions.Deploy; } else { mission = Missions.Harvest; };
				mission = Missions.Deploy;
				fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, _tbotInstance.UserData.userInfo.Class, true);

							if (fleetId != (int) SendFleetCode.GenericError ||
								fleetId != (int) SendFleetCode.AfterSleepTime ||
								fleetId != (int) SendFleetCode.AfterSleepTime ||
								fleetId != (int) SendFleetCode.NotEnoughSlots) {
								possibleFleet = fleet;
								AlreadySent = true;
								break;
							}
						}
					}
				}
			}
			//Doing colonize
			if (!AlreadySent && celestial.Ships.ColonyShip > 0) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Colonize destination...");
				mission = Missions.Colonize;
				fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, _tbotInstance.UserData.userInfo.Class, true);

							if (fleetId != (int) SendFleetCode.GenericError ||
								fleetId != (int) SendFleetCode.AfterSleepTime ||
								fleetId != (int) SendFleetCode.NotEnoughSlots) {
								possibleFleet = fleet;
								AlreadySent = true;
								break;
							}
						}
					}
				}
			}
			//Doing Spy
			if (!AlreadySent && celestial.Ships.EspionageProbe > 0) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Spy destination...");
				mission = Missions.Spy;
				fleetHypotesis = await GetFleetSaveDestination(_tbotInstance.UserData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, _tbotInstance.UserData.userInfo.Class, true);

							if (fleetId != (int) SendFleetCode.GenericError ||
								fleetId != (int) SendFleetCode.AfterSleepTime ||
								fleetId != (int) SendFleetCode.NotEnoughSlots) {
								possibleFleet = fleet;
								AlreadySent = true;
								break;
							}
						}
					}
				}
			}

			//Doing switch
			bool hasMoon = _tbotInstance.UserData.celestials.Count(c => c.HasCoords(new Coordinate(celestial.Coordinate.Galaxy, celestial.Coordinate.System, celestial.Coordinate.Position, Celestials.Moon))) == 1;
			if (!AlreadySent && hasMoon && !timers.TryGetValue("GhostSleepTimer", out Timer val)) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} possible (missing fuel?), checking for switch if has Moon");
				//var validSpeeds = _tbotInstance.UserData.userInfo.Class == CharacterClass.General ? Speeds.GetGeneralSpeedsList() : Speeds.GetNonGeneralSpeedsList();
				//Random randomSpeed = new Random();
				//decimal speed = validSpeeds[randomSpeed.Next(validSpeeds.Count)];
				decimal speed = 10;
				AlreadySent = await _tbotInstance.TelegramSwitch(speed, celestial);
			}

			if (!AlreadySent) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.Coordinate.ToString()} no suitable destination found, you gonna get hit!");
				await _tbotInstance.SendTelegramMessage($"Fleetsave from {celestial.Coordinate.ToString()} No destination found!, you gonna get hit!");
				return;
			}


			if ((bool) _tbotInstance.InstanceSettings.SleepMode.AutoFleetSave.Recall && AlreadySent) {
				if (fleetId != (int) SendFleetCode.GenericError ||
					fleetId != (int) SendFleetCode.AfterSleepTime ||
					fleetId != (int) SendFleetCode.NotEnoughSlots) {
					Fleet fleet = _tbotInstance.UserData.fleets.Single(fleet => fleet.ID == fleetId);
					DateTime time = await _tbotOgameBridge.GetDateTime();
					var interval = ((minDuration / 2) * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					timers.Add($"RecallTimer-{fleetId.ToString()}", new Timer(RetireFleet, fleet, interval, Timeout.Infinite));
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"The fleet will be recalled at {newTime.ToString()}");
					if (fromTelegram)
						await _tbotInstance.SendTelegramMessage($"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}, recalled at {newTime.ToString()}");
				}
			} else {
				if (fleetId != (int) SendFleetCode.GenericError ||
					fleetId != (int) SendFleetCode.AfterSleepTime ||
					fleetId != (int) SendFleetCode.NotEnoughSlots) {
					Fleet fleet = _tbotInstance.UserData.fleets.Single(fleet => fleet.ID == fleetId);
					DateTime returntime = (DateTime) fleet.BackTime;
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, arrive at {possibleFleet.Duration} fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}");
					if (fromTelegram)
						await _tbotInstance.SendTelegramMessage($"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, arrive at {possibleFleet.Duration.ToString()}, returned at {returntime.ToString()} fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}");
				}
			}
		}

		public async Task<int> SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Resources payload = null, CharacterClass playerClass = CharacterClass.NoClass, bool force = false) {
			_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending fleet from {origin.Coordinate.ToString()} to {destination.ToString()}. Mission: {mission.ToString()}. Speed: {(speed * 10).ToString()}% Ships: {ships.ToString()}");

			if (playerClass == CharacterClass.NoClass)
				playerClass = _tbotInstance.UserData.userInfo.Class;

			if (!ships.HasMovableFleet()) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: there are no ships to send");
				return (int) SendFleetCode.GenericError;
			}
			if (mission == Missions.Expedition && ships.IsOnlyProbes()) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: cannot send an expedition with no ships");
				return (int) SendFleetCode.GenericError;
			}
			if (origin.Coordinate.IsSame(destination)) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: origin and destination are the same");
				return (int) SendFleetCode.GenericError;
			}
			if (destination.Galaxy <= 0 || destination.Galaxy > _tbotInstance.UserData.serverData.Galaxies || destination.Position <= 0 || destination.Position > 17) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
				return (int) SendFleetCode.GenericError;
			}
			if (destination.System <= 0 || destination.System > _tbotInstance.UserData.serverData.Systems) {
				if (_tbotInstance.UserData.serverData.DonutGalaxy) {
					if (destination.System <= 0) {
						destination.System += _tbotInstance.UserData.serverData.Systems;
					} else if (destination.System > _tbotInstance.UserData.serverData.Systems) {
						destination.System -= _tbotInstance.UserData.serverData.Systems;
					}
				} else {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
					return (int) SendFleetCode.GenericError;
				}
			}

			/*
			if (
				playerClass != CharacterClass.General && (
					speed == Speeds.FivePercent ||
					speed == Speeds.FifteenPercent ||
					speed == Speeds.TwentyfivePercent ||
					speed == Speeds.ThirtyfivePercent ||
					speed == Speeds.FourtyfivePercent ||
					speed == Speeds.FiftyfivePercent ||
					speed == Speeds.SixtyfivePercent ||
					speed == Speeds.SeventyfivePercent ||
					speed == Speeds.EightyfivePercent ||
					speed == Speeds.NinetyfivePercent
				)
			) {*/

			if (!_calcService.GetValidSpeedsForClass(playerClass).Any(s => s == speed)) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: speed not available for your class");
				return (int) SendFleetCode.GenericError;
			}
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
			FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, destination, ships, mission, speed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
			_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"Calculated flight time (one-way): {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");

			var flightTime = mission switch {
				Missions.Deploy => fleetPrediction.Time,

				// Use a ternary operator to conditionally handle IgnoreSleep setting
				Missions.Expedition => (bool) _tbotInstance.InstanceSettings.Expeditions.IgnoreSleep
					? 1
					: (long) Math.Round((double) (2 * fleetPrediction.Time) + 3600, 0, MidpointRounding.ToPositiveInfinity),

				// Default case for other missions
				_ => (long) Math.Round((double) (2 * fleetPrediction.Time), 0, MidpointRounding.ToPositiveInfinity)
			};
			_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"Calculated flight time (full trip): {TimeSpan.FromSeconds(flightTime).ToString()}");
			_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"Calculated flight fuel: {fleetPrediction.Fuel.ToString()}");

			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
			if (origin.Resources.Deuterium < fleetPrediction.Fuel) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: not enough deuterium!");
				return (int) SendFleetCode.GenericError;
			}
			if (_calcService.CalcFleetFuelCapacity(ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo) != 0 && _calcService.CalcFleetFuelCapacity(ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo) < fleetPrediction.Fuel) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: ships don't have enough fuel capacity!");
				return (int) SendFleetCode.GenericError;
			}
			if (
				(bool) _tbotInstance.InstanceSettings.SleepMode.Active &&
				DateTime.TryParse((string) _tbotInstance.InstanceSettings.SleepMode.GoToSleep, out DateTime goToSleep) &&
				DateTime.TryParse((string) _tbotInstance.InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp) &&
				!force
			) {
				DateTime time = await _tbotOgameBridge.GetDateTime();
				if (GeneralHelper.ShouldSleep(time, goToSleep, wakeUp)) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: bed time has passed");
					return (int) SendFleetCode.AfterSleepTime;
				}
				if (goToSleep >= wakeUp) {
					wakeUp = wakeUp.AddDays(1);
				}
				if (goToSleep < time) {
					goToSleep = goToSleep.AddDays(1);
				}
				if (wakeUp < time) {
					wakeUp = wakeUp.AddDays(1);
				}
				_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"goToSleep : {goToSleep.ToString()}");
				_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"wakeUp : {wakeUp.ToString()}");
				DateTime returnTime = time.AddSeconds(flightTime);
				_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"returnTime : {returnTime.ToString()}");

				if (returnTime >= goToSleep && returnTime <= wakeUp) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: it would come back during sleep time");
					return (int) SendFleetCode.AfterSleepTime;
				}
			}
			_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
			int slotsToLeaveFree = (int) _tbotInstance.InstanceSettings.General.SlotsToLeaveFree;
			if (_tbotInstance.UserData.slots.Free == 0) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet, no slots available");
				return (int) SendFleetCode.NotEnoughSlots;
			} else if (_tbotInstance.UserData.slots.Free > slotsToLeaveFree || force) {
				if (payload == null)
					payload = new();
				try {
					if (payload.Metal < 0)
						payload.Metal = 0;
					if (payload.Crystal < 0)
						payload.Crystal = 0;
					if (payload.Deuterium < 0)
						payload.Deuterium = 0;
					if (payload.Food < 0)
						payload.Food = 0;
					Fleet fleet = await _ogameService.SendFleet(origin, ships, destination, mission, speed, payload);
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Fleet succesfully sent");
					_tbotInstance.UserData.fleets = await _ogameService.GetFleets();
					_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
					return fleet.ID;
				} catch (Exception e) {
					_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"Unable to send fleet: an exception has occurred: {e.Message}");
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
					return (int) SendFleetCode.GenericError;
				}
			} else {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet, no slots available");
				return (int) SendFleetCode.NotEnoughSlots;
			}
		}

		public async Task CancelFleet(Fleet fleet) {
			//_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Recalling fleet id {fleet.ID} originally from {fleet.Origin.ToString()} to {fleet.Destination.ToString()} with mission: {fleet.Mission.ToString()}. Start time: {fleet.StartTime.ToString()} - Arrival time: {fleet.ArrivalTime.ToString()} - Ships: {fleet.Ships.ToString()}");
			_tbotInstance.UserData.slots = await _tbotOgameBridge.UpdateSlots();
			try {
				await Task.Delay((int) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
				_tbotInstance.log(LogLevel.Debug, LogSender.FleetScheduler, $"Recall Fleet with ID: {fleet.ID}");
				await _ogameService.CancelFleet(fleet);
				await Task.Delay((int) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
				_tbotInstance.UserData.fleets = await UpdateFleets();
				Fleet recalledFleet = _tbotInstance.UserData.fleets.SingleOrDefault(f => f.ID == fleet.ID) ?? new() { ID = (int) SendFleetCode.GenericError };
				if (recalledFleet.ID == (int) SendFleetCode.GenericError) {
					_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, "Unable to recall fleet: an unknon error has occurred, already recalled ?.");
				} else {
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
					if ((bool) _tbotInstance.InstanceSettings.Defender.TelegramMessenger.Active) {
						await _tbotInstance.SendTelegramMessage($"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
					}
					return;
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"Unable to recall fleet: an exception has occurred: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return;
			} finally {
				if (timers.TryGetValue($"RecallTimer-{fleet.ID.ToString()}", out Timer value)) {
					value.Dispose();
					timers.Remove($"RecallTimer-{fleet.ID.ToString()}");
				}

			}
		}
		public async Task<List<Fleet>> UpdateFleets() {
			try {
				return await _ogameService.GetFleets();
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateFleets() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateFleets() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public async void RetireFleet(object fleet) {
			await CancelFleet((Fleet)fleet);
		}


		private async void GhostandSleepAfterFleetsReturnAll(object state) {
			if (timers.TryGetValue("GhostSleepTimer", out Timer value))
				value.Dispose();
			timers.Remove("GhostSleepTimer");


			var celestialsToFleetsave = await _tbotOgameBridge.UpdateCelestials();
			celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
			if (celestialsToFleetsave.Count == 0)
				celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Planet).ToList();

			foreach (Celestial celestial in celestialsToFleetsave)
				await AutoFleetSave(celestial, false, _tbotInstance.SleepDuration, false, _tbotInstance.TelegramUserData.Mission, true);

			await _tbotInstance.SleepNow(_tbotInstance.NextWakeUpTime);
		}

		private async void GhostandSleepAfterFleetsReturn(object state) {
			if (timers.TryGetValue("GhostSleepTimer", out Timer value))
				value.Dispose();
			timers.Remove("GhostSleepTimer");

			await AutoFleetSave(_tbotInstance.TelegramUserData.CurrentCelestialToSave, false, _tbotInstance.SleepDuration, false, _tbotInstance.TelegramUserData.Mission, true);

			await _tbotInstance.SleepNow(_tbotInstance.NextWakeUpTime);
		}

		private bool CheckFuel(FleetHypotesis fleetHypotesis, Celestial celestial) {
			if (celestial.Resources.Deuterium < fleetHypotesis.Fuel) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: not enough fuel!");
				return false;
			}
			if (_calcService.CalcFleetFuelCapacity(fleetHypotesis.Ships, _tbotInstance.UserData.serverData, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo) < fleetHypotesis.Fuel) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: ships don't have enough fuel capacity!");
				return false;
			}
			return true;
		}

		private async Task<List<FleetHypotesis>> GetFleetSaveDestination(List<Celestial> source, Celestial origin, DateTime departureDate, long minFlightTime, Missions mission, long maxFuel) {
			var validSpeeds = _tbotInstance.UserData.userInfo.Class == CharacterClass.General ? Speeds.GetGeneralSpeedsList() : Speeds.GetNonGeneralSpeedsList();
			List<FleetHypotesis> possibleFleets = new();
			List<Coordinate> possibleDestinations = new();
			GalaxyInfo galaxyInfo = new();
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
			int sys = 0;

			switch (mission) {
				case Missions.Spy:
					if (origin.Ships.EspionageProbe == 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"No espionageprobe available, skipping to next mission...");
						break;
					}
					for (sys = origin.Coordinate.System - 5; sys <= origin.Coordinate.System + 5; sys++) {
						sys = GeneralHelper.ClampSystem(sys);
						Coordinate destination = new(origin.Coordinate.Galaxy, sys, 16, Celestials.Planet);
						foreach (var currentSpeed in validSpeeds) {
							FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, destination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

							FleetHypotesis fleetHypotesis = new() {
								Origin = origin,
								Destination = destination,
								Ships = origin.Ships.GetMovableShips(),
								Mission = mission,
								Speed = currentSpeed,
								Duration = fleetPrediction.Time,
								Fuel = fleetPrediction.Fuel
							};
							if (fleetHypotesis.Duration >= minFlightTime / 2 && fleetHypotesis.Fuel <= maxFuel) {
								possibleFleets.Add(fleetHypotesis);
								break;
							}
						}
					}
					break;

				case Missions.Colonize:
					if (origin.Ships.ColonyShip == 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"No colony ship available, skipping to next mission...");
						break;
					}
					galaxyInfo = await _ogameService.GetGalaxyInfo(origin.Coordinate);

					for (sys = origin.Coordinate.System - 5; sys <= origin.Coordinate.System + 5; sys++) {
						int pos = 1;
						sys = GeneralHelper.ClampSystem(sys);
						galaxyInfo = await _ogameService.GetGalaxyInfo(origin.Coordinate.Galaxy, sys);
						foreach (var planet in galaxyInfo.Planets) {
							if (planet == null) {
								possibleDestinations.Add(new(origin.Coordinate.Galaxy, sys, pos));
							}
							pos++;
						}
					}

					if (possibleDestinations.Count() > 0) {
						foreach (var possibleDestination in possibleDestinations) {
							foreach (var currentSpeed in validSpeeds) {
								FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

								FleetHypotesis fleetHypotesis = new() {
									Origin = origin,
									Destination = possibleDestination,
									Ships = origin.Ships.GetMovableShips(),
									Mission = mission,
									Speed = currentSpeed,
									Duration = fleetPrediction.Time,
									Fuel = fleetPrediction.Fuel
								};
								if (fleetHypotesis.Duration >= minFlightTime / 2 && fleetHypotesis.Fuel <= maxFuel) {
									possibleFleets.Add(fleetHypotesis);
									break;
								}
							}
						}
					}
					break;

				case Missions.Harvest:
					if (origin.Ships.Recycler == 0) {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"No recycler available, skipping to next mission...");
						break;
					}
					int playerid = _tbotInstance.UserData.userInfo.PlayerID;
					for (sys = origin.Coordinate.System - 5; sys <= origin.Coordinate.System + 5; sys++) {
						sys = GeneralHelper.ClampSystem(sys);
						galaxyInfo = await _ogameService.GetGalaxyInfo(origin.Coordinate.Galaxy, sys);
						foreach (var planet in galaxyInfo.Planets) {
							if (planet != null && planet.Debris != null && planet.Debris.Resources.TotalResources > 0) {
								possibleDestinations.Add(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris));
							}
						}
					}


					if (possibleDestinations.Count() > 0) {
						foreach (var possibleDestination in possibleDestinations) {
							foreach (var currentSpeed in validSpeeds) {
								FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

								FleetHypotesis fleetHypotesis = new() {
									Origin = origin,
									Destination = possibleDestination,
									Ships = origin.Ships.GetMovableShips(),
									Mission = mission,
									Speed = currentSpeed,
									Duration = fleetPrediction.Time,
									Fuel = fleetPrediction.Fuel
								};
								if (fleetHypotesis.Duration >= minFlightTime / 2 && fleetHypotesis.Fuel <= maxFuel) {
									possibleFleets.Add(fleetHypotesis);
									break;
								}
							}
						}
					}
					break;

				case Missions.Deploy:
					possibleDestinations = _tbotInstance.UserData.celestials
						.Where(planet => planet.ID != origin.ID)
						.Where(planet => (planet.Coordinate.Type == Celestials.Moon))
						.Select(planet => planet.Coordinate)
						.ToList();

					if (possibleDestinations.Count == 0) {
						possibleDestinations = _tbotInstance.UserData.celestials
							.Where(planet => planet.ID != origin.ID)
							.Select(planet => planet.Coordinate)
							.ToList();
					}

					foreach (var possibleDestination in possibleDestinations) {
						foreach (var currentSpeed in validSpeeds) {
							FleetPrediction fleetPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);

							FleetHypotesis fleetHypotesis = new() {
								Origin = origin,
								Destination = possibleDestination,
								Ships = origin.Ships.GetMovableShips(),
								Mission = mission,
								Speed = currentSpeed,
								Duration = fleetPrediction.Time,
								Fuel = fleetPrediction.Fuel
							};
							if (fleetHypotesis.Duration >= minFlightTime && fleetHypotesis.Fuel <= maxFuel) {
								possibleFleets.Add(fleetHypotesis);
								break;
							}
						}
					}
					break;

				default:
					break;
			}

			if (possibleFleets.Count() > 0) {
				return possibleFleets;

			} else {
				return new List<FleetHypotesis>();
			}
		}

		public async Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Resources resources, Buildables buildable = Buildables.Null, Buildings maxBuildings = null, Facilities maxFacilities = null, Facilities maxLunarFacilities = null, AutoMinerSettings autoMinerSettings = null) {
			try {
				if (origin.ID == destination.ID) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = resources.Difference(destination.Resources);
					Resources resToLeave = new(0, 0, 0);
					if ((long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeave > 0)
						resToLeave.Deuterium = (long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeave;

					origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.Transports.CargoType, true, out preferredShip)) {
							_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}
						
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
						float cargoBonus = origin.LFBonuses.GetShipCargoBonus(preferredShip);
						long idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						Ships ships = new();
						Ships tempShips = new();
						tempShips.Add(preferredShip, 1);
						var flightPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, tempShips, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
						long flightTime = flightPrediction.Time;
						var availableShips = origin.Ships.GetAmount(preferredShip);
						if (buildable != Buildables.Null) {
							int level = _calcService.GetNextLevel(destination, buildable);
							long buildTime = _calcService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, destination.Facilities);
							if (maxBuildings != null && maxFacilities != null && maxLunarFacilities != null && autoMinerSettings != null) {
								var tempCelestial = destination;
								while (flightTime * 2 >= buildTime && idealShips <= availableShips) {
									tempCelestial.SetLevel(buildable, level);
									if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler && buildable != Buildables.SpaceDock) {
										tempCelestial.Fields.Built += 1;
									}
									var nextBuildable = Buildables.Null;
									if (tempCelestial.Coordinate.Type == Celestials.Planet) {
										tempCelestial.Resources.Energy += _calcService.GetProductionEnergyDelta(buildable, level, _tbotInstance.UserData.researches.EnergyTechnology, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
										tempCelestial.ResourcesProduction.Energy.Available += _calcService.GetProductionEnergyDelta(buildable, level, _tbotInstance.UserData.researches.EnergyTechnology, 1, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff.Engineer, _tbotInstance.UserData.staff.IsFull);
										tempCelestial.Resources.Energy -= _calcService.GetRequiredEnergyDelta(buildable, level);
										tempCelestial.ResourcesProduction.Energy.Available -= _calcService.GetRequiredEnergyDelta(buildable, level);
										nextBuildable = _calcService.GetNextBuildingToBuild(tempCelestial as Planet, _tbotInstance.UserData.researches, maxBuildings, maxFacilities, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.staff, _tbotInstance.UserData.serverData, autoMinerSettings, 1);
									} else {
										nextBuildable = _calcService.GetNextLunarFacilityToBuild(tempCelestial as Moon, _tbotInstance.UserData.researches, maxLunarFacilities);
									}
									if ((nextBuildable != Buildables.Null) && (buildable != Buildables.SolarSatellite)) {
										var nextLevel = _calcService.GetNextLevel(tempCelestial, nextBuildable);
										var newMissingRes = missingResources.Sum(_calcService.CalcPrice(nextBuildable, nextLevel));

										if (origin.Resources.IsEnoughFor(newMissingRes, resToLeave)) {
											var newIdealShips = _calcService.CalcShipNumberForPayload(newMissingRes, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
											if (newIdealShips <= origin.Ships.GetAmount(preferredShip)) {
												idealShips = newIdealShips;
												missingResources = newMissingRes;
												buildTime += _calcService.CalcProductionTime(nextBuildable, nextLevel, _tbotInstance.UserData.serverData, tempCelestial.Facilities);
												_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending resources for {nextBuildable.ToString()} level {nextLevel} too");
												level = nextLevel;
												buildable = nextBuildable;
											} else {
												break;
											}
										} else {
											break;
										}
									} else {
										break;
									}
								}
							}
						}

						if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.Transports, "RoundResources") && (bool) _tbotInstance.InstanceSettings.Brain.Transports.RoundResources) {
							missingResources = missingResources.Round();
							idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						}

						bool doMultipleTransports = (idealShips > origin.Ships.GetAmount(preferredShip) && (bool) _tbotInstance.InstanceSettings.Brain.Transports.DoMultipleTransportIsNotEnoughShipButSamePosition) &&
							(origin.Coordinate.Galaxy == destination.Coordinate.Galaxy &&
							origin.Coordinate.System == destination.Coordinate.System &&
							origin.Coordinate.Position == destination.Coordinate.Position) ?
								true:
								false;

						if (idealShips <= origin.Ships.GetAmount(preferredShip) || (doMultipleTransports && !origin.Ships.IsEmpty())) {
							if (doMultipleTransports) {
								ships.Add(preferredShip, origin.Ships.GetAmount(preferredShip));
							} else {
								ships.Add(preferredShip, idealShips);
							}

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourceSettings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Buildings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourcesProduction);

								float metProdInASecond = destination.ResourcesProduction.Metal.CurrentProduction / (float) 3600;
								float cryProdInASecond = destination.ResourcesProduction.Crystal.CurrentProduction / (float) 3600;
								float deutProdInASecond = destination.ResourcesProduction.Deuterium.CurrentProduction / (float) 3600;
								var metProdInFlightTime = metProdInASecond * flightTime;
								var cryProdInFlightTime = cryProdInASecond * flightTime;
								var deutProdInFlightTime = deutProdInASecond * flightTime;

								if (
									(metProdInASecond == 0 && missingResources.Metal > 0) ||
									(cryProdInFlightTime == 0 && missingResources.Crystal > 0) ||
									(deutProdInFlightTime == 0 && missingResources.Deuterium > 0) ||
									missingResources.Metal >= metProdInFlightTime ||
									missingResources.Crystal >= cryProdInFlightTime ||
									missingResources.Deuterium >= deutProdInFlightTime ||
									resources.Metal > destination.ResourcesProduction.Metal.StorageCapacity ||
									resources.Crystal > destination.ResourcesProduction.Crystal.StorageCapacity ||
									resources.Deuterium > destination.ResourcesProduction.Deuterium.StorageCapacity
								) {
									_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
									return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Skipping transport: it is quicker to wait for production.");
									return 0;
								}
							} else {
								_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
								return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
							}
						} else {
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping transport: not enough resources in {origin.ToString()}. Needed: {missingResources.TransportableResources} - Available: {origin.Resources.TransportableResources}");
						return (int) SendFleetCode.NotEnoughRessources;
					}
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"HandleMinerTransport Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}

		public async Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Resources resources, LFBuildables buildable = LFBuildables.None, LFBuildings maxLFBuildings = null, bool preventIfMoreExpensiveThanNextMine = false) {
			try {
				if (origin.ID == destination.ID) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = resources.Difference(destination.Resources);
					Resources resToLeave = new(0, 0, 0);
					if ((long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeave > 0)
						resToLeave.Deuterium = (long) _tbotInstance.InstanceSettings.Brain.Transports.DeutToLeave;
					
					origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);
						destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Facilities);
						destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Constructions);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.Transports.CargoType, true, out preferredShip)) {
							_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}

						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBuildings);
						origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.LFBonuses);
						float cargoBonus = origin.LFBonuses.GetShipCargoBonus(preferredShip);
						long idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo) + 1;
						Ships ships = new();
						Ships tempShips = new();
						if (maxLFBuildings == null)
							maxLFBuildings = new();
						tempShips.Add(preferredShip, 1);
						var flightPrediction = _calcService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, tempShips, Missions.Transport, Speeds.HundredPercent, _tbotInstance.UserData.researches, _tbotInstance.UserData.serverData, origin.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.allianceClass);
						long flightTime = flightPrediction.Time;
						idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						var availableShips = origin.Ships.GetAmount(preferredShip);
						if (buildable != LFBuildables.None) {
							int level = _calcService.GetNextLevel(destination, buildable);
							long buildTime = _calcService.CalcProductionTime(buildable, level, _tbotInstance.UserData.serverData, destination);
							var tempCelestial = destination;
							while (flightTime * 2 >= buildTime && idealShips <= availableShips) {
								tempCelestial.SetLevel(buildable, level);
									
								tempCelestial.ResourcesProduction.Population.LivingSpace = _calcService.CalcLivingSpace(tempCelestial as Planet);
								tempCelestial.ResourcesProduction.Population.Satisfied = _calcService.CalcSatisfied(tempCelestial as Planet);

								bool preventTechBuilding = false;
								if (tempCelestial.Constructions.LFResearchCountdown > 0) {
									preventTechBuilding = true;
								}

								var nextBuildable = LFBuildables.None;									
								nextBuildable = _calcService.GetNextLFBuildingToBuild(tempCelestial as Planet, maxLFBuildings, preventIfMoreExpensiveThanNextMine, preventTechBuilding);
								if (nextBuildable != LFBuildables.None) {
									var nextLevel = _calcService.GetNextLevel(tempCelestial, nextBuildable);
									float costReduction = _calcService.CalcLFBuildingsResourcesCostBonus(tempCelestial);
									float popReduction = _calcService.CalcLFBuildingsPopulationCostBonus(tempCelestial);
									var newMissingRes = missingResources.Sum(_calcService.CalcPrice(nextBuildable, nextLevel, costReduction, 0, popReduction));

									if (origin.Resources.IsEnoughFor(newMissingRes, resToLeave)) {
										var newIdealShips = _calcService.CalcShipNumberForPayload(newMissingRes, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
										if (newIdealShips <= origin.Ships.GetAmount(preferredShip)) {
											idealShips = newIdealShips;
											missingResources = newMissingRes;
											buildTime += _calcService.CalcProductionTime(nextBuildable, nextLevel, _tbotInstance.UserData.serverData, tempCelestial);
											_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending resources for {nextBuildable.ToString()} level {nextLevel} too");
											level = nextLevel;
											buildable = nextBuildable;
										} else {
											break;
										}
									} else {
										break;
									}
								} else {
									break;
								}
							}
						}

						if (SettingsService.IsSettingSet(_tbotInstance.InstanceSettings.Brain.Transports, "RoundResources") && (bool) _tbotInstance.InstanceSettings.Brain.Transports.RoundResources) {
							missingResources = missingResources.Round();
							idealShips = _calcService.CalcShipNumberForPayload(missingResources, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
						}

						bool doMultipleTransports = (idealShips > origin.Ships.GetAmount(preferredShip) && (bool) _tbotInstance.InstanceSettings.Brain.Transports.DoMultipleTransportIsNotEnoughShipButSamePosition) &&
							(origin.Coordinate.Galaxy == destination.Coordinate.Galaxy &&
							origin.Coordinate.System == destination.Coordinate.System &&
							origin.Coordinate.Position == destination.Coordinate.Position) ?
								true:
								false;
						
						if (idealShips <= origin.Ships.GetAmount(preferredShip) || (doMultipleTransports && !origin.Ships.IsEmpty())) {
							if (doMultipleTransports) {
								ships.Add(preferredShip, origin.Ships.GetAmount(preferredShip));
							} else {
								ships.Add(preferredShip, idealShips);
							}

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourceSettings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.Buildings);
								destination = await _tbotOgameBridge.UpdatePlanet(destination, UpdateTypes.ResourcesProduction);

								float metProdInASecond = destination.ResourcesProduction.Metal.CurrentProduction / (float) 3600;
								float cryProdInASecond = destination.ResourcesProduction.Crystal.CurrentProduction / (float) 3600;
								float deutProdInASecond = destination.ResourcesProduction.Deuterium.CurrentProduction / (float) 3600;
								var metProdInFlightTime = metProdInASecond * flightTime;
								var cryProdInFlightTime = cryProdInASecond * flightTime;
								var deutProdInFlightTime = deutProdInASecond * flightTime;

								if (
									(metProdInASecond == 0 && missingResources.Metal > 0) ||
									(cryProdInFlightTime == 0 && missingResources.Crystal > 0) ||
									(deutProdInFlightTime == 0 && missingResources.Deuterium > 0) ||
									missingResources.Metal >= metProdInFlightTime ||
									missingResources.Crystal >= cryProdInFlightTime ||
									missingResources.Deuterium >= deutProdInFlightTime ||
									resources.Metal > destination.ResourcesProduction.Metal.StorageCapacity ||
									resources.Crystal > destination.ResourcesProduction.Crystal.StorageCapacity ||
									resources.Deuterium > destination.ResourcesProduction.Deuterium.StorageCapacity
								) {
									_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
									return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
								} else {
									_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Skipping transport: it is quicker to wait for production.");
									return (int) SendFleetCode.QuickerToWaitForProduction;
								}
							} else {
								_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
								return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, _tbotInstance.UserData.userInfo.Class);
							}
						} else {
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping transport: not enough resources in {origin.ToString()}. Needed: {missingResources.TransportableResources} - Available: {origin.Resources.TransportableResources}");
						return (int) SendFleetCode.NotEnoughRessources;
					}
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.FleetScheduler, $"HandleMinerTransport Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}

		public async Task Collect() {
			await CollectImpl(true);
		}

		public async Task CollectDeut(long MinAmount = 0) {
			_tbotInstance.UserData.fleets = await UpdateFleets();
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

				var tempCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);
				_tbotInstance.UserData.fleets = await UpdateFleets();

				tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
				tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);

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

				tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.LFBonuses);
				float cargoBonus = tempCelestial.LFBonuses.GetShipCargoBonus(preferredShip);
				long idealShips = _calcService.CalcShipNumberForPayload(payload, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

				Ships ships = new();
				if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
					if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
						ships.Add(preferredShip, idealShips);
					} else {
						ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
					}
					payload = _calcService.CalcMaxTransportableResources(ships, payload, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

					if ((long) payload.TotalResources >= (long) MinAmount) {
						var fleetId = await SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
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

		public async Task<RepatriateCode> CollectImpl(bool fromTelegram) {
			try {
				_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, "Repatriating resources...");

				if (fromTelegram) {
					_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Telegram collect initated..");
				}
				if (_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target) {
					_tbotInstance.UserData.fleets = await UpdateFleets();
					long TotalMet = 0;
					long TotalCri = 0;
					long TotalDeut = 0;
					bool samePosition = (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.TargetAssociateMoon;
					Coordinate destinationCoordinate = new();
					if (!samePosition) {
						destinationCoordinate = new(
						(int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Galaxy,
							(int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.System,
							(int) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Position,
							Enum.Parse<Celestials>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.Target.Type)
						);
					}
					List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
					List<Celestial> celestialsToExclude = _calcService.ParseCelestialsList(_tbotInstance.InstanceSettings.Brain.AutoRepatriate.Exclude, _tbotInstance.UserData.celestials);
					List<Celestial> celestialList = _tbotInstance.UserData.celestials.ToList();
					if (!samePosition)
						celestialList = (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.RandomOrder ? celestialList.Shuffle().ToList() : celestialList.OrderBy(c => _calcService.CalcDistance(c.Coordinate, destinationCoordinate, _tbotInstance.UserData.serverData)).ToList();

					foreach (Celestial celestial in celestialList) {
						if (samePosition) {
							if (celestial.Coordinate.Type == Celestials.Planet && !_calcService.IsThereMoonHere(_tbotInstance.UserData.celestials, celestial)) {
								_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping {celestial.ToString()}: There is no moon.");
								continue;
							}
							destinationCoordinate = new(
								(int) celestial.Coordinate.Galaxy,
								(int) celestial.Coordinate.System,
								(int) celestial.Coordinate.Position,
								celestial.Coordinate.Type == Celestials.Planet ? Celestials.Moon : Celestials.Planet
							);
						}
						if (celestialsToExclude.Has(celestial)) {
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}
						if (celestial.Coordinate.IsSame(destinationCoordinate)) {
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping {celestial.ToString()}: celestial is the target.");
							continue;
						}

						var tempCelestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Fast);

						_tbotInstance.UserData.fleets = await UpdateFleets();
						if ((bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.SkipIfIncomingTransport && _calcService.IsThereTransportTowardsCelestial(celestial, _tbotInstance.UserData.fleets) && (!fromTelegram)) {
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
							continue;
						}
						if (celestial.Coordinate.Type == Celestials.Moon && (bool) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.ExcludeMoons) {
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
							continue;
						}

						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Resources);
						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.Ships);

						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) _tbotInstance.InstanceSettings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
							_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to parse CargoType. Falling back to default SmallCargo");
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
							_tbotInstance.log(LogLevel.Information, LogSender.FleetScheduler, $"Skipping {tempCelestial.ToString()}: resources under set limit");
							continue;
						}

						tempCelestial = await _tbotOgameBridge.UpdatePlanet(tempCelestial, UpdateTypes.LFBonuses);
						float cargoBonus = tempCelestial.LFBonuses.GetShipCargoBonus(preferredShip);
						long idealShips = _calcService.CalcShipNumberForPayload(payload, preferredShip, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, cargoBonus, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);

						Ships ships = new();
						if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
							if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
								ships.Add(preferredShip, idealShips);
							} else {
								ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
							}
							payload = _calcService.CalcMaxTransportableResources(ships, payload, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, tempCelestial.LFBonuses, _tbotInstance.UserData.userInfo.Class, _tbotInstance.UserData.serverData.ProbeCargo);
							
							if (payload.TotalResources > 0) {
								var fleetId = await SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
								if (fleetId == (int) SendFleetCode.AfterSleepTime) {
									return RepatriateCode.Stop;
								}
								if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
									return RepatriateCode.Delay;
								}
								TotalMet += payload.Metal;
								TotalCri += payload.Crystal;
								TotalDeut += payload.Deuterium;
							}
						} else {
							_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping {tempCelestial.ToString()}: there are no {preferredShip.ToString()}");
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
					return RepatriateCode.Success;
				} else {
					_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, "Skipping autorepatriate: unable to parse custom destination");
					return RepatriateCode.Failure;
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Unable to complete repatriate: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return RepatriateCode.Failure;
			}
		}
	}
}
