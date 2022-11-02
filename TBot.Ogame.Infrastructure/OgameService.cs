using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using TBot.Ogame.Infrastructure.Models;
using System.Net.Http.Headers;
using Microsoft.Extensions.Logging;
using TBot.Common;
using RestSharp;
using Newtonsoft.Json;
using System.Text;
using TBot.Ogame.Infrastructure.Exceptions;
using TBot.Ogame.Infrastructure.Enums;
using Newtonsoft.Json.Linq;

namespace TBot.Ogame.Infrastructure {

	public class OgameService : IOgameService {

		private readonly ILoggerService<OgameService> _logger;
		private HttpClient _client;
		private Process _ogamedProcess;
		private string _username;

		private Credentials _credentials;
		private string _host;
		private int _port;
		private string _captchaKey;
		private ProxySettings _proxySettings;
		private string _cookiesPath;


		public OgameService(ILoggerService<OgameService> logger) {
			_logger = logger;
		}

		public void Initialize(Credentials credentials,
				ProxySettings proxySettings,
				string host = "127.0.0.1",
				int port = 8080,
				string captchaKey = "",
				string cookiesPath = "") {
			_credentials = credentials;
			_host = host;
			_port = port;
			_captchaKey = captchaKey;
			_proxySettings = proxySettings;
			_cookiesPath = cookiesPath;

			_username = credentials.Username;

			_ogamedProcess = ExecuteOgamedExecutable(credentials, host, port, captchaKey, proxySettings, cookiesPath);

			_client = new HttpClient() {
				BaseAddress = new Uri($"http://{host}:{port}/"),
				Timeout = TimeSpan.FromSeconds(60)
			};
			if (credentials.BasicAuthUsername != "" && credentials.BasicAuthPassword != "") {
				_client.DefaultRequestHeaders.Authorization =
					new AuthenticationHeaderValue("Basic",
					Convert.ToBase64String(ASCIIEncoding.ASCII.GetBytes($"{credentials.BasicAuthUsername}:{credentials.BasicAuthPassword}")));
			}
		}

		public bool ValidatePrerequisites() {
			if (!File.Exists(Path.Combine(Path.GetFullPath(AppContext.BaseDirectory), GetExecutableName()))) {
				_logger.Log(LogLevel.Error, LogSender.Main, $"\"{GetExecutableName()}\" not found. Cannot proceed...");
				return false;
			}
			return true;
		}

		public bool IsPortAvailable(string host, int port = 8080) {
			try {
				// Host is not needed. We need to bind locally
				IPAddress localAddr = IPAddress.Parse("127.0.0.1");
				var server = new TcpListener(localAddr, port);

				server.Start(); // Should raise exception if not available

				server.Stop();

				return true;
			} catch (Exception e) {
				_logger.Log(LogLevel.Information, LogSender.OGameD, $"PortAvailable({port} Error: {e.Message}");
				return false;
			}
		}

