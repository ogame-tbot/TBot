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
using RestSharp;
using Newtonsoft.Json;
using System.Text;
using TBot.Ogame.Infrastructure.Exceptions;
using TBot.Ogame.Infrastructure.Enums;
using Newtonsoft.Json.Linq;
using TBot.Common.Logging;
using System.Net.Http;
using Polly;
using Polly.Extensions.Http;
using Polly.Retry;

namespace TBot.Ogame.Infrastructure {

	public class OgameService : IOgameService {

		private readonly ILoggerService<OgameService> _logger;
		private HttpClient _client;
		private Process? _ogamedProcess;
		private string _username;

		private Credentials _credentials;
		private Device _device;
		private string _host;
		private int _port;
		private string _captchaKey;
		private ProxySettings _proxySettings;
		private string _cookiesPath;

		public event EventHandler OnError;

		bool _mustKill = false;	// used whenever we want to actually kill ogamed

		public OgameService(ILoggerService<OgameService> logger) {
			_logger = logger;
		}

		public void Initialize(Credentials credentials,
				Device device,
				ProxySettings proxySettings,
				string host = "127.0.0.1",
				int port = 8080,
				string captchaKey = "") {
			_credentials = credentials;
			_device = device;
			_host = host;
			_port = port;
			_captchaKey = captchaKey;
			_proxySettings = proxySettings;

			_username = credentials.Username;

			_ogamedProcess = ExecuteOgamedExecutable(credentials, device, host, port, captchaKey, proxySettings);

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
				_logger.WriteLog(LogLevel.Error, LogSender.Main, $"\"{GetExecutableName()}\" not found. Cannot proceed...");
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
				_logger.WriteLog(LogLevel.Information, LogSender.OGameD, $"PortAvailable({port} Error: {e.Message}");
				return false;
			}
		}

		public string GetExecutableName() {
			return (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) ? "ogamed.exe" : "ogamed";
		}

		internal Process ExecuteOgamedExecutable(Credentials credentials, Device device, string host = "localhost", int port = 8080, string captchaKey = "", ProxySettings proxySettings = null) {
			Process? ogameProc = null;
			try {
				string args = $"--universe=\"{credentials.Universe}\" --username={credentials.Username} --password={credentials.Password} --device-name={device.Name} --language={credentials.Language} --auto-login=false --port={port} --host=0.0.0.0 --api-new-hostname=http://{host}:{port}";
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

				if (device.System != "") {
					args += $" --device-system={device.System}";
				}
				if (device.Browser != "") {
					args += $" --device-browser={device.Browser}";
				}
				if (device.Memory > 0) {
					args += $" --device-memory={device.Memory}";
				}
				if (device.Concurrency > 0) {
					args += $" --device-concurrency={device.Concurrency}";
				}
				if (device.Color > 0) {
					args += $" --device-color={device.Color}";
				}
				if (device.Width > 0) {
					args += $" --device-width={device.Width}";
				}
				if (device.Height > 0) {
					args += $" --device-height={device.Height}";
				}
				if (device.Timezone != "") {
					args += $" --device-timezone={device.Timezone}";
				}
				if (device.Lang != "") {
					args += $" --device-lang={device.Lang}";
				}

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

				_logger.WriteLog(LogLevel.Information, LogSender.OGameD, $"OgameD Started with PID {ogameProc.Id}");   // This would raise an exception
				_mustKill = false;
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Error, LogSender.OGameD, $"Error executing ogamed instance: {ex.Message}");
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
			_logger.WriteLog(isErr ? LogLevel.Error : LogLevel.Information, LogSender.OGameD, $"[{_username}] \"{payload}\"");
		}

		private void handle_ogamedProcess_Exited(object? sender, EventArgs e) {
			var totalRunTime = Math.Round((_ogamedProcess.ExitTime - _ogamedProcess.StartTime).TotalMilliseconds);
			_logger.WriteLog(LogLevel.Information, LogSender.OGameD, $"OgameD Exited {_ogamedProcess.ExitCode}" +
				$" TotalTime(ms) {totalRunTime}");

			// If total run time is very low, then OGameD encountered a serious problem
			if (_mustKill == false) {
				if (totalRunTime > 500) {
					Task.Delay(1000).Wait();
					RerunOgamed();
				} else {
					_ogamedProcess.Dispose();
					_ogamedProcess = null;
					if (OnError != null) {
						OnError(this, EventArgs.Empty);
					}
				}
			} else {
				_ogamedProcess.Dispose();
				_ogamedProcess = null;
			}
		}
		public void RerunOgamed() {
			ExecuteOgamedExecutable(_credentials, _device, _host, _port, _captchaKey, _proxySettings);
		}

		public void KillOgamedExecutable(CancellationToken ct = default) {
			if (_ogamedProcess != null) {
				_mustKill = true;
				_ogamedProcess.Kill();
				_ogamedProcess.Dispose();
				_ogamedProcess = null;
			}
		}

