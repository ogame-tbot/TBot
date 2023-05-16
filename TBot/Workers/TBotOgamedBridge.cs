using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure;
using Tbot.Services;
using Tbot.Includes;
using Tbot.Helpers;
using TBot.Model;

namespace Tbot.Workers
{
	public class TBotOgamedBridge : ITBotOgamedBridge {
		private readonly ITBotMain _tbotInstance;
		private readonly IOgameService _ogameService;

		public TBotOgamedBridge(
			ITBotMain tbotInstance,
			IOgameService ogameService) {
			_ogameService = ogameService;
			_tbotInstance = tbotInstance;
		}
		public async Task<DateTime> GetDateTime() {
			try {
				DateTime dateTime = await _ogameService.GetServerTime();
				if (dateTime.Kind == DateTimeKind.Utc)
					return dateTime.ToLocalTime();
				else
					return dateTime;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"GetDateTime() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				var fallback = DateTime.Now;
				if (fallback.Kind == DateTimeKind.Utc)
					return fallback.ToLocalTime();
				else
					return fallback;
			}
		}
		public async Task<Celestial> UpdatePlanet(Celestial planet, UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogLevel.Information, LogSender.Tbot, $"Updating {planet.ToString()}. Mode: {UpdateTypes.ToString()}");
			IOgameService ogameService = _ogameService;
			try {
				switch (UpdateTypes) {
					case UpdateTypes.Fast:
						planet = await ogameService.GetCelestial(planet);
						break;
					case UpdateTypes.Resources:
						planet.Resources = await ogameService.GetResources(planet);
						break;
					case UpdateTypes.Buildings:
						planet.Buildings = await ogameService.GetBuildings(planet);
						break;
					case UpdateTypes.LFBuildings:
						planet.LFBuildings = await ogameService.GetLFBuildings(planet);
						planet.LFtype = planet.SetLFType();
						break;
					case UpdateTypes.LFTechs:
						planet.LFTechs = await ogameService.GetLFTechs(planet);
						break;
					case UpdateTypes.Ships:
						planet.Ships = await ogameService.GetShips(planet);
						break;
					case UpdateTypes.Facilities:
						planet.Facilities = await ogameService.GetFacilities(planet);
						break;
					case UpdateTypes.Defences:
						planet.Defences = await ogameService.GetDefences(planet);
						break;
					case UpdateTypes.Productions:
						planet.Productions = await ogameService.GetProductions(planet);
						break;
					case UpdateTypes.Constructions:
						planet.Constructions = await ogameService.GetConstructions(planet);
						break;
					case UpdateTypes.ResourceSettings:
						if (planet is Planet) {
							planet.ResourceSettings = await ogameService.GetResourceSettings(planet as Planet);
						}
						break;
					case UpdateTypes.ResourcesProduction:
						if (planet is Planet) {
							planet.ResourcesProduction = await ogameService.GetResourcesProduction(planet as Planet);
						}
						break;
					case UpdateTypes.Techs:
						var techs = await ogameService.GetTechs(planet);
						planet.Defences = techs.defenses;
						planet.Facilities = techs.facilities;
						planet.Ships = techs.ships;
						planet.Buildings = techs.supplies;
						break;
					case UpdateTypes.Debris:
						if (planet is Moon)
							break;
						var galaxyInfo = await ogameService.GetGalaxyInfo(planet.Coordinate);
						var thisPlanetGalaxyInfo = galaxyInfo.Planets.Single(p => p != null && p.Coordinate.IsSame(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet)));
						planet.Debris = thisPlanetGalaxyInfo.Debris;
						break;
					case UpdateTypes.Full:
					default:
						planet.Resources = await ogameService.GetResources(planet);
						planet.Productions = await ogameService.GetProductions(planet);
						planet.Constructions = await ogameService.GetConstructions(planet);
						if (planet is Planet) {
							planet.ResourceSettings = await ogameService.GetResourceSettings(planet as Planet);
							planet.ResourcesProduction = await ogameService.GetResourcesProduction(planet as Planet);
						}
						planet.Buildings = await ogameService.GetBuildings(planet);
						planet.Facilities = await ogameService.GetFacilities(planet);
						planet.Ships = await ogameService.GetShips(planet);
						planet.Defences = await ogameService.GetDefences(planet);
						break;
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"An error has occurred with update {UpdateTypes.ToString()}. Skipping update");
			}
			return planet;
		}

		public async Task<List<Celestial>> UpdatePlanets(UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogLevel.Information, LogSender.Tbot, $"Updating userData.celestials... Mode: {UpdateTypes.ToString()}");
			List<Celestial> localPlanets = await GetPlanets();
			List<Celestial> newPlanets = new();
			try {
				foreach (Celestial planet in localPlanets) {
					newPlanets.Add(await UpdatePlanet(planet, UpdateTypes));
				}
				return newPlanets;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdatePlanets({UpdateTypes.ToString()}) Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return newPlanets;
			}
		}

		public async Task<List<Celestial>> GetPlanets() {
			List<Celestial> localPlanets = _tbotInstance.UserData.celestials ?? new();
			try {
				List<Celestial> ogamedPlanets = await _ogameService.GetCelestials();
				if (localPlanets.Count() == 0 || ogamedPlanets.Count() != _tbotInstance.UserData.celestials.Count) {
					localPlanets = ogamedPlanets.ToList();
				}
				return localPlanets;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"GetPlanets() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return localPlanets;
			}
		}

		public async Task<Slots> UpdateSlots() {
			try {
				return await _ogameService.GetSlots();
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateSlots() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public async Task<List<GalaxyInfo>> UpdateGalaxyInfos() {
			try {
				List<GalaxyInfo> galaxyInfos = new();
				Planet newPlanet = new();
				List<Celestial> newCelestials = _tbotInstance.UserData.celestials.ToList();
				foreach (Planet planet in _tbotInstance.UserData.celestials.Where(p => p is Planet)) {
					newPlanet = planet;
					var gi = await _ogameService.GetGalaxyInfo(planet.Coordinate);
					if (gi.Planets.Any(p => p != null && p.ID == planet.ID)) {
						newPlanet.Debris = gi.Planets.Single(p => p != null && p.ID == planet.ID).Debris;
						galaxyInfos.Add(gi);
					}

					if (_tbotInstance.UserData.celestials.Any(p => p.HasCoords(newPlanet.Coordinate))) {
						Planet oldPlanet = _tbotInstance.UserData.celestials.Unique().SingleOrDefault(p => p.HasCoords(newPlanet.Coordinate)) as Planet;
						newCelestials.Remove(oldPlanet);
						newCelestials.Add(newPlanet);
					}
				}
				_tbotInstance.UserData.celestials = newCelestials;
				return galaxyInfos;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateGalaxyInfos() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public async Task<ServerData> UpdateServerData() {
			try {
				return await _ogameService.GetServerData();
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateServerData() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public async Task<Server> UpdateServerInfo() {
			try {
				return await _ogameService.GetServerInfo();
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateServerInfo() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public async Task<UserInfo> UpdateUserInfo() {
			try {
				UserInfo user = await _ogameService.GetUserInfo();
				user.Class = await _ogameService.GetUserClass();
				return user;
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateUserInfo() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new() {
					PlayerID = 0,
					PlayerName = "Uninitialized",
					Class = CharacterClass.NoClass,
					Points = 0,
					HonourPoints = 0,
					Rank = 0,
					Total = 0
				};
			}
		}

		public async Task<List<Celestial>> UpdateCelestials() {
			try {
				return await _ogameService.GetCelestials();
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateCelestials() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return _tbotInstance.UserData.celestials ?? new();
			}
		}

		public async Task<Researches> UpdateResearches() {
			try {
				return await _ogameService.GetResearches();
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateResearches() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public async Task<Staff> UpdateStaff() {
			try {
				return await _ogameService.GetStaff();
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateStaff() Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}


		public async Task<bool> CheckCelestials() {
			try {
				if (!_tbotInstance.UserData.isSleeping) {
					var newCelestials = await UpdateCelestials();
					if (_tbotInstance.UserData.celestials.Count() != newCelestials.Count) {
						if (_tbotInstance.UserData.celestials.Count() > newCelestials.Count) {
							_tbotInstance.log(LogLevel.Warning, LogSender.Tbot, "Less userData.celestials than last check detected!!");
						} else {
							_tbotInstance.log(LogLevel.Information, LogSender.Tbot, "More userData.celestials than last check detected");
							await _tbotInstance.InitializeFeature(Feature.BrainAutoMine);
						}
						_tbotInstance.UserData.celestials = newCelestials.Unique().ToList();
						return true;
					}
				}
			} catch {
				_tbotInstance.UserData.celestials = _tbotInstance.UserData.celestials.Unique().ToList();
			}
			return false;   // Boolean indicates if SleepMode must start
		}
	}
}