		public string GetExecutableName() {
			return (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? "ogamed.exe" : "ogamed";
		}

		internal Process ExecuteOgamedExecutable(Credentials credentials, string host = "localhost", int port = 8080, string captchaKey = "", ProxySettings proxySettings = null, string cookiesPath = "cookies.txt") {
			Process? ogameProc = null;
			try {
				string args = $"--universe=\"{credentials.Universe}\" --username={credentials.Username} --password={credentials.Password} --language={credentials.Language} --auto-login=false --port={port} --host=0.0.0.0 --api-new-hostname=http://{host}:{port} --cookies-filename={cookiesPath}";
				if (captchaKey != "")
					args += $" --nja-api-key={captchaKey}";
				if (proxySettings.Enabled) {
					if (proxySettings.Type == "socks5" || proxySettings.Type == "http") {
						args += $" --proxy={proxySettings.Address}";
						args += $" --proxy-type={proxySettings.Type}";
						if (proxySettings.Username != "")
							args += $" --proxy-username={proxySettings.Username}";
						if (proxySettings.Password != "")
							args += $" --proxy-password={proxySettings.Password}";
						if (proxySettings.LoginOnly)
							args += " --proxy-login-only=true";
					}
				}
				if (credentials.IsLobbyPioneers)
					args += " --lobby=lobby-pioneers";
				if (credentials.BasicAuthUsername != "" && credentials.BasicAuthPassword != "") {
					args += $" --basic-auth-username={credentials.BasicAuthUsername}";
					args += $" --basic-auth-password={credentials.BasicAuthPassword}";
				}
				if (cookiesPath.Length > 0)
					args += $" --cookies-filename=\"{cookiesPath}\"";

				ogameProc = new Process();
				ogameProc.StartInfo.FileName = GetExecutableName();
				ogameProc.StartInfo.Arguments = args;
				ogameProc.EnableRaisingEvents = true;
				ogameProc.StartInfo.RedirectStandardOutput = true;
				ogameProc.StartInfo.RedirectStandardError = true;
				ogameProc.StartInfo.RedirectStandardInput = true;
				ogameProc.Exited += handle_ogamedProcess_Exited;
				ogameProc.OutputDataReceived += handle_ogamedProcess_OutputDataReceived;
				ogameProc.ErrorDataReceived += handle_ogamedProcess_ErrorDataReceived;

				ogameProc.Start();
				ogameProc.BeginErrorReadLine();
				ogameProc.BeginOutputReadLine();

				_logger.Log(LogLevel.Information, LogSender.OGameD, $"OgameD Started with PID {ogameProc.Id}");   // This would raise an exception
			} catch (Exception ex) {
				_logger.Log(LogLevel.Error, LogSender.OGameD, $"Error executing ogamed instance: {ex.Message}");
				Environment.Exit(0);
			}
			return ogameProc;
		}

		private void handle_ogamedProcess_ErrorDataReceived(object sender, DataReceivedEventArgs e) {
			if (e.Data?.Length != 0)
				dump_ogamedProcess_Log(true, e.Data);
		}

		private void handle_ogamedProcess_OutputDataReceived(object sender, DataReceivedEventArgs e) {
			if (e.Data?.Length != 0)
				dump_ogamedProcess_Log(false, e.Data);
		}

		private void dump_ogamedProcess_Log(bool isErr, string? payload) {
			_logger.Log(isErr ? LogLevel.Error : LogLevel.Information, LogSender.OGameD, $"[{_username}] \"{payload}\"");
		}

		private bool runningRerun = false;
		private void handle_ogamedProcess_Exited(object? sender, EventArgs e) {
			_logger.Log(LogLevel.Information, LogSender.OGameD, $"OgameD Exited {_ogamedProcess.ExitCode}" +
				$" TotalTime(ms) {Math.Round((_ogamedProcess.ExitTime - _ogamedProcess.StartTime).TotalMilliseconds)}");
			if (!runningRerun) {
				runningRerun = true;
				RerunOgamed();
			} else {
				_ogamedProcess.Dispose();
			}
		}
		public void RerunOgamed() {
			ExecuteOgamedExecutable(_credentials, _host, _port, _captchaKey, _proxySettings, _cookiesPath);
			runningRerun = false;
		}

		public void KillOgamedExecutable(CancellationToken ct = default) {
			if (_ogamedProcess != null) {
				_ogamedProcess.Kill();
				_ogamedProcess.Dispose();
			}
		}

		private async Task<T> GetAsync<T>(string resource) {
			var request = new HttpRequestMessage() {
				Method = HttpMethod.Get,
				RequestUri = new Uri(resource, UriKind.Relative)
			};

			var response = await _client.SendAsync(request);
			response.EnsureSuccessStatusCode();
			var jsonResponseContent = await response.Content.ReadAsStringAsync();
			var result = JsonConvert.DeserializeObject<OgamedResponse>(jsonResponseContent, new JsonSerializerSettings {
				DateTimeZoneHandling = DateTimeZoneHandling.Local,
				NullValueHandling = NullValueHandling.Ignore
			});
			if (result?.Status != "ok") {
				throw new OgamedException($"An error has occurred: Status: {result?.Status} - Message: {result?.Message}");
			} else {
				if (result.Result is JObject || result.Result is JArray) {
					return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings {
						DateTimeZoneHandling = DateTimeZoneHandling.Local,
						NullValueHandling = NullValueHandling.Ignore
					});
				}
			}
			return (T) result.Result;
		}

