using RestSharp;
using System;
using System.Collections.Generic;
using Tbot.Model;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Tbot.Services {
	class OgamedService {
		private RestClient Client { get; set; }

		public OgamedService(Credentials credentials, string host = "127.0.0.1", int port = 8080, string captchaKey = "", ProxySettings proxySettings = null) {
			ExecuteOgamedExecutable(credentials, host, port, captchaKey, proxySettings);

			var url = $"http://{host}:{port}";

			Client = new(url) {
				Timeout = 86400000,
				ReadWriteTimeout = 86400000
			};
		}

		private static void ExecuteOgamedExecutable(Credentials credentials, string host = "localhost", int port = 8080, string captchaKey = "", ProxySettings proxySettings = null) {
			try {
				string args = $"--universe=\"{credentials.Universe}\" --username={credentials.Username} --password={credentials.Password} --language={credentials.Language} --auto-login=false --port={port} --host=0.0.0.0 --api-new-hostname=http://{host}:{port} --cookies-filename=cookies.txt";

				if (captchaKey != "") {
					args += $" --nja-api-key={captchaKey}";
				}

				if (proxySettings.Enabled) {
					if (proxySettings.Type == "socks5" || proxySettings.Type == "http") {
						args += $" --proxy={proxySettings.Address}";
						args += $" --proxy-type={proxySettings.Type}";

						if (proxySettings.Username != "") {
							args += $" --proxy-username={proxySettings.Username}";
						}

						if (proxySettings.Password != "") {
							args += $" --proxy-password={proxySettings.Password}";
						}

						if (proxySettings.LoginOnly) {
							args += " --proxy-login-only=true";
						}
					}
				}
				if (credentials.IsLobbyPioneers) {
					args += " --lobby=lobby-pioneers";
				}

				if (credentials.BasicAuthUsername != "" && credentials.BasicAuthPassword != "") {
					args += $" --basic-auth-username={credentials.BasicAuthUsername}";
					args += $" --basic-auth-password={credentials.BasicAuthPassword}";
				}

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					Process.Start("ogamed.exe", args);
				} else {
					Process.Start("ogamed", args);
				}
			} catch {
				Environment.Exit(0);
			}
		}

		public void KillOgamedExecultable() {
			foreach (var process in Process.GetProcessesByName("ogamed")) {
				process.Kill();
			}
		}

		public bool SetUserAgent(string userAgent) {
			RestRequest request = new("/bot/set-user-agent", Method.POST);
			request.AddParameter("userAgent", userAgent);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool Login() {
			RestRequest request = new("/bot/login");

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool Logout() {
			RestRequest request = new("/bot/logout");

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public string GetCaptchaChallengeID() {
			RestRequest request = new("/bot/captcha/challengeID");

			try {
				var result = Client.Execute<OgamedResponse<string>>(request).Data;
				if (result.Status == "ok") {
					return result.Result;
				}
			} catch { }

			return "";
		}

		public byte[] GetCaptchaTextImage(string challengeID) {
			RestRequest request = new($"/bot/captcha/question/{challengeID}");

			try {
				var response = Client.Execute(request);
				var result = response.RawBytes;
				if (result != null) {
					return result;
				}
			} catch { }

			return new byte[0];
		}

		public byte[] GetCaptchaIcons(string challengeID) {
			RestRequest request = new($"/bot/captcha/icons/{challengeID}");

			try {
				var response = Client.Execute(request);
				var result = response.RawBytes;
				if (result != null) {
					return result;
				}
			} catch { }

			return new byte[0];
		}

		public void SolveCaptcha(string challengeID, int answer) {
			RestRequest request = new("/bot/captcha/solve", Method.POST);
			request.AddParameter("challenge_id", challengeID);
			request.AddParameter("answer", answer);

			try {
				Client.Execute(request);
			} catch { }
		}

		public string GetOgamedIP() {
			RestRequest request = new("/bot/ip");

			try {
				var result = Client.Execute<OgamedResponse<string>>(request).Data;
				if (result.Status == "ok") {
					return result.Result;
				}
			} catch { }

			return "";
		}

		public string GetTbotIP() {
			RestRequest request = new("https://jsonip.com/");

			try {
				var result = Client.Execute<JsonIpResponse>(request).Data;
				return result.Ip;
			} catch { }

			return "";
		}

		public Server GetServerInfo() {
			RestRequest request = new("/bot/server");

			var result = Client.Execute<OgamedResponse<Server>>(request).Data;

			var date = result.Result.StartDate.ToString();

			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public ServerData GetServerData() {
			RestRequest request = new("/bot/server-data");

			var result = Client.Execute<OgamedResponse<ServerData>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public string GetServerUrl() {
			RestRequest request = new("/bot/server-url");

			var result = Client.Execute<OgamedResponse<string>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public string GetServerLanguage() {
			RestRequest request = new("/bot/language");

			var result = Client.Execute<OgamedResponse<string>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public string GetServerName() {
			RestRequest request = new("/bot/universe-name");

			var result = Client.Execute<OgamedResponse<string>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public int GetServerSpeed() {
			RestRequest request = new("/bot/server/speed");

			var result = Client.Execute<OgamedResponse<int>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public int GetServerFleetSpeed() {
			RestRequest request = new("/bot/server/speed-fleet");

			var result = Client.Execute<OgamedResponse<int>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public string GetServerVersion() {
			RestRequest request = new("/bot/server/version");

			var result = Client.Execute<OgamedResponse<string>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public DateTime GetServerTime() {
			RestRequest request = new("/bot/server/time");

			var result = Client.Execute<OgamedResponse<DateTime>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public string GetUsername() {
			RestRequest request = new("/bot/username");

			var result = Client.Execute<OgamedResponse<string>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public UserInfo GetUserInfo() {
			RestRequest request = new("/bot/user-infos");

			var result = Client.Execute<OgamedResponse<UserInfo>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public CharacterClass GetUserClass() {
			RestRequest request = new("/bot/character-class");

			var result = Client.Execute<OgamedResponse<CharacterClass>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public List<Planet> GetPlanets() {
			RestRequest request = new("/bot/planets");

			var result = Client.Execute<OgamedResponse<List<Planet>>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Planet GetPlanet(Planet planet) {
			RestRequest request = new($"/bot/planets/{planet.ID}");

			var result = Client.Execute<OgamedResponse<Planet>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public List<Moon> GetMoons() {
			RestRequest request = new("/bot/moons");

			var result = Client.Execute<OgamedResponse<List<Moon>>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Moon GetMoon(Moon moon) {
			RestRequest request = new($"/bot/moons/{moon.ID}");

			var result = Client.Execute<OgamedResponse<Moon>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public List<Celestial> GetCelestials() {
			var planets = this.GetPlanets();
			var moons = this.GetMoons();

			List<Celestial> celestials = new();
			celestials.AddRange(planets);
			celestials.AddRange(moons);

			return celestials;
		}

		public Celestial GetCelestial(Celestial celestial) {
			if (celestial is Moon) {
				return this.GetMoon(celestial as Moon);
			} else if (celestial is Planet) {
				return this.GetPlanet(celestial as Planet);
			}

			return celestial;
		}

		public Techs GetTechs(Celestial celestial) {
			RestRequest request = new($"/bot/celestials/{celestial.ID}/techs");

			var result = Client.Execute<OgamedResponse<Techs>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Resources GetResources(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/resources");

			var result = Client.Execute<OgamedResponse<Resources>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Buildings GetBuildings(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/resources-buildings");

			var result = Client.Execute<OgamedResponse<Buildings>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Facilities GetFacilities(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/facilities");

			var result = Client.Execute<OgamedResponse<Facilities>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Defences GetDefences(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/defence");

			var result = Client.Execute<OgamedResponse<Defences>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Ships GetShips(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/ships");

			var result = Client.Execute<OgamedResponse<Ships>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool IsUnderAttack() {
			RestRequest request = new("/bot/is-under-attack");

			var result = Client.Execute<OgamedResponse<bool>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool IsVacationMode() {
			RestRequest request = new("/bot/is-vacation-mode");

			var result = Client.Execute<OgamedResponse<bool>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool HasCommander() {
			RestRequest request = new("/bot/has-commander");

			var result = Client.Execute<OgamedResponse<bool>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool HasAdmiral() {
			RestRequest request = new("/bot/has-admiral");

			var result = Client.Execute<OgamedResponse<bool>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool HasEngineer() {
			RestRequest request = new("/bot/has-engineer");

			var result = Client.Execute<OgamedResponse<bool>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool HasGeologist() {
			RestRequest request = new("/bot/has-geologist");

			var result = Client.Execute<OgamedResponse<bool>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool HasTechnocrat() {
			RestRequest request = new("/bot/has-technocrat");

			var result = Client.Execute<OgamedResponse<bool>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Staff GetStaff() {
			try {
				return new() {
					Commander = HasCommander(),
					Admiral = HasAdmiral(),
					Engineer = HasEngineer(),
					Geologist = HasGeologist(),
					Technocrat = HasTechnocrat()
				};
			} catch { }

			return new();
		}

		public bool BuyOfferOfTheDay() {
			RestRequest request = new("/bot/buy-offer-of-the-day");

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public List<AttackerFleet> GetAttacks() {
			RestRequest request = new("/bot/attacks");

			var result = Client.Execute<OgamedResponse<List<AttackerFleet>>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public List<Fleet> GetFleets() {
			RestRequest request = new("/bot/fleets");

			var result = Client.Execute<OgamedResponse<List<Fleet>>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Slots GetSlots() {
			RestRequest request = new("/bot/fleets/slots");

			var result = Client.Execute<OgamedResponse<Slots>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Researches GetResearches() {
			RestRequest request = new("/bot/get-research");

			var result = Client.Execute<OgamedResponse<Researches>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public List<Production> GetProductions(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/production");

			var result = Client.Execute<OgamedResponse<List<Production>>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public ResourceSettings GetResourceSettings(Planet planet) {
			RestRequest request = new($"/bot/planets/{planet.ID}/resource-settings");

			var result = Client.Execute<OgamedResponse<ResourceSettings>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public ResourcesProduction GetResourcesProduction(Planet planet) {
			RestRequest request = new($"/bot/planets/{planet.ID}/resources-details");

			var result = Client.Execute<OgamedResponse<ResourcesProduction>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public Constructions GetConstructions(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/constructions");

			var result = Client.Execute<OgamedResponse<Constructions>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool CancelConstruction(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/cancel-building", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool CancelResearch(Celestial celestial) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/cancel-research", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public Fleet SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Resources payload) {
			RestRequest request = new($"/bot/planets/{origin.ID}/send-fleet", Method.POST);
			request.AddParameter("galaxy", destination.Galaxy);
			request.AddParameter("system", destination.System);
			request.AddParameter("position", destination.Position);
			request.AddParameter("type", (int) destination.Type);

			foreach (PropertyInfo prop in ships.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(ships, null);
				if (qty == 0) {
					continue;
				}

				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					request.AddParameter("ships", (int) buildable + "," + prop.GetValue(ships, null), ParameterType.GetOrPost);
				}
			}

			request.AddParameter("mission", (int) mission, ParameterType.GetOrPost);

			request.AddParameter("speed", speed.ToString(), ParameterType.GetOrPost);

			request.AddParameter("metal", payload.Metal, ParameterType.GetOrPost);
			request.AddParameter("crystal", payload.Crystal, ParameterType.GetOrPost);
			request.AddParameter("deuterium", payload.Deuterium, ParameterType.GetOrPost);

			var result = Client.Execute<OgamedResponse<Fleet>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool CancelFleet(Fleet fleet) {
			RestRequest request = new($"/bot/fleets/{fleet.ID}/cancel", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public GalaxyInfo GetGalaxyInfo(Coordinate coordinate) {
			RestRequest request = new($"/bot/galaxy-infos/{coordinate.Galaxy}/{coordinate.System}");

			var result = Client.Execute<OgamedResponse<GalaxyInfo>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public GalaxyInfo GetGalaxyInfo(int galaxy, int system) {
			Coordinate coordinate = new() { Galaxy = galaxy, System = system };
			return this.GetGalaxyInfo(coordinate);
		}

		public bool BuildCancelable(Celestial celestial, Buildables buildable) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/build/cancelable/{(int) buildable}", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool BuildConstruction(Celestial celestial, Buildables buildable) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/build/building/{(int) buildable}", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool BuildTechnology(Celestial celestial, Buildables buildable) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/build/technology/{(int) buildable}", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool BuildMilitary(Celestial celestial, Buildables buildable, long quantity) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/build/production/{(int) buildable}/{quantity}", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool BuildShips(Celestial celestial, Buildables buildable, long quantity) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/build/ships/{(int) buildable}/{quantity}", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool BuildDefences(Celestial celestial, Buildables buildable, long quantity) {
			RestRequest request = new($"/bot/planets/{celestial.ID}/build/defence/{(int) buildable}/{quantity}", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public Resources GetPrice(Buildables buildable, long levelOrQuantity) {
			RestRequest request = new($"/bot/price/{(int) buildable}/{levelOrQuantity}");

			var result = Client.Execute<OgamedResponse<Resources>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public bool SendMessage(int playerID, string message) {
			RestRequest request = new("/bot/send-message", Method.POST);
			request.AddParameter("playerID", playerID);
			request.AddParameter("message", message);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool DeleteReport(int reportID) {
			RestRequest request = new($"/bot/delete-report/{reportID}", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public bool DeleteAllEspionageReports() {
			RestRequest request = new("/bot/delete-all-espionage-reports", Method.POST);

			try {
				var result = Client.Execute<OgamedResponse<object>>(request).Data;
				return (result.Status == "ok");
			} catch { }

			return false;
		}

		public List<EspionageReportSummary> GetEspionageReports() {
			RestRequest request = new("/bot/espionage-report");

			var result = Client.Execute<OgamedResponse<List<EspionageReportSummary>>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public EspionageReport GetEspionageReport(Coordinate coordinate) {
			RestRequest request = new($"/bot/espionage-report/{coordinate.Galaxy}/{coordinate.System}/{coordinate.Position}");

			var result = Client.Execute<OgamedResponse<EspionageReport>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}

		public EspionageReport GetEspionageReport(int msgId) {
			RestRequest request = new($"/bot/espionage-report/{msgId}");

			var result = Client.Execute<OgamedResponse<EspionageReport>>(request).Data;
			if (result.Status != "ok") {
				throw new Exception($"An error has occurred: Status: {result.Status} - Message: {result.Message}");
			}

			return result.Result;
		}
	}
}