		private async Task<T> ManageResponse<T>(HttpResponseMessage response) {
			OgamedResponse result = null;
			try {
				var jsonResponseContent = await response.Content.ReadAsStringAsync();
				if (jsonResponseContent != null) {
					result = JsonConvert.DeserializeObject<OgamedResponse>(jsonResponseContent, new JsonSerializerSettings {
						DateTimeZoneHandling = DateTimeZoneHandling.Local,
						NullValueHandling = NullValueHandling.Ignore
					});
				}
				else {
					response.EnsureSuccessStatusCode();
				}
			}
			catch {
				response.EnsureSuccessStatusCode();
			}
			if (result != null) {
				if (result.Status == null) {
					throw new OgamedException("An error has occurred");
				}
				else if (result.Status != "ok") {
					throw new OgamedException($"An error has occurred: Status Code: {response.StatusCode} Status: {result?.Status} - Message: {result?.Message}");
				} else {
					if (result.Result is JObject) {
						var jObject = result.Result as JObject;
						return jObject.ToObject<T>();
					} else if (result.Result is JArray) {
						var jArray = result.Result as JArray;
						return jArray.ToObject<T>();
					}
				}
			}
			else {
				response.EnsureSuccessStatusCode();
			}
			return (T) result.Result;
		}

		private AsyncRetryPolicy<HttpResponseMessage> GetRetryPolicy() {
			return HttpPolicyExtensions.HandleTransientHttpError()
				.WaitAndRetryAsync(3, retryCount => TimeSpan.FromSeconds(Math.Pow(2, retryCount)));
		}

		private async Task<T> GetAsync<T>(string resource, bool ensureSuccess = true) {
			var response = await GetRetryPolicy()
				.ExecuteAsync(async () => {
					var request = new HttpRequestMessage() {
						Method = HttpMethod.Get,
						RequestUri = new Uri(resource, UriKind.Relative)
					};
					var response = await _client.SendAsync(request);
					return response;
				});
			return await ManageResponse<T>(response);
		}

		private async Task<T> PostAsync<T>(string resource, params KeyValuePair<string, string>[] parameters) {
			var response = await GetRetryPolicy()
				.ExecuteAsync(async () => {
					var request = new HttpRequestMessage() {
						Method = HttpMethod.Post,
						RequestUri = new Uri(resource, UriKind.Relative)
					};
					request.Content = new FormUrlEncodedContent(parameters);
					var response = await _client.SendAsync(request);
					return response;
				});
			return await ManageResponse<T>(response);
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

		public async Task<OfferOfTheDayStatus> BuyOfferOfTheDay() {
			OfferOfTheDayStatus sts = OfferOfTheDayStatus.OfferOfTheDayUnknown;
			// 200 means it has been bought
			// 400 {"Status":"error","Code":400,"Message":"Offer already accepted","Result":null}
			try {
				await GetAsync<object>("/bot/buy-offer-of-the-day");
				sts = OfferOfTheDayStatus.OfferOfTheDayBougth;
			} catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.BadRequest) {
				sts = OfferOfTheDayStatus.OfferOfTheDayAlreadyBought;
			} catch {
				// Unknown!
			}
			return sts;
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
			List<Fleet> phalanxedFleets = new();
			try {
				phalanxedFleets = await GetAsync<List<Fleet>>($"/bot/moons/{origin.ID}/phalanx/{coords.Galaxy}/{coords.System}/{coords.Position}");
			} catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.BadRequest) {
				// Means not fleet or can't phalanx. Got to check better with ogamed
			}
			return phalanxedFleets;
		}
		public async Task<bool> SendDiscovery(Celestial origin, Coordinate coords) {
			bool success = false;
			try {
				List<KeyValuePair<string, string>> parameters = new List<KeyValuePair<string, string>>();
				parameters.Add(new KeyValuePair<string, string>("galaxy", coords.Galaxy.ToString()));
				parameters.Add(new KeyValuePair<string, string>("system", coords.System.ToString()));
				parameters.Add(new KeyValuePair<string, string>("position", coords.Position.ToString()));
				success = await PostAsync<bool>($"/bot/planets/{origin.ID}/send-discovery", parameters.ToArray());
			} catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.BadRequest) {
				success = false;
			} catch (OgamedException e) {
				success = false;
			} catch (Exception e) {
				success = false;
			}
			return success;
		}

		public async Task<bool> AbandonCelestial(Celestial celestial) {
			bool success = false;
			try {
				Abandon result = await GetAsync<Abandon>($"/bot/celestials/{celestial.ID}/abandon");
				if (result.Result == "succeed") {
					success = true;
				} else {
					success = false;
				}
			} catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.BadRequest) {
				success = false;
			} catch (OgamedException e) {
				success = false;
			} catch (Exception e) {
				success = false;
			}
			return success;
		}
	}
}