		private async Task<T> PostAsync<T>(string resource, params KeyValuePair<string, string>[] parameters) {
			var request = new HttpRequestMessage() {
				Method = HttpMethod.Post,
				RequestUri = new Uri(resource, UriKind.Relative)
			};
			request.Content = new FormUrlEncodedContent(parameters);

			var response = await _client.SendAsync(request);
			response.EnsureSuccessStatusCode();
			var jsonResponseContent = await response.Content.ReadAsStringAsync();
			var result = JsonConvert.DeserializeObject<OgamedResponse>(jsonResponseContent, new JsonSerializerSettings {
				DateTimeZoneHandling = DateTimeZoneHandling.Local,
				NullValueHandling = NullValueHandling.Ignore
			});
			if (result?.Status != "ok") {
				throw new OgamedException($"An error has occurred: Status: {result?.Status} - Message: {result?.Message}");
			} else {
				if (result.Result is JObject || result.Result is JArray) {
					return JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(result.Result), new JsonSerializerSettings {
						DateTimeZoneHandling = DateTimeZoneHandling.Local,
						NullValueHandling = NullValueHandling.Ignore
					});
				}
			}
			return (T) result.Result;
		}

		public async Task SetUserAgent(string userAgent) {
			await PostAsync<object>("/bot/set-user-agent", new KeyValuePair<string, string>("userAgent", userAgent));
		}

		public async Task Login() {
			await GetAsync<object>("/bot/login");
		}

		public async Task Logout() {
			await GetAsync<object>("/bot/logout");
		}

		public async Task<CaptchaChallenge> GetCaptchaChallenge() {
			try {
				return await GetAsync<CaptchaChallenge>("/bot/captcha/challenge");
			} catch {
				return new CaptchaChallenge();
			}
		}

		public async Task SolveCaptcha(string challengeID, int answer) {
			await PostAsync<object>($"/bot/captcha/solve",
				new KeyValuePair<string, string>("challenge_id", challengeID),
				new KeyValuePair<string, string>("answer", answer.ToString()));
		}

		public async Task<string> GetOgamedIP() {
			try {
				return await GetAsync<string>("/bot/ip");
			} catch {
				return "";
			}
		}

		public async Task<string> GetTbotIP() {
			var result = await GetAsync<dynamic>("https://jsonip.com");
			return result.ip;
		}

		public async Task<Server> GetServerInfo() {
			return await GetAsync<Server>("/bot/server");
		}

		public async Task<ServerData> GetServerData() {
			return await GetAsync<ServerData>("/bot/server-data");
		}

		public async Task<string> GetServerUrl() {
			return await GetAsync<string>("/bot/server-url");
		}

		public async Task<string> GetServerLanguage() {
			return await GetAsync<string>("/bot/language");
		}

		public async Task<string> GetServerName() {
			return await GetAsync<string>("/bot/universe-name");
		}

		public async Task<int> GetServerSpeed() {
			return await GetAsync<int>("/bot/server/speed");
		}

		public async Task<int> GetServerFleetSpeed() {
			return await GetAsync<int>("/bot/server/speed-fleet");
		}

		public async Task<string> GetServerVersion() {
			return await GetAsync<string>("/bot/server/version");
		}

		public async Task<DateTime> GetServerTime() {
			return await GetAsync<DateTime>("/bot/server/time");
		}

		public async Task<string> GetUsername() {
			return await GetAsync<string>("/bot/username");
		}

