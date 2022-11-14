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

namespace Tbot.Workers {
	public class HarvestWorker : WorkerBase {

		public HarvestWorker(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) :
			base(parentInstance, fleetScheduler, helpersService) {
		}
		public HarvestWorker(ITBotMain parentInstance) :
			base(parentInstance) {
		}

		public override string GetWorkerName() {
			return "Harvest";
		}
		public override Feature GetFeature() {
			return Feature.Harvest;
		}

		public override LogSender GetLogSender() {
			return LogSender.Harvest;
		}

		protected override async Task Execute(CancellationToken ct) {
			bool stop = false;
			bool delay = false;
			try {

				if (_tbotInstance.UserData.isSleeping) {
					_tbotInstance.log(LogLevel.Information, LogSender.Harvest, "Skipping: Sleep Mode Active!");
					return;
				}

				if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.Active) {
					_tbotInstance.log(LogLevel.Information, LogSender.Harvest, "Detecting harvest targets");

					List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
					var dic = new Dictionary<Coordinate, Celestial>();

					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();

					foreach (Planet planet in _tbotInstance.UserData.celestials.Where(c => c is Planet)) {
						Planet tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, planet, UpdateTypes.Fast) as Planet;
						tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Ships) as Planet;
						Moon moon = new() {
							Ships = new()
						};

						bool hasMoon = _tbotInstance.UserData.celestials.Count(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) == 1;
						if (hasMoon) {
							moon = _tbotInstance.UserData.celestials.Unique().Single(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) as Moon;
							moon = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, moon, UpdateTypes.Ships) as Moon;
						}

