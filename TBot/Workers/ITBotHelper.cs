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

namespace Tbot.Workers
{
    public abstract class ITBotHelper
    {
		private static ILoggerService<ITBotHelper> _logger;
		public static async Task<Celestial> UpdatePlanet(IOgameService ogameService, Celestial planet, UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogLevel.Information, LogSender.Tbot, $"Updating {planet.ToString()}. Mode: {UpdateTypes.ToString()}");
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
				_logger.WriteLog(LogLevel.Debug, LogSender.Tbot, $"Exception: {e.Message}");
				_logger.WriteLog(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				_logger.WriteLog(LogLevel.Warning, LogSender.Tbot, $"An error has occurred with update {UpdateTypes.ToString()}. Skipping update");
			}
			return planet;
		}
	}
}
