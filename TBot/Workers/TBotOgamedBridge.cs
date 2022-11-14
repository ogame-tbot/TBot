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
    public static class ITBotHelper
    {
		public static async Task<DateTime> GetDateTime(ITBotMain tbotInstance) {
			try {
				IOgameService ogameService = tbotInstance.OgamedInstance;
				DateTime dateTime = await ogameService.GetServerTime();
				if (dateTime.Kind == DateTimeKind.Utc)
					return dateTime.ToLocalTime();
				else
					return dateTime;
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"GetDateTime() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				var fallback = DateTime.Now;
				if (fallback.Kind == DateTimeKind.Utc)
					return fallback.ToLocalTime();
				else
					return fallback;
			}
		}
		public static async Task<Celestial> UpdatePlanet(ITBotMain tbotInstance, Celestial planet, UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogLevel.Information, LogSender.Tbot, $"Updating {planet.ToString()}. Mode: {UpdateTypes.ToString()}");
			IOgameService ogameService = tbotInstance.OgamedInstance;
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
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"An error has occurred with update {UpdateTypes.ToString()}. Skipping update");
			}
			return planet;
		}

		public static async Task<List<Celestial>> UpdatePlanets(ITBotMain tbotInstance, UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogLevel.Information, LogSender.Tbot, $"Updating userData.celestials... Mode: {UpdateTypes.ToString()}");
			List<Celestial> localPlanets = await GetPlanets(tbotInstance);
			List<Celestial> newPlanets = new();
			try {
				foreach (Celestial planet in localPlanets) {
					newPlanets.Add(await UpdatePlanet(tbotInstance, planet, UpdateTypes));
				}
				return newPlanets;
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdatePlanets({UpdateTypes.ToString()}) Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return newPlanets;
			}
		}

		public static async Task<List<Celestial>> GetPlanets(ITBotMain tbotInstance) {
			List<Celestial> localPlanets = tbotInstance.UserData.celestials ?? new();
			try {
				List<Celestial> ogamedPlanets = await tbotInstance.OgamedInstance.GetCelestials();
				if (localPlanets.Count() == 0 || ogamedPlanets.Count() != tbotInstance.UserData.celestials.Count) {
					localPlanets = ogamedPlanets.ToList();
				}
				return localPlanets;
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"GetPlanets() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return localPlanets;
			}
		}

		public static async Task<Slots> UpdateSlots(ITBotMain tbotInstance) {
			try {
				return await tbotInstance.OgamedInstance.GetSlots();
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateSlots() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public static async Task<List<GalaxyInfo>> UpdateGalaxyInfos(ITBotMain tbotInstance) {
			try {
				List<GalaxyInfo> galaxyInfos = new();
				Planet newPlanet = new();
				List<Celestial> newCelestials = tbotInstance.UserData.celestials.ToList();
				foreach (Planet planet in tbotInstance.UserData.celestials.Where(p => p is Planet)) {
					newPlanet = planet;
					var gi = await tbotInstance.OgamedInstance.GetGalaxyInfo(planet.Coordinate);
					if (gi.Planets.Any(p => p != null && p.ID == planet.ID)) {
						newPlanet.Debris = gi.Planets.Single(p => p != null && p.ID == planet.ID).Debris;
						galaxyInfos.Add(gi);
					}

					if (tbotInstance.UserData.celestials.Any(p => p.HasCoords(newPlanet.Coordinate))) {
						Planet oldPlanet = tbotInstance.UserData.celestials.Unique().SingleOrDefault(p => p.HasCoords(newPlanet.Coordinate)) as Planet;
						newCelestials.Remove(oldPlanet);
						newCelestials.Add(newPlanet);
					}
				}
				tbotInstance.UserData.celestials = newCelestials;
				return galaxyInfos;
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateGalaxyInfos() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public static async Task<ServerData> UpdateServerData(ITBotMain tbotInstance) {
			try {
				return await tbotInstance.OgamedInstance.GetServerData();
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateServerData() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public static async Task<Server> UpdateServerInfo(ITBotMain tbotInstance) {
			try {
				return await tbotInstance.OgamedInstance.GetServerInfo();
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateServerInfo() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public static async Task<UserInfo> UpdateUserInfo(ITBotMain tbotInstance) {
			try {
				UserInfo user = await tbotInstance.OgamedInstance.GetUserInfo();
				user.Class = await tbotInstance.OgamedInstance.GetUserClass();
				return user;
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateUserInfo() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
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

		public static async Task<List<Celestial>> UpdateCelestials(ITBotMain tbotInstance) {
			try {
				return await tbotInstance.OgamedInstance.GetCelestials();
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateCelestials() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return tbotInstance.UserData.celestials ?? new();
			}
		}

		public static async Task<Researches> UpdateResearches(ITBotMain tbotInstance) {
			try {
				return await tbotInstance.OgamedInstance.GetResearches();
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateResearches() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		public static async Task<Staff> UpdateStaff(ITBotMain tbotInstance) {
			try {
				return await tbotInstance.OgamedInstance.GetStaff();
			} catch (Exception e) {
				tbotInstance.log(LogLevel.Debug, LogSender.Tbot, $"UpdateStaff() Exception: {e.Message}");
				tbotInstance.log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}


		public static async Task<bool> CheckCelestials(ITBotMain tbotInstance) {
			try {
				if (!tbotInstance.UserData.isSleeping) {
					var newCelestials = await UpdateCelestials(tbotInstance);
					if (tbotInstance.UserData.celestials.Count() != newCelestials.Count) {
						tbotInstance.UserData.celestials = newCelestials.Unique().ToList();
						if (tbotInstance.UserData.celestials.Count() > newCelestials.Count) {
							tbotInstance.log(LogLevel.Warning, LogSender.Tbot, "Less userData.celestials than last check detected!!");
						} else {
							tbotInstance.log(LogLevel.Information, LogSender.Tbot, "More userData.celestials than last check detected");
						}
						return true;
					}
				}
			} catch {
				tbotInstance.UserData.celestials = tbotInstance.UserData.celestials.Unique().ToList();
			}
			return false;	// Boolean indicates if SleepMode must start
		}
	}
}