						if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.HarvestOwnDF) {
							Coordinate dest = new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris);
							if (dic.Keys.Any(d => d.IsSame(dest)))
								continue;
							if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
								continue;
							tempCelestial = await TBotOgamedBridge.UpdatePlanet(_tbotInstance, tempCelestial, UpdateTypes.Debris) as Planet;
							if (tempCelestial.Debris != null && tempCelestial.Debris.Resources.TotalResources >= (long) _tbotInstance.InstanceSettings.AutoHarvest.MinimumResourcesOwnDF) {
								if (moon.Ships.Recycler >= tempCelestial.Debris.RecyclersNeeded)
									dic.Add(dest, moon);
								else if (moon.Ships.Recycler > 0)
									dic.Add(dest, moon);
								else if (tempCelestial.Ships.Recycler >= tempCelestial.Debris.RecyclersNeeded)
									dic.Add(dest, tempCelestial);
								else if (tempCelestial.Ships.Recycler > 0)
									dic.Add(dest, tempCelestial);
								else
									_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Skipping harvest in {dest.ToString()}: not enough recyclers.");
							}
						}

						if ((bool) _tbotInstance.InstanceSettings.AutoHarvest.HarvestDeepSpace) {
							List<Coordinate> destinations = new List<Coordinate>();
							if ((bool) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Active) {
								int range = (int) _tbotInstance.InstanceSettings.Expeditions.SplitExpeditionsBetweenSystems.Range;

								for (int i = -range; i <= range + 1; i++) {
									Coordinate destination = new Coordinate {
										Galaxy = tempCelestial.Coordinate.Galaxy,
										System = tempCelestial.Coordinate.System + i,
										Position = 16,
										Type = Celestials.DeepSpace
									};
									destination.System = GeneralHelper.WrapSystem(destination.System);

									destinations.Add(destination);
								}
							} else {
								destinations.Add(new(tempCelestial.Coordinate.Galaxy, tempCelestial.Coordinate.System, 16, Celestials.DeepSpace));
							}

							foreach (Coordinate dest in destinations) {
								if (dic.Keys.Any(d => d.IsSame(dest)))
									continue;
								if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
									continue;
								ExpeditionDebris expoDebris = (await _tbotInstance.OgamedInstance.GetGalaxyInfo(dest)).ExpeditionDebris;
								if (expoDebris != null && expoDebris.Resources.TotalResources >= (long) _tbotInstance.InstanceSettings.AutoHarvest.MinimumResourcesDeepSpace) {
									if (moon.Ships.Pathfinder >= expoDebris.PathfindersNeeded)
										dic.Add(dest, moon);
									else if (moon.Ships.Pathfinder > 0)
										dic.Add(dest, moon);
									else if (tempCelestial.Ships.Pathfinder >= expoDebris.PathfindersNeeded)
										dic.Add(dest, tempCelestial);
									else if (tempCelestial.Ships.Pathfinder > 0)
										dic.Add(dest, tempCelestial);
									else
										_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Skipping harvest in {dest.ToString()}: not enough pathfinders.");
								}
							}
						}

						newCelestials.Remove(planet);
						newCelestials.Add(tempCelestial);
					}
					_tbotInstance.UserData.celestials = newCelestials;

					if (dic.Count() == 0)
						_tbotInstance.log(LogLevel.Information, LogSender.Harvest, "Skipping harvest: there are no fields to harvest.");

					foreach (Coordinate destination in dic.Keys) {
						var fleetId = (int) SendFleetCode.GenericError;
						Celestial origin = dic[destination];
						if (destination.Position == 16) {
							ExpeditionDebris debris = (await _tbotInstance.OgamedInstance.GetGalaxyInfo(destination)).ExpeditionDebris;
							long pathfindersToSend = Math.Min(_helpersService.CalcShipNumberForPayload(debris.Resources, Buildables.Pathfinder, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class), origin.Ships.Pathfinder);
							_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {pathfindersToSend.ToString()} {Buildables.Pathfinder.ToString()}");
							fleetId = await _fleetScheduler.SendFleet(origin, new Ships { Pathfinder = pathfindersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
						} else {
							if (_tbotInstance.UserData.celestials.Any(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet)))) {
								Debris debris = (_tbotInstance.UserData.celestials.Where(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet))).First() as Planet).Debris;
								long recyclersToSend = Math.Min(_helpersService.CalcShipNumberForPayload(debris.Resources, Buildables.Recycler, _tbotInstance.UserData.researches.HyperspaceTechnology, _tbotInstance.UserData.serverData, _tbotInstance.UserData.userInfo.Class), origin.Ships.Recycler);
								_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {recyclersToSend.ToString()} {Buildables.Recycler.ToString()}");
								fleetId = await _fleetScheduler.SendFleet(origin, new Ships { Recycler = recyclersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
							}
						}

						if (fleetId == (int) SendFleetCode.AfterSleepTime) {
							stop = true;
							return;
						}
						if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
							delay = true;
							return;
						}
					}

					_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
					long interval;
					if (_tbotInstance.UserData.fleets.Any(f => f.Mission == Missions.Harvest)) {
						interval = (_tbotInstance.UserData.fleets
							.Where(f => f.Mission == Missions.Harvest)
						.OrderBy(f => f.BackIn)
						.First()
							.BackIn ?? 0) * 1000;
					} else {
						interval = (int) RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMax);
					}
					var time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
					_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Next check at {newTime.ToString()}");
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Warning, LogSender.Harvest, $"HandleHarvest exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Harvest, $"Stacktrace: {e.StackTrace}");
				long interval = (int) RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.AutoHarvest.CheckIntervalMax);
				var time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
				_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Next check at {newTime.ToString()}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Stopping feature.");
					}
					if (delay) {
						_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Delaying...");
						var time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
						_tbotInstance.UserData.fleets = await _fleetScheduler.UpdateFleets();
						long interval = (_tbotInstance.UserData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
						_tbotInstance.log(LogLevel.Information, LogSender.Harvest, $"Next check at {newTime.ToString()}");
					}
					await TBotOgamedBridge.CheckCelestials(_tbotInstance);
				}
			}
		}
	}
}
