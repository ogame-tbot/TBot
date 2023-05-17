using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace TBot.Ogame.Infrastructure {
	public interface IOgameService {
		event EventHandler OnError;
		void Initialize(Credentials credentials,
				Device device,
				ProxySettings proxySettings,
				string host = "127.0.0.1",
				int port = 8080,
				string captchaKey = "");
		string GetExecutableName();
		bool ValidatePrerequisites();
		Task BuildCancelable(Celestial celestial, Buildables buildable);
		Task BuildCancelable(Celestial celestial, LFBuildables buildable);
		Task BuildCancelable(Celestial celestial, LFTechno buildable);
		Task BuildConstruction(Celestial celestial, Buildables buildable);
		Task BuildDefences(Celestial celestial, Buildables buildable, long quantity);
		Task BuildMilitary(Celestial celestial, Buildables buildable, long quantity);
		Task BuildShips(Celestial celestial, Buildables buildable, long quantity);
		Task BuildTechnology(Celestial celestial, Buildables buildable);
		Task<OfferOfTheDayStatus> BuyOfferOfTheDay();
		Task CancelConstruction(Celestial celestial);
		Task CancelFleet(Fleet fleet);
		Task CancelResearch(Celestial celestial);
		Task DeleteAllEspionageReports();
		Task DeleteReport(int reportID);
		Task DoAuction(Celestial celestial, Resources resources);
		Task<List<AttackerFleet>> GetAttacks();
		Task<Buildings> GetBuildings(Celestial celestial);
		Task<CaptchaChallenge> GetCaptchaChallenge();
		Task<Celestial> GetCelestial(Celestial celestial);
		Task<List<Celestial>> GetCelestials();
		Task<Constructions> GetConstructions(Celestial celestial);
		Task<Auction> GetCurrentAuction();
		Task<Defences> GetDefences(Celestial celestial);
		Task<EspionageReport> GetEspionageReport(Coordinate coordinate);
		Task<EspionageReport> GetEspionageReport(int msgId);
		Task<List<EspionageReportSummary>> GetEspionageReports();
		Task<Facilities> GetFacilities(Celestial celestial);
		Task<List<Fleet>> GetFleets();
		Task<GalaxyInfo> GetGalaxyInfo(Coordinate coordinate);
		Task<GalaxyInfo> GetGalaxyInfo(int galaxy, int system);
		Task<LFBuildings> GetLFBuildings(Celestial celestial);
		Task<LFTechs> GetLFTechs(Celestial celestial);
		Task<Moon> GetMoon(Moon moon);
		Task<List<Moon>> GetMoons();
		Task<string> GetOgamedIP();
		Task<Planet> GetPlanet(Planet planet);
		Task<List<Planet>> GetPlanets();
		Task<Resources> GetPrice(Buildables buildable, long levelOrQuantity);
		Task<Resources> GetPrice(LFBuildables buildable, long levelOrQuantity);
		Task<Resources> GetPrice(LFTechno buildable, long levelOrQuantity);
		Task<List<Production>> GetProductions(Celestial celestial);
		Task<Researches> GetResearches();
		Task<Resources> GetResources(Celestial celestial);
		Task<ResourceSettings> GetResourceSettings(Planet planet);
		Task<ResourcesProduction> GetResourcesProduction(Planet planet);
		Task<ServerData> GetServerData();
		Task<int> GetServerFleetSpeed();
		Task<Server> GetServerInfo();
		Task<string> GetServerLanguage();
		Task<string> GetServerName();
		Task<int> GetServerSpeed();
		Task<DateTime> GetServerTime();
		Task<string> GetServerUrl();
		Task<string> GetServerVersion();
		Task<Ships> GetShips(Celestial celestial);
		Task<Slots> GetSlots();
		Task<Staff> GetStaff();
		Task<string> GetTbotIP();
		Task<Techs> GetTechs(Celestial celestial);
		Task<CharacterClass> GetUserClass();
		Task<UserInfo> GetUserInfo();
		Task<string> GetUsername();
		Task<bool> HasAdmiral();
		Task<bool> HasCommander();
		Task<bool> HasEngineer();
		Task<bool> HasGeologist();
		Task<bool> HasTechnocrat();
		bool IsPortAvailable(string host, int port = 8080);
		Task<bool> IsUnderAttack();
		Task<bool> IsVacationMode();
		Task JumpGate(Celestial origin, Celestial destination, Ships ships);
		Task<bool> SendDiscovery(Celestial origin, Coordinate coords);
		void KillOgamedExecutable(CancellationToken ct = default);
		Task Login();
		Task Logout();
		Task<List<Fleet>> Phalanx(Celestial origin, Coordinate coords);
		void RerunOgamed();
		Task<Fleet> SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Resources payload);
		Task SendMessage(int playerID, string message);
		Task SetUserAgent(string userAgent);
		Task SolveCaptcha(string challengeID, int answer);
		Task<bool> AbandonCelestial(Celestial celestial);
	}
}