		public async Task<UserInfo> GetUserInfo() {
			return await GetAsync<UserInfo>("/bot/user-infos");
		}

		public async Task<CharacterClass> GetUserClass() {
			return await GetAsync<CharacterClass>("/bot/character-class");
		}

		public async Task<List<Planet>> GetPlanets() {
			return await GetAsync<List<Planet>>("/bot/planets");
		}

		public async Task<Planet> GetPlanet(Planet planet) {
			return await GetAsync<Planet>($"/bot/planets/{planet.ID}");
		}

		public async Task<List<Moon>> GetMoons() {
			return await GetAsync<List<Moon>>("/bot/moons");
		}

		public async Task<Moon> GetMoon(Moon moon) {
			return await GetAsync<Moon>($"/bot/moons/{moon.ID}");
		}

		public async Task<List<Celestial>> GetCelestials() {
			var planets = await GetPlanets();
			var moons = await GetMoons();
			List<Celestial> celestials = new();
			celestials.AddRange(planets);
			celestials.AddRange(moons);
			return celestials;
		}

		public async Task<Celestial> GetCelestial(Celestial celestial) {
			if (celestial is Moon)
				return await GetMoon(celestial as Moon);
			else if (celestial is Planet)
				return await GetPlanet(celestial as Planet);
			else
				return celestial;
		}

		public async Task<Techs> GetTechs(Celestial celestial) {
			return await GetAsync<Techs>($"/bot/celestials/{celestial.ID}/techs");
		}

		public async Task<Resources> GetResources(Celestial celestial) {
			return await GetAsync<Resources>($"/bot/planets/{celestial.ID}/resources");
		}

		public async Task<Buildings> GetBuildings(Celestial celestial) {
			return await GetAsync<Buildings>($"/bot/planets/{celestial.ID}/resources-buildings");
		}

		public async Task<LFBuildings> GetLFBuildings(Celestial celestial) {
			return await GetAsync<LFBuildings>($"/bot/planets/{celestial.ID}/lifeform-buildings");
		}

		public async Task<LFTechs> GetLFTechs(Celestial celestial) {
			return await GetAsync<LFTechs>($"/bot/planets/{celestial.ID}/lifeform-techs");
		}

		public async Task<Facilities> GetFacilities(Celestial celestial) {
			return await GetAsync<Facilities>($"/bot/planets/{celestial.ID}/facilities");
		}

		public async Task<Defences> GetDefences(Celestial celestial) {
			return await GetAsync<Defences>($"/bot/planets/{celestial.ID}/defence");
		}

		public async Task<Ships> GetShips(Celestial celestial) {
			return await GetAsync<Ships>($"/bot/planets/{celestial.ID}/ships");
		}

		public async Task<bool> IsUnderAttack() {
			return await GetAsync<bool>("/bot/is-under-attack");
		}

		public async Task<bool> IsVacationMode() {
			return await GetAsync<bool>("/bot/is-vacation-mode");
		}

		public async Task<bool> HasCommander() {
			return await GetAsync<bool>("/bot/has-commander");
		}

		public async Task<bool> HasAdmiral() {
			return await GetAsync<bool>("/bot/has-admiral");
		}

		public async Task<bool> HasEngineer() {
			return await GetAsync<bool>("/bot/has-engineer");
		}

		public async Task<bool> HasGeologist() {
			return await GetAsync<bool>("/bot/has-geologist");
		}

		public async Task<bool> HasTechnocrat() {
			return await GetAsync<bool>("/bot/has-technocrat");
		}

		public async Task<Staff> GetStaff() {
			try {
				return new() {
					Commander = await HasCommander(),
					Admiral = await HasAdmiral(),
					Engineer = await HasEngineer(),
					Geologist = await HasGeologist(),
					Technocrat = await HasTechnocrat()
				};
			} catch {
				return new();
			}
		}

		public async Task BuyOfferOfTheDay() {
			await GetAsync<object>("/bot/buy-offer-of-the-day");
		}

