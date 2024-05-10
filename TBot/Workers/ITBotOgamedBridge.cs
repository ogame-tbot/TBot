using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public interface ITBotOgamedBridge {
		Task<bool> CheckCelestials();
		Task<DateTime> GetDateTime();
		Task<List<Celestial>> GetPlanets();
		Task<List<Celestial>> UpdateCelestials();
		Task<List<GalaxyInfo>> UpdateGalaxyInfos();
		Task<Celestial> UpdatePlanet(Celestial planet, UpdateTypes UpdateTypes = UpdateTypes.Full);
		Task<List<Celestial>> UpdatePlanets(UpdateTypes UpdateTypes = UpdateTypes.Full);
		Task<Researches> UpdateResearches();
		Task<ServerData> UpdateServerData();
		Task<Server> UpdateServerInfo();
		Task<Slots> UpdateSlots();
		Task<Staff> UpdateStaff();
		Task<AllianceClass> UpdateAllianceClass();
		Task<UserInfo> UpdateUserInfo();
	}
}