		public async Task<List<AttackerFleet>> GetAttacks() {
			return await GetAsync<List<AttackerFleet>>("/bot/attacks");
		}

		public async Task<List<Fleet>> GetFleets() {
			return await GetAsync<List<Fleet>>("/bot/fleets");
		}

		public async Task<Slots> GetSlots() {
			return await GetAsync<Slots>("/bot/fleets/slots");
		}

		public async Task<Researches> GetResearches() {
			return await GetAsync<Researches>("/bot/get-research");
		}

		public async Task<List<Production>> GetProductions(Celestial celestial) {
			return await GetAsync<List<Production>>($"/bot/planets/{celestial.ID}/production");
		}

		public async Task<ResourceSettings> GetResourceSettings(Planet planet) {
			return await GetAsync<ResourceSettings>($"/bot/planets/{planet.ID}/resource-settings");
		}

		public async Task<ResourcesProduction> GetResourcesProduction(Planet planet) {
			return await GetAsync<ResourcesProduction>($"/bot/planets/{planet.ID}/resources-details");
		}

		public async Task<Constructions> GetConstructions(Celestial celestial) {
			return await GetAsync<Constructions>($"/bot/planets/{celestial.ID}/constructions");
		}

		public async Task CancelConstruction(Celestial celestial) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/cancel-building");
		}

		public async Task CancelResearch(Celestial celestial) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/cancel-research");
		}

		public async Task<Fleet> SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Resources payload) {
			List<KeyValuePair<string, string>> parameters = new List<KeyValuePair<string, string>>();
			parameters.Add(new KeyValuePair<string, string>("galaxy", destination.Galaxy.ToString()));
			parameters.Add(new KeyValuePair<string, string>("system", destination.System.ToString()));
			parameters.Add(new KeyValuePair<string, string>("position", destination.Position.ToString()));
			parameters.Add(new KeyValuePair<string, string>("type", ((int) destination.Type).ToString()));

			var request = new RestRequest {
				Resource = $"/bot/planets/{origin.ID}/send-fleet",
				Method = Method.Post,
			};
			foreach (PropertyInfo prop in ships.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(ships, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					parameters.Add(new KeyValuePair<string, string>("ships", (int) buildable + "," + prop.GetValue(ships, null)));
				}
			}
			parameters.Add(new KeyValuePair<string, string>("mission", ((int) mission).ToString()));


			parameters.Add(new KeyValuePair<string, string>("speed", speed.ToString()));
			parameters.Add(new KeyValuePair<string, string>("metal", payload.Metal.ToString()));
			parameters.Add(new KeyValuePair<string, string>("crystal", payload.Crystal.ToString()));
			parameters.Add(new KeyValuePair<string, string>("deuterium", payload.Deuterium.ToString()));
			parameters.Add(new KeyValuePair<string, string>("food", payload.Food.ToString()));

			return await PostAsync<Fleet>($"/bot/planets/{origin.ID}/send-fleet", parameters.ToArray());
		}

		public async Task CancelFleet(Fleet fleet) {
			await PostAsync<object>($"/bot/fleets/{fleet.ID}/cancel");
		}

		public async Task<GalaxyInfo> GetGalaxyInfo(Coordinate coordinate) {
			return await GetAsync<GalaxyInfo>($"/bot/galaxy-infos/{coordinate.Galaxy}/{coordinate.System}");
		}

		public async Task<GalaxyInfo> GetGalaxyInfo(int galaxy, int system) {
			Coordinate coordinate = new() { Galaxy = galaxy, System = system };
			return await GetGalaxyInfo(coordinate);
		}

		public async Task BuildCancelable(Celestial celestial, Buildables buildable) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/build/cancelable/{(int) buildable}");
		}

		public async Task BuildCancelable(Celestial celestial, LFBuildables buildable) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/build/cancelable/{(int) buildable}");
		}

		public async Task BuildCancelable(Celestial celestial, LFTechno buildable) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/build/cancelable/{(int) buildable}");
		}

		public async Task BuildConstruction(Celestial celestial, Buildables buildable) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/build/building/{(int) buildable}");
		}

		public async Task BuildTechnology(Celestial celestial, Buildables buildable) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/build/technology/{(int) buildable}");
		}

		public async Task BuildMilitary(Celestial celestial, Buildables buildable, long quantity) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/build/production/{(int) buildable}/{quantity}");
		}

		public async Task BuildShips(Celestial celestial, Buildables buildable, long quantity) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/build/ships/{(int) buildable}/{quantity}");
		}

		public async Task BuildDefences(Celestial celestial, Buildables buildable, long quantity) {
			await PostAsync<object>($"/bot/planets/{celestial.ID}/build/defence/{(int) buildable}/{quantity}");
		}

		public async Task<Resources> GetPrice(Buildables buildable, long levelOrQuantity) {
			return await GetAsync<Resources>($"/bot/price/{(int) buildable}/{levelOrQuantity}");
		}

		public async Task<Resources> GetPrice(LFBuildables buildable, long levelOrQuantity) {
			return await GetAsync<Resources>($"/bot/price/{(int) buildable}/{levelOrQuantity}");
		}

		public async Task<Resources> GetPrice(LFTechno buildable, long levelOrQuantity) {
			return await GetAsync<Resources>($"/bot/price/{(int) buildable}/{levelOrQuantity}");
		}

		public async Task<Auction> GetCurrentAuction() {
			return await GetAsync<Auction>("/bot/get-auction");
		}

		public async Task DoAuction(Celestial celestial, Resources resources) {
			await PostAsync<object>("/bot/do-auction",
				new KeyValuePair<string, string>($"{celestial.ID}", $"{resources.Metal}:{resources.Crystal}:{resources.Deuterium}"));
		}

		public async Task SendMessage(int playerID, string message) {
			await PostAsync<object>("/bot/send-message",
				new KeyValuePair<string, string>("playerID", playerID.ToString()),
				new KeyValuePair<string, string>("message", message));
		}

		public async Task DeleteReport(int reportID) {
			await PostAsync<object>($"/bot/delete-report/{reportID}");
		}

		public async Task DeleteAllEspionageReports() {
			await PostAsync<object>("/bot/delete-all-espionage-reports");
		}

		public async Task<List<EspionageReportSummary>> GetEspionageReports() {
			return await GetAsync<List<EspionageReportSummary>>("/bot/espionage-report");
		}

		public async Task<EspionageReport> GetEspionageReport(Coordinate coordinate) {
			return await GetAsync<EspionageReport>($"/bot/espionage-report/{coordinate.Galaxy}/{coordinate.System}/{coordinate.Position}");
		}

		public async Task<EspionageReport> GetEspionageReport(int msgId) {
			return await GetAsync<EspionageReport>($"/bot/espionage-report/{msgId}");
		}

		public async Task JumpGate(Celestial origin, Celestial destination, Ships ships) {
			List<KeyValuePair<string, string>> parameters = new List<KeyValuePair<string, string>>();

			parameters.Add(new KeyValuePair<string, string>("moonDestination", destination.ID.ToString()));

			foreach (PropertyInfo prop in ships.GetType().GetProperties()) {
				long qty = (long) prop.GetValue(ships, null);
				if (qty == 0)
					continue;
				if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable)) {
					parameters.Add(new KeyValuePair<string, string>("ships", $"{(int) buildable},{prop.GetValue(ships, null)}"));
				}
			}

			await PostAsync<object>($"/bot/moons/{origin.ID}/jump-gate", parameters.ToArray());
		}

		public async Task<List<Fleet>> Phalanx(Celestial origin, Coordinate coords) {

			return await GetAsync<List<Fleet>>($"/bot/moons/{origin.ID}/phalanx/{coords.Galaxy}/{coords.System}/{coords.Position}");
		}
	}
}
