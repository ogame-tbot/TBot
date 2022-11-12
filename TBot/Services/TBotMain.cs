using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using Tbot.Common.Extensions;
using Tbot.Common.Settings;
using Tbot.Helpers;
using Tbot.Includes;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Exceptions;
using TBot.Ogame.Infrastructure.Models;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static System.Reflection.Metadata.BlobBuilder;

namespace Tbot.Services {
	internal class TBotMain : IEquatable<TBotMain>, IAsyncDisposable, ITBotMain {
		private readonly IOgameService _ogameService;
		private readonly ILoggerService<TBotMain> _logger;
		private readonly ICalculationService _helpersService;

		private bool loggedIn = false;
		private Dictionary<string, Timer> timers;
		private ConcurrentDictionary<Feature, bool> features;
		private ConcurrentDictionary<Feature, SemaphoreSlim> xaSem = new();


		public UserData userData = new();
		public TelegramUserData telegramUserData = new();

		public long duration;
		public DateTime NextWakeUpTime;
		public DateTime startTime = DateTime.UtcNow;
		public SettingsFileWatcher settingsWatcher;

		private string settingsPath;
		private string instanceAlias;
		private dynamic settings;
		private ITelegramMessenger telegramMessenger;

		public TBotMain(
			IOgameService ogameService,
			ICalculationService helpersService,
			ILoggerService<TBotMain> logger) {

			_ogameService = ogameService;
			_logger = logger;
			_helpersService = helpersService;
		}

		private Credentials GetCredentialsFromSettings() {
			return new() {
				Universe = ((string) settings.Credentials.Universe).FirstCharToUpper(),
				Username = (string) settings.Credentials.Email,
				Password = (string) settings.Credentials.Password,
				Language = ((string) settings.Credentials.Language).ToLower(),
				IsLobbyPioneers = (bool) settings.Credentials.LobbyPioneers,
				BasicAuthUsername = (string) settings.Credentials.BasicAuth.Username,
				BasicAuthPassword = (string) settings.Credentials.BasicAuth.Password
			};
		}

		private async Task InitializeOgame() {
			string host = (string) settings.General.Host ?? "localhost";
			string port = (string) settings.General.Port ?? "8080";
			if (!_ogameService.IsPortAvailable(host, int.Parse(port))) {
				throw new Exception("Port " + port + " is not available");
			}

			string captchaKey = (string) settings.General.CaptchaAPIKey ?? "";
			ProxySettings proxy = new();
			string cookiesPath = "cookies" + (string) settings.Credentials.Email + ".txt";

			if ((bool) settings.General.Proxy.Enabled && (string) settings.General.Proxy.Address != "") {
				log(LogLevel.Information, LogSender.Tbot, "Initializing proxy");
				string proxyType = ((string) settings.General.Proxy.Type).ToLower();
				string proxyAddress = (string) settings.General.Proxy.Address;
				if (proxyType == "https") {
					proxyType = "http";
				}
				if (proxyType == "http" && proxyAddress.Contains(':') && !proxyAddress.StartsWith("http://")) {
					proxyAddress = "http://" + proxyAddress;
				}
				if (proxyType == "socks5" || proxyType == "http") {
					proxy.Enabled = (bool) settings.General.Proxy.Enabled;
					proxy.Address = proxyAddress;
					proxy.Type = proxyType;
					proxy.Username = (string) settings.General.Proxy.Username ?? "";
					proxy.Password = (string) settings.General.Proxy.Password ?? "";
				} else {
					log(LogLevel.Warning, LogSender.Tbot, "Unable to initialize proxy: unsupported proxy type");
					log(LogLevel.Warning, LogSender.Tbot, "Press enter to continue");
				}
			}

			if (SettingsService.IsSettingSet(settings.General, "CookiesPath") && (string) settings.General.CookiesPath != "") {
				// Cookies are defined relative to the settings file
				cookiesPath = Path.Combine(Path.GetDirectoryName(settingsPath), (string) settings.General.CookiesPath);
			}
			_ogameService.Initialize(GetCredentialsFromSettings(), proxy, (string) host, int.Parse(port), (string) captchaKey, cookiesPath);
			await _ogameService.SetUserAgent((string) settings.General.UserAgent);
		}

		private async Task ResolveCaptcha() {

			var captchaChallenge = await _ogameService.GetCaptchaChallenge();
			if (captchaChallenge.Id == "") {
				log(LogLevel.Warning, LogSender.Tbot, "No captcha found. Unable to login.");
				log(LogLevel.Warning, LogSender.Tbot, "Please check your credentials, language and universe name.");
				log(LogLevel.Warning, LogSender.Tbot, "If your credentials are correct try refreshing your IP address.");
				log(LogLevel.Warning, LogSender.Tbot, "If you are using a proxy, a VPN or hosting TBot on a VPS, be warned that Ogame blocks datacenters' IPs. You probably need a residential proxy.");
			} else {
				log(LogLevel.Information, LogSender.Tbot, "Trying to solve captcha...");
				int answer = 0;
				if (captchaChallenge.Icons != "" && captchaChallenge.Question != "" && captchaChallenge.Icons != null && captchaChallenge.Question != null) {
					answer = OgameCaptchaSolver.GetCapcthaSolution(captchaChallenge.Icons, captchaChallenge.Question);
				}
				await _ogameService.SolveCaptcha(captchaChallenge.Id, answer);
				await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
				await _ogameService.Login();
			}
		}

		private async Task InitUserData() {
			userData.serverInfo = await UpdateServerInfo();
			userData.serverData = await UpdateServerData();
			userData.userInfo = await UpdateUserInfo();
			userData.staff = await UpdateStaff();

			var serverTime = await GetDateTime();

			log(LogLevel.Information, LogSender.Tbot, $"Server time: {serverTime.ToString()}");
			log(LogLevel.Information, LogSender.Tbot, $"Player name: {userData.userInfo.PlayerName}");
			log(LogLevel.Information, LogSender.Tbot, $"Player class: {userData.userInfo.Class.ToString()}");
			log(LogLevel.Information, LogSender.Tbot, $"Player rank: {userData.userInfo.Rank}");
			log(LogLevel.Information, LogSender.Tbot, $"Player points: {userData.userInfo.Points}");
			log(LogLevel.Information, LogSender.Tbot, $"Player honour points: {userData.userInfo.HonourPoints}");
		}

		private void InitializeTimers() {
			timers = new Dictionary<string, Timer>();

			xaSem[Feature.Defender] = new SemaphoreSlim(1, 1);
			xaSem[Feature.Brain] = new SemaphoreSlim(1, 1);
			xaSem[Feature.BrainAutobuildCargo] = new SemaphoreSlim(1, 1);
			xaSem[Feature.BrainAutoRepatriate] = new SemaphoreSlim(1, 1);
			xaSem[Feature.BrainAutoMine] = new SemaphoreSlim(1, 1);
			xaSem[Feature.BrainLifeformAutoMine] = new SemaphoreSlim(1, 1);
			xaSem[Feature.BrainOfferOfTheDay] = new SemaphoreSlim(1, 1);
			xaSem[Feature.AutoFarm] = new SemaphoreSlim(1, 1);
			xaSem[Feature.Expeditions] = new SemaphoreSlim(1, 1);
			xaSem[Feature.Harvest] = new SemaphoreSlim(1, 1);
			xaSem[Feature.Colonize] = new SemaphoreSlim(1, 1);
			xaSem[Feature.FleetScheduler] = new SemaphoreSlim(1, 1);
			xaSem[Feature.SleepMode] = new SemaphoreSlim(1, 1);
		}

		public async Task<bool> Init(string settingPath,
			string alias,
			ITelegramMessenger telegramHandler) {

			settingsPath = settingPath;
			instanceAlias = alias;
			settings = SettingsService.GetSettings(settingPath);

			telegramMessenger = telegramHandler;
			try {
				await InitializeOgame();
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Tbot, $"Unable to start ogamed: {e.Message}");
				throw;
			}

			await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));

			try {
				await _ogameService.Login();
			} catch (OgamedException oe) {
				log(LogLevel.Warning, LogSender.Tbot, $"Unable to login (\"{oe.Message}\"). Checking captcha...");
				await ResolveCaptcha();
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Tbot, $"Unable to login. (\"{e.Message}\")");
				throw;
			}

			await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));

			log(LogLevel.Information, LogSender.Tbot, "Logged in!");

			await InitUserData();

			var isVacationMode = await _ogameService.IsVacationMode();

			if (isVacationMode) {
				log(LogLevel.Warning, LogSender.Tbot, "Account in vacation mode");
				loggedIn = false;
				await _ogameService.Logout();
				return false;
			}

			if (telegramMessenger != null) {
				await telegramMessenger.AddTbotInstance(this);
			}
			userData.lastDOIR = 0;
			userData.nextDOIR = 0;

			InitializeTimers();

			features = new();
			InitializeFeatures(Features.AllFeatures);

			log(LogLevel.Information, LogSender.Tbot, "Initializing data...");
			userData.celestials = await GetPlanets();
			userData.researches = await UpdateResearches();
			userData.scheduledFleets = new();
			userData.farmTargets = new();

			if (userData.celestials.Count == 1) {
				await EditSettings(userData.celestials.First());
				settings = SettingsService.GetSettings(settingsPath);
			}

			log(LogLevel.Information, LogSender.Tbot, "Initializing features...");
			InitializeSleepMode();

			// Up and running. Lets initialize notification for settings file
			settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, settingsPath);


			return true;
		}

		public async ValueTask DisposeAsync() {
			log(LogLevel.Information, LogSender.Tbot, "Deinitializing instance...");

			settingsWatcher.deinitWatch();

			if (telegramMessenger != null) {
				log(LogLevel.Information, LogSender.Tbot, "Removing instance from telegram...");
				await telegramMessenger.RemoveTBotInstance(this);
			}

			foreach (var sem in xaSem) {
				log(LogLevel.Information, LogSender.Tbot, $"Deinitializing feature {sem.Key.ToString()}");
				await sem.Value.WaitAsync();
			}

			log(LogLevel.Information, LogSender.Tbot, "Deinitializing timers...");
			foreach (KeyValuePair<string, Timer> entry in timers) {
				log(LogLevel.Information, LogSender.Tbot, $"Disposing timer \"{entry.Key}\"");
				entry.Value.Dispose();
			}
			timers.Clear();

			log(LogLevel.Information, LogSender.Tbot, "Deinitializing ogamed...");
			if (_ogameService != null) {
				if (loggedIn) {
					loggedIn = false;
					log(LogLevel.Information, LogSender.Tbot, "Logging out");
					await _ogameService.Logout();
				}

				log(LogLevel.Information, LogSender.Tbot, "Killing ogamed instance");
				_ogameService.KillOgamedExecutable();
			}

			log(LogLevel.Information, LogSender.Tbot, "Deinitialization completed");
		}

		public bool Equals(TBotMain other) {
			if (other == null)
				return false;
			return object.ReferenceEquals(this, other);
		}

		private void log(LogLevel logLevel, LogSender sender, string format) {
			if (loggedIn && (userData.userInfo != null) && (userData.serverData != null))
				_logger.WriteLog(logLevel, sender, $"[{userData.userInfo.PlayerName}@{userData.serverData.Name}] {format}");
			else if (instanceAlias != "MAIN")
				_logger.WriteLog(logLevel, sender, $"[{instanceAlias}] {format}");
			else
				_logger.WriteLog(logLevel, sender, format);
		}

		private bool HandleStartStopFeatures(Feature feature, bool currentValue) {
			if (userData.isSleeping && (bool) settings.SleepMode.Active)
				switch (feature) {
					case Feature.Defender:
						if (currentValue)
							StopDefender();
						return false;
					case Feature.Brain:
						return false;
					case Feature.BrainAutobuildCargo:
						if (currentValue)
							StopBrainAutoCargo();
						return false;
					case Feature.BrainAutoRepatriate:
						if (currentValue)
							StopBrainRepatriate();
						return false;
					case Feature.BrainAutoMine:
						if (currentValue)
							StopBrainAutoMine();
						return false;
					case Feature.BrainLifeformAutoMine:
						if (currentValue)
							StopBrainLifeformAutoMine();
						return false;
					case Feature.BrainLifeformAutoResearch:
						if (currentValue)
							StopBrainLifeformAutoResearch();
						return false;
					case Feature.BrainOfferOfTheDay:
						if (currentValue)
							StopBrainOfferOfTheDay();
						return false;
					case Feature.BrainAutoResearch:
						if (currentValue)
							StopBrainAutoResearch();
						return false;
					case Feature.AutoFarm:
						if (currentValue)
							StopAutoFarm();
						return false;
					case Feature.Expeditions:
						if (currentValue)
							StopExpeditions();
						return false;
					case Feature.Harvest:
						if (currentValue)
							StopHarvest();
						return false;
					case Feature.Colonize:
						if (currentValue)
							StopColonize();
						return false;
					case Feature.FleetScheduler:
						if (currentValue)
							StopFleetScheduler();
						return false;
					case Feature.SleepMode:
						if (!currentValue)
							InitializeSleepMode();
						return true;

					default:
						return false;
				}

			switch (feature) {
				case Feature.Defender:
					if ((bool) settings.Defender.Active) {
						InitializeDefender();
						return true;
					} else {
						if (currentValue)
							StopDefender();
						return false;
					}
				case Feature.Brain:
					if ((bool) settings.Brain.Active)
						return true;
					else
						return false;
				case Feature.BrainAutobuildCargo:
					if ((bool) settings.Brain.Active && (bool) settings.Brain.AutoCargo.Active) {
						InitializeBrainAutoCargo();
						return true;
					} else {
						if (currentValue)
							StopBrainAutoCargo();
						return false;
					}
				case Feature.BrainAutoRepatriate:
					if ((bool) settings.Brain.Active && (bool) settings.Brain.AutoRepatriate.Active) {
						InitializeBrainRepatriate();
						return true;
					} else {
						if (currentValue)
							StopBrainRepatriate();
						return false;
					}
				case Feature.BrainAutoMine:
					if ((bool) settings.Brain.Active && (bool) settings.Brain.AutoMine.Active) {
						InitializeBrainAutoMine();
						return true;
					} else {
						if (currentValue)
							StopBrainAutoMine();
						return false;
					}
				case Feature.BrainLifeformAutoMine:
					if ((bool) settings.Brain.Active && (bool) settings.Brain.LifeformAutoMine.Active) {
						InitializeBrainLifeformAutoMine();
						return true;
					} else {
						if (currentValue)
							StopBrainLifeformAutoMine();
						return false;
					}
				case Feature.BrainLifeformAutoResearch:
					if ((bool) settings.Brain.Active && (bool) settings.Brain.LifeformAutoResearch.Active) {
						InitializeBrainLifeformAutoResearch();
						return true;
					} else {
						if (currentValue)
							StopBrainLifeformAutoResearch();
						return false;
					}
				case Feature.BrainOfferOfTheDay:
					if ((bool) settings.Brain.Active && (bool) settings.Brain.BuyOfferOfTheDay.Active) {
						InitializeBrainOfferOfTheDay();
						return true;
					} else {
						if (currentValue)
							StopBrainOfferOfTheDay();
						return false;
					}
				case Feature.BrainAutoResearch:
					if ((bool) settings.Brain.Active && (bool) settings.Brain.AutoResearch.Active) {
						InitializeBrainAutoResearch();
						return true;
					} else {
						if (currentValue)
							StopBrainAutoResearch();
						return false;
					}
				case Feature.AutoFarm:
					if ((bool) settings.AutoFarm.Active) {
						InitializeAutoFarm();
						return true;
					} else {
						if (currentValue)
							StopAutoFarm();
						return false;
					}
				case Feature.Expeditions:
					if ((bool) settings.Expeditions.Active) {
						InitializeExpeditions();
						return true;
					} else {
						if (currentValue)
							StopExpeditions();
						return false;
					}
				case Feature.Harvest:
					if ((bool) settings.AutoHarvest.Active) {
						InitializeHarvest();
						return true;
					} else {
						if (currentValue)
							StopHarvest();
						return false;
					}
				case Feature.Colonize:
					if ((bool) settings.AutoColonize.Active) {
						InitializeColonize();
						return true;
					} else {
						if (currentValue)
							StopHarvest();
						return false;
					}
				case Feature.FleetScheduler:
					if (!currentValue) {
						InitializeFleetScheduler();
						return true;
					} else {
						StopFleetScheduler();
						return false;
					}
				case Feature.SleepMode:
					if ((bool) settings.SleepMode.Active) {
						InitializeSleepMode();
						return true;
					} else {
						if (currentValue)
							StopSleepMode();
						return false;
					}
				default:
					return false;
			}
		}

		private void InitializeFeatures(List<Feature> featuresToInitialize = null) {
			if (featuresToInitialize == null) {
				featuresToInitialize = new List<Feature>() {
					Feature.Defender,
					Feature.Brain,
					Feature.BrainAutobuildCargo,
					Feature.BrainAutoRepatriate,
					Feature.BrainAutoMine,
					Feature.BrainLifeformAutoMine,
					Feature.BrainLifeformAutoResearch,
					Feature.BrainOfferOfTheDay,
					Feature.BrainAutoResearch,
					Feature.AutoFarm,
					Feature.Expeditions,
					Feature.Harvest,
					Feature.Colonize,
				};
			}
			foreach (Feature feat in featuresToInitialize) {
				features.AddOrUpdate(feat, false, HandleStartStopFeatures);
			}
		}

		public async Task<bool> EditSettings(Celestial celestial = null, Feature feature = Feature.Null, string recall = "", int cargo = 0) {
			await Task.Delay(500);
			var file = System.IO.File.ReadAllText(Path.GetFullPath(settingsPath));
			var jsonObj = new JObject();
			jsonObj = Newtonsoft.Json.JsonConvert.DeserializeObject<dynamic>(file);

			if (recall != "") {
				jsonObj["SleepMode"]["AutoFleetSave"]["Recall"] = Convert.ToBoolean(recall);
			}

			if (cargo > 0) {
				jsonObj["Expeditions"]["MinPrimaryToSend"] = (int) cargo;
			}

			if (celestial != null) {
				string type = "";
				if (celestial.Coordinate.Type == Celestials.Moon)
					type = "Moon" ?? "Planet";
				else
					type = "Planet";

				if (feature == Feature.BrainAutoMine || feature == Feature.Null) {
					jsonObj["Brain"]["AutoMine"]["Transports"]["Origin"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["Brain"]["AutoMine"]["Transports"]["Origin"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["Brain"]["AutoMine"]["Transports"]["Origin"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["Brain"]["AutoMine"]["Transports"]["Origin"]["Type"] = type;
				}

				if (feature == Feature.BrainAutoResearch || feature == Feature.Null) {
					jsonObj["Brain"]["AutoResearch"]["Target"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["Brain"]["AutoResearch"]["Target"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["Brain"]["AutoResearch"]["Target"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["Brain"]["AutoResearch"]["Target"]["Type"] = "Planet";

					jsonObj["Brain"]["AutoResearch"]["Transports"]["Origin"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["Brain"]["AutoResearch"]["Transports"]["Origin"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["Brain"]["AutoResearch"]["Transports"]["Origin"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["Brain"]["AutoResearch"]["Transports"]["Origin"]["Type"] = type;
				}

				if (feature == Feature.BrainAutoRepatriate || feature == Feature.Null) {
					jsonObj["Brain"]["AutoRepatriate"]["Target"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["Brain"]["AutoRepatriate"]["Target"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["Brain"]["AutoRepatriate"]["Target"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["Brain"]["AutoRepatriate"]["Target"]["Type"] = type;
				}

				if (feature == Feature.Expeditions || feature == Feature.Null) {
					jsonObj["Expeditions"]["Origin"][0]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["Expeditions"]["Origin"][0]["System"] = (int) celestial.Coordinate.System;
					jsonObj["Expeditions"]["Origin"][0]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["Expeditions"]["Origin"][0]["Type"] = type;
				}

				if (feature == Feature.Colonize || feature == Feature.Null) {
					jsonObj["AutoColonize"]["Origin"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["AutoColonize"]["Origin"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["AutoColonize"]["Origin"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["AutoColonize"]["Origin"]["Type"] = "Planet";
				}
			}

			string output = Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj, Newtonsoft.Json.Formatting.Indented);
			System.IO.File.WriteAllText(Path.GetFullPath(settingsPath), output);

			return true;
		}

		public async Task WaitFeature() {
			await Task.WhenAll(
				xaSem[Feature.Brain].WaitAsync(),
				xaSem[Feature.Expeditions].WaitAsync(),
				xaSem[Feature.Harvest].WaitAsync(),
				xaSem[Feature.Colonize].WaitAsync(),
				xaSem[Feature.AutoFarm].WaitAsync());

		}

		public void releaseFeature() {
			xaSem[Feature.Brain].Release();
			xaSem[Feature.Expeditions].Release();
			xaSem[Feature.Harvest].Release();
			xaSem[Feature.Colonize].Release();
			xaSem[Feature.AutoFarm].Release();
		}

		private async void OnSettingsChanged() {

			log(LogLevel.Information, LogSender.Tbot, "Settings file change detected! Waiting workers to complete ongoing activities...");

			List<Feature> featuresToHandle = new List<Feature>{
				Feature.Defender,
				Feature.Brain,
				Feature.Expeditions,
				Feature.Harvest,
				Feature.Colonize,
				Feature.AutoFarm,
				Feature.SleepMode
			};

			// Wait on feature to be locked
			foreach (var feature in featuresToHandle) {
				log(LogLevel.Information, LogSender.Tbot, $"Waiting on feature {feature.ToString()}...");
				await xaSem[feature].WaitAsync();
				log(LogLevel.Information, LogSender.Tbot, $"Feature {feature.ToString()} locked for settings reload!");
			}

			log(LogLevel.Information, LogSender.Tbot, "Reloading Settings file");
			settings = SettingsService.GetSettings(settingsPath);

			// Release features lock!
			foreach (var feature in featuresToHandle) {
				log(LogLevel.Information, LogSender.Tbot, $"Unlocking feature {feature.ToString()}...");
				xaSem[feature].Release();
				log(LogLevel.Information, LogSender.Tbot, $"Feature {feature.ToString()} unlocked!");
			}

			InitializeSleepMode();
		}

		public async Task<DateTime> GetDateTime() {
			try {
				DateTime dateTime = await _ogameService.GetServerTime();
				if (dateTime.Kind == DateTimeKind.Utc)
					return dateTime.ToLocalTime();
				else
					return dateTime;
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"GetDateTime() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				var fallback = DateTime.Now;
				if (fallback.Kind == DateTimeKind.Utc)
					return fallback.ToLocalTime();
				else
					return fallback;
			}
		}

		private async Task<Slots> UpdateSlots() {
			try {
				return await _ogameService.GetSlots();
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateSlots() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private async Task<List<Fleet>> UpdateFleets() {
			try {
				return await _ogameService.GetFleets();
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateFleets() Exception: {e.Message}");
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateFleets() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private async Task<List<GalaxyInfo>> UpdateGalaxyInfos() {
			try {
				List<GalaxyInfo> galaxyInfos = new();
				Planet newPlanet = new();
				List<Celestial> newCelestials = userData.celestials.ToList();
				foreach (Planet planet in userData.celestials.Where(p => p is Planet)) {
					newPlanet = planet;
					var gi = await _ogameService.GetGalaxyInfo(planet.Coordinate);
					if (gi.Planets.Any(p => p != null && p.ID == planet.ID)) {
						newPlanet.Debris = gi.Planets.Single(p => p != null && p.ID == planet.ID).Debris;
						galaxyInfos.Add(gi);
					}

					if (userData.celestials.Any(p => p.HasCoords(newPlanet.Coordinate))) {
						Planet oldPlanet = userData.celestials.Unique().SingleOrDefault(p => p.HasCoords(newPlanet.Coordinate)) as Planet;
						newCelestials.Remove(oldPlanet);
						newCelestials.Add(newPlanet);
					}
				}
				userData.celestials = newCelestials;
				return galaxyInfos;
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateGalaxyInfos() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private async Task<ServerData> UpdateServerData() {
			try {
				return await _ogameService.GetServerData();
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateServerData() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private async Task<Server> UpdateServerInfo() {
			try {
				return await _ogameService.GetServerInfo();
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateServerInfo() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private async Task<UserInfo> UpdateUserInfo() {
			try {
				UserInfo user = await _ogameService.GetUserInfo();
				user.Class = await _ogameService.GetUserClass();
				return user;
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateUserInfo() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
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
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateCelestials() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return userData.celestials ?? new();
			}
		}

		private async Task<Researches> UpdateResearches() {
			try {
				return await _ogameService.GetResearches();
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateResearches() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private async Task<Staff> UpdateStaff() {
			try {
				return await _ogameService.GetStaff();
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdateStaff() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private async Task<List<Celestial>> GetPlanets() {
			List<Celestial> localPlanets = userData.celestials ?? new();
			try {
				List<Celestial> ogamedPlanets = await _ogameService.GetCelestials();
				if (localPlanets.Count() == 0 || ogamedPlanets.Count() != userData.celestials.Count) {
					localPlanets = ogamedPlanets.ToList();
				}
				return localPlanets;
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"GetPlanets() Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return localPlanets;
			}
		}

		private async Task<List<Celestial>> UpdatePlanets(UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogLevel.Information, LogSender.Tbot, $"Updating userData.celestials... Mode: {UpdateTypes.ToString()}");
			List<Celestial> localPlanets = await GetPlanets();
			List<Celestial> newPlanets = new();
			try {
				foreach (Celestial planet in localPlanets) {
					newPlanets.Add(await UpdatePlanet(planet, UpdateTypes));
				}
				return newPlanets;
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"UpdatePlanets({UpdateTypes.ToString()}) Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return newPlanets;
			}
		}

		private async Task<Celestial> UpdatePlanet(Celestial planet, UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogLevel.Information, LogSender.Tbot, $"Updating {planet.ToString()}. Mode: {UpdateTypes.ToString()}");
			try {
				switch (UpdateTypes) {
					case UpdateTypes.Fast:
						planet = await _ogameService.GetCelestial(planet);
						break;
					case UpdateTypes.Resources:
						planet.Resources = await _ogameService.GetResources(planet);
						break;
					case UpdateTypes.Buildings:
						planet.Buildings = await _ogameService.GetBuildings(planet);
						break;
					case UpdateTypes.LFBuildings:
						planet.LFBuildings = await _ogameService.GetLFBuildings(planet);
						planet.LFtype = planet.SetLFType();
						break;
					case UpdateTypes.LFTechs:
						planet.LFTechs = await _ogameService.GetLFTechs(planet);
						break;
					case UpdateTypes.Ships:
						planet.Ships = await _ogameService.GetShips(planet);
						break;
					case UpdateTypes.Facilities:
						planet.Facilities = await _ogameService.GetFacilities(planet);
						break;
					case UpdateTypes.Defences:
						planet.Defences = await _ogameService.GetDefences(planet);
						break;
					case UpdateTypes.Productions:
						planet.Productions = await _ogameService.GetProductions(planet);
						break;
					case UpdateTypes.Constructions:
						planet.Constructions = await _ogameService.GetConstructions(planet);
						break;
					case UpdateTypes.ResourceSettings:
						if (planet is Planet) {
							planet.ResourceSettings = await _ogameService.GetResourceSettings(planet as Planet);
						}
						break;
					case UpdateTypes.ResourcesProduction:
						if (planet is Planet) {
							planet.ResourcesProduction = await _ogameService.GetResourcesProduction(planet as Planet);
						}
						break;
					case UpdateTypes.Techs:
						var techs = await _ogameService.GetTechs(planet);
						planet.Defences = techs.defenses;
						planet.Facilities = techs.facilities;
						planet.Ships = techs.ships;
						planet.Buildings = techs.supplies;
						break;
					case UpdateTypes.Debris:
						if (planet is Moon)
							break;
						var galaxyInfo = await _ogameService.GetGalaxyInfo(planet.Coordinate);
						var thisPlanetGalaxyInfo = galaxyInfo.Planets.Single(p => p != null && p.Coordinate.IsSame(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet)));
						planet.Debris = thisPlanetGalaxyInfo.Debris;
						break;
					case UpdateTypes.Full:
					default:
						planet.Resources = await _ogameService.GetResources(planet);
						planet.Productions = await _ogameService.GetProductions(planet);
						planet.Constructions = await _ogameService.GetConstructions(planet);
						if (planet is Planet) {
							planet.ResourceSettings = await _ogameService.GetResourceSettings(planet as Planet);
							planet.ResourcesProduction = await _ogameService.GetResourcesProduction(planet as Planet);
						}
						planet.Buildings = await _ogameService.GetBuildings(planet);
						planet.Facilities = await _ogameService.GetFacilities(planet);
						planet.Ships = await _ogameService.GetShips(planet);
						planet.Defences = await _ogameService.GetDefences(planet);
						break;
				}
			} catch (Exception e) {
				log(LogLevel.Debug, LogSender.Tbot, $"Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				log(LogLevel.Warning, LogSender.Tbot, $"An error has occurred with update {UpdateTypes.ToString()}. Skipping update");
			}
			return planet;
		}

		private async Task CheckCelestials() {
			try {
				if (!userData.isSleeping) {
					var newCelestials = await UpdateCelestials();
					if (userData.celestials.Count() != newCelestials.Count) {
						userData.celestials = newCelestials.Unique().ToList();
						if (userData.celestials.Count() > newCelestials.Count) {
							log(LogLevel.Warning, LogSender.Tbot, "Less userData.celestials than last check detected!!");
						} else {
							log(LogLevel.Information, LogSender.Tbot, "More userData.celestials than last check detected");
						}
						InitializeSleepMode();
					}
				}
			} catch {
				userData.celestials = userData.celestials.Unique().ToList();
			}
		}

		public void InitializeDefender() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing defender...");
			StopDefender(false);
			timers.Add("DefenderTimer", new Timer(Defender, null, RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		public void StopDefender(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping defender...");
			if (timers.TryGetValue("DefenderTimer", out Timer value))
				value.Dispose();
			timers.Remove("DefenderTimer");
		}

		private void InitializeBrainAutoCargo() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing autocargo...");
			StopBrainAutoCargo(false);
			timers.Add("CapacityTimer", new Timer(AutoBuildCargo, null, RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo), Timeout.Infinite));
		}

		private void StopBrainAutoCargo(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping autocargo...");
			if (timers.TryGetValue("CapacityTimer", out Timer value))
				value.Dispose();
			timers.Remove("CapacityTimer");
		}

		public void InitializeBrainRepatriate() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing repatriate...");
			StopBrainRepatriate(false);
			if (!timers.TryGetValue("RepatriateTimer", out Timer value))
				timers.Add("RepatriateTimer", new Timer(AutoRepatriate, null, RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		public void StopBrainRepatriate(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping repatriate...");
			if (timers.TryGetValue("RepatriateTimer", out Timer value))
				value.Dispose();
			timers.Remove("RepatriateTimer");
		}

		public void InitializeBrainAutoMine() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing automine...");
			StopBrainAutoMine(false);
			timers.Add("AutoMineTimer", new Timer(AutoMine, null, RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		public void StopBrainAutoMine(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping automine...");
			if (timers.TryGetValue("AutoMineTimer", out Timer value))
				value.Dispose();
			timers.Remove("AutoMineTimer");
			foreach (var celestial in userData.celestials) {
				if (timers.TryGetValue($"AutoMineTimer-{celestial.ID.ToString()}", out value))
					value.Dispose();
				timers.Remove($"AutoMineTimer-{celestial.ID.ToString()}");
			}
		}

		public void InitializeBrainLifeformAutoMine() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing Lifeform autoMine...");
			StopBrainLifeformAutoMine(false);
			timers.Add("LifeformAutoMineTimer", new Timer(LifeformAutoMine, null, RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		public void StopBrainLifeformAutoMine(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping Lifeform autoMine...");
			if (timers.TryGetValue("LifeformAutoMineTimer", out Timer value))
				value.Dispose();
			timers.Remove("LifeformAutoMineTimer");
			foreach (var celestial in userData.celestials) {
				if (timers.TryGetValue($"LifeformAutoMineTimer-{celestial.ID.ToString()}", out value))
					value.Dispose();
				timers.Remove($"LifeformAutoMineTimer-{celestial.ID.ToString()}");
			}
		}

		public void InitializeBrainLifeformAutoResearch() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing Lifeform autoResearch...");
			StopBrainLifeformAutoResearch(false);
			timers.Add("LifeformAutoResearchTimer", new Timer(LifeformAutoResearch, null, RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		public void StopBrainLifeformAutoResearch(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping Lifeform autoResearch...");
			if (timers.TryGetValue("LifeformAutoResearchTimer", out Timer value))
				value.Dispose();
			timers.Remove("LifeformAutoResearchTimer");
			foreach (var celestial in userData.celestials) {
				if (timers.TryGetValue($"LifeformAutoResearchTimer-{celestial.ID.ToString()}", out value))
					value.Dispose();
				timers.Remove($"LifeformAutoResearchTimer-{celestial.ID.ToString()}");
			}
		}

		private void InitializeBrainOfferOfTheDay() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing offer of the day...");
			StopBrainOfferOfTheDay(false);
			timers.Add("OfferOfTheDayTimer", new Timer(BuyOfferOfTheDay, null, RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private void StopBrainOfferOfTheDay(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping offer of the day...");
			if (timers.TryGetValue("OfferOfTheDayTimer", out Timer value))
				value.Dispose();
			timers.Remove("OfferOfTheDayTimer");
		}

		public void InitializeBrainAutoResearch() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing autoresearch...");
			StopBrainAutoResearch(false);
			timers.Add("AutoResearchTimer", new Timer(AutoResearch, null, RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		public void StopBrainAutoResearch(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping autoresearch...");
			if (timers.TryGetValue("AutoResearchTimer", out Timer value))
				value.Dispose();
			timers.Remove("AutoResearchTimer");
		}

		public void InitializeAutoFarm() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing autofarm...");
			StopAutoFarm(false);
			timers.Add("AutoFarmTimer", new Timer(AutoFarm, null, RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo), Timeout.Infinite));
		}

		public void StopAutoFarm(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping autofarm...");
			if (timers.TryGetValue("AutoFarmTimer", out Timer value))
				value.Dispose();
			timers.Remove("AutoFarmTimer");
		}

		public void InitializeExpeditions() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing expeditions...");
			StopExpeditions(false);
			timers.Add("ExpeditionsTimer", new Timer(HandleExpeditions, null, RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		public void StopExpeditions(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping expeditions...");
			if (timers.TryGetValue("ExpeditionsTimer", out Timer value))
				value.Dispose();
			timers.Remove("ExpeditionsTimer");
		}

		private void InitializeHarvest() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing harvest...");
			StopHarvest(false);
			timers.Add("HarvestTimer", new Timer(HandleHarvest, null, RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private void StopHarvest(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping harvest...");
			if (timers.TryGetValue("HarvestTimer", out Timer value))
				value.Dispose();
			timers.Remove("HarvestTimer");
		}

		private void InitializeColonize() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing colonize...");
			StopColonize(false);
			timers.Add("ColonizeTimer", new Timer(HandleColonize, null, RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private void StopColonize(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping colonize...");
			if (timers.TryGetValue("ColonizeTimer", out Timer value))
				value.Dispose();
			timers.Remove("ColonizeTimer");
		}

		private void InitializeSleepMode() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing sleep mode...");
			StopSleepMode(false);
			timers.Add("SleepModeTimer", new Timer(HandleSleepMode, null, 0, Timeout.Infinite));
		}

		private void StopSleepMode(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping sleep mode...");
			if (timers.TryGetValue("SleepModeTimer", out Timer value))
				value.Dispose();
			timers.Remove("SleepModeTimer");
		}

		private void InitializeFleetScheduler() {
			log(LogLevel.Information, LogSender.Tbot, "Initializing fleet scheduler...");
			userData.scheduledFleets = new();
			StopFleetScheduler(false);
			timers.Add("FleetSchedulerTimer", new Timer(HandleScheduledFleet, null, Timeout.Infinite, Timeout.Infinite));
		}

		private void StopFleetScheduler(bool echo = true) {
			if (echo)
				log(LogLevel.Information, LogSender.Tbot, "Stopping fleet scheduler...");
			if (timers.TryGetValue("FleetSchedulerTimer", out Timer value))
				value.Dispose();
			timers.Remove("FleetSchedulerTimer");
		}

		public void RemoveTelegramMessenger() {
			if (telegramMessenger != null) {
				log(LogLevel.Information, LogSender.Tbot, "Removing TelegramMessenger from current instance...");
				telegramMessenger = null;
			}
		}

		public async Task SendTelegramMessage(string fmt) {
			// We may consider any filter logic to be put here IE telegram notification disabled from settings
			if (telegramMessenger != null) {
				string finalStr = $"<code>[{userData.userInfo.PlayerName}@{userData.serverData.Name}]</code>\n" +
					fmt;
				await telegramMessenger.SendMessage(finalStr);
			}
		}

		public async Task TelegramGetFleets() {
			userData.fleets = (await UpdateFleets()).Where(f => !f.ReturnFlight).ToList();
			string message = "";
			foreach (Fleet fleet in userData.fleets) {
				message += $"{fleet.ID} -> Origin: {fleet.Origin.ToString()}, Dest: {fleet.Destination.ToString()}, Type: {fleet.Mission}, ArrivalTime: {fleet.ArrivalTime.ToString()}\n\n";
			}
			await SendTelegramMessage(message);

		}

		public async Task TelegramBuild(Buildables buildable, decimal num = 0) {
			string results = "";
			decimal MaxNumToBuild = 0;
			Resources cost = _helpersService.CalcPrice(buildable, 1);
			foreach (Celestial celestial in userData.celestials.Where(c => c is Planet).ToList()) {
				List<decimal> MaxNumber = new();
				await UpdatePlanet(celestial, UpdateTypes.Constructions);
				if ((int) celestial.Constructions.BuildingID == (int) Buildables.NaniteFactory || (int) celestial.Constructions.BuildingID == (int) Buildables.Shipyard) {
					results += $"{celestial.Coordinate.ToString()}: Shipyard or Nanite in construction\n";
					continue;
				}
				await UpdatePlanet(celestial, UpdateTypes.Resources);
				Resources resources = celestial.Resources;
				if (num == 0) {
					if (cost.Metal > 0)
						MaxNumber.Add(Math.Floor((decimal) resources.Metal / (decimal) cost.Metal));
					if (cost.Crystal > 0)
						MaxNumber.Add(Math.Floor((decimal) resources.Crystal / (decimal) cost.Crystal));
					if (cost.Deuterium > 0)
						MaxNumber.Add(Math.Floor((decimal) resources.Deuterium / (decimal) cost.Deuterium));

					MaxNumToBuild = MaxNumber.Min();
				} else {
					MaxNumToBuild = num;
				}

				if (MaxNumToBuild > 0) {
					if (buildable == Buildables.RocketLauncher || buildable == Buildables.LightLaser || buildable == Buildables.HeavyLaser || buildable == Buildables.GaussCannon || buildable == Buildables.IonCannon || buildable == Buildables.PlasmaTurret || buildable == Buildables.InterplanetaryMissiles || buildable == Buildables.AntiBallisticMissiles) {
						await _ogameService.BuildDefences(celestial, buildable, (long) MaxNumToBuild);
					} else {
						await _ogameService.BuildShips(celestial, buildable, (long) MaxNumToBuild);
						results += $"{celestial.Coordinate.ToString()}: {MaxNumToBuild} started\n";
					}
				} else {
					results += $"{celestial.Coordinate.ToString()}: Not enough resources\n";
				}
			}
			if (results != "") {
				await SendTelegramMessage(results);
			} else {
				await SendTelegramMessage("Could not start build anywhere.");
			}
			return;
		}

		public async Task TelegramGetCurrentAuction() {
			Auction auction;
			try {
				auction = await _ogameService.GetCurrentAuction();
				string outStr = "";
				if (auction.TotalResourcesOffered > 0) {
					outStr += "Offerings: \n";
					// Find from which planet we have put resources
					foreach (var item in auction.Resources) {
						string planetIdString = item.Key;
						AuctionResourcesValue value = item.Value;

						if (value.output.TotalResources > 0) {
							outStr += $"\t\"{value.Name}\" ID:{planetIdString} {value.output.ToString()} \n";
						}
					}
				}
				outStr += auction.ToString();
				await SendTelegramMessage(outStr);
			} catch (Exception e) {
				await SendTelegramMessage($"Error on GetCurrentAuction {e.Message}");
				return;
			}
		}

		public async Task TelegramSubscribeToNextAuction() {
			var auction = await _ogameService.GetCurrentAuction();
			if (auction.HasFinished) {
				// Dispose existing
				if (timers.TryGetValue("TelegramAuctionSubscription", out Timer value))
					value.Dispose();
				timers.Remove("TelegramAuctionSubscription");
				// Evaluate a reasonable time. Assuming maximum auction time can range from 30 to 45m30s
				// Arm from minimum 5 mins (inside next auction) or exact time + 5 minute
				long timerTimeMs;
				long minTimerSec = 5 * 60;
				if (auction.Endtime < minTimerSec) {
					timerTimeMs = minTimerSec * 1000;
				} else {
					timerTimeMs = (auction.Endtime * 1000) + (minTimerSec * 1000);
				}

				// Arm a new timer!
				string timeStr = ((timerTimeMs / 1000 / 60) > 0) ?
					$"{timerTimeMs / 1000 / 60}m{timerTimeMs % 1000}s" :
					$"{timerTimeMs / 1000}s";
				await SendTelegramMessage($"Next auction notified in {timeStr}");
				timers.Add("TelegramAuctionSubscription", new Timer(async _ => {
					var nextAuction = await _ogameService.GetCurrentAuction();
					string auctionStr =
						$"Auction in progress! \n" +
						$"{nextAuction.ToString()}";
					await SendTelegramMessage(auctionStr);
				}, null, timerTimeMs, Timeout.Infinite));
			} else {
				await SendTelegramMessage("An auction is in progress! Hurry and use <code>/getcurrentauction</code>");
			}
		}

		public async Task TelegramBidAuctionMinimum() {
			var auction = await _ogameService.GetCurrentAuction();
			if (auction.HasFinished) {
				await SendTelegramMessage("No auction in progress!");
			} else {
				// check if auction is currently ours
				if (userData.userInfo.PlayerID == auction.HighestBidderUserID) {
					await SendTelegramMessage("Auction is already ours!\n" +
						$"Item: \"{auction.CurrentItem}\"\n" +
						$"Ending in: {auction.GetTimeString()}");
				} else {
					long minBidRequired = auction.MinimumBid - auction.AlreadyBid;

					Celestial celestial = null;

					log(LogLevel.Information, LogSender.Tbot, "Finding input for Minimum Bid into Auction...");
					foreach (var item in auction.Resources) {
						var planetIdStr = item.Key;
						var planetResources = item.Value;

						long auctionPoints = (long) Math.Round(
								(planetResources.input.Metal / auction.ResourceMultiplier.Metal) +
								(planetResources.input.Crystal / auction.ResourceMultiplier.Crystal) +
								(planetResources.input.Deuterium / auction.ResourceMultiplier.Deuterium)
						);
						if (auctionPoints > minBidRequired) {
							await SendTelegramMessage($"Found celestial! \"{planetResources.Name}\". ID: {planetIdStr}");
							celestial = new Celestial();
							celestial.ID = Int32.Parse(planetIdStr);
							celestial.Name = planetResources.Name;
							celestial.Resources = new Resources();
							celestial.Resources.Metal = planetResources.input.Metal;
							celestial.Resources.Crystal = planetResources.input.Crystal;
							celestial.Resources.Deuterium = planetResources.input.Deuterium;

							log(LogLevel.Information, LogSender.Tbot, $"Planet \"{planetResources.Name}\" points {auctionPoints} > {minBidRequired}. Proceeding! :)");
							break;
						} else
							log(LogLevel.Information, LogSender.Tbot, $"Planet \"{planetResources.Name}\" points {auctionPoints} < {minBidRequired}. Discarding");
					}

					if (celestial == null) {
						await SendTelegramMessage(
							$"No celestial with minimum required resources found! \n" +
							$"Resource Multiplier: M:{auction.ResourceMultiplier.Metal} C:{auction.ResourceMultiplier.Crystal} D:{auction.ResourceMultiplier.Deuterium}.\n" +
							$"Doing nothing...");
					} else {
						Resources res = new Resources();

						// Prioritize Metal then crystal then deuterium
						int resIndex = 0;
						while ((resIndex < 3) && (minBidRequired > 0)) {

							if (resIndex == 0) {
								long metalNeeded = (long) Math.Round(minBidRequired / auction.ResourceMultiplier.Metal);

								if (celestial.Resources.Metal > metalNeeded) {
									res.Metal = metalNeeded;
								} else {
									res.Metal = celestial.Resources.Metal;
								}
								minBidRequired -= (long) Math.Round(res.Metal * auction.ResourceMultiplier.Metal);
							}

							if (resIndex == 1) {
								long crystalNeeded = (long) Math.Round(minBidRequired / auction.ResourceMultiplier.Crystal);

								if (celestial.Resources.Crystal > crystalNeeded) {
									res.Crystal = crystalNeeded;
								} else {
									res.Crystal = celestial.Resources.Crystal;
								}
								minBidRequired -= (long) Math.Round(res.Crystal * auction.ResourceMultiplier.Crystal);
							}

							if (resIndex == 2) {
								long deuteriumNeeded = (long) Math.Round(minBidRequired / auction.ResourceMultiplier.Deuterium);

								if (celestial.Resources.Deuterium > deuteriumNeeded) {
									res.Deuterium = deuteriumNeeded;
								} else {
									res.Deuterium = celestial.Resources.Deuterium;
								}
								minBidRequired -= (long) Math.Round(res.Deuterium * auction.ResourceMultiplier.Deuterium);
							}

							resIndex++;
						}

						if (minBidRequired > 0) {
							await SendTelegramMessage("Cannot bid. Try again");
							log(LogLevel.Information, LogSender.Tbot, $"Planet \"{celestial.Name}\" minimum bidding failed. Remaining {minBidRequired}. Doing nothing");
						} else {
							await SendTelegramMessage(
								$"Bidding Auction with M:{res.Metal} C:{res.Crystal} D:{res.Deuterium}\n" +
								$"From {celestial.Name} ID:{celestial.Name}");
							await TelegramBidAuction(celestial, res);
						}
					}
				}
			}
		}

		public async Task TelegramBidAuction(Celestial celestial, Resources res) {
			log(LogLevel.Information, LogSender.Tbot, $"Bidding auction with {celestial.Name} {res.ToString()}");
			try {
				await _ogameService.DoAuction(celestial, res);
				await SendTelegramMessage($"Auction done with Resources of Planet {celestial.Name} ID:{celestial.ID} \n" +
												$"M:{res.Metal} C:{res.Crystal} D:{res.Deuterium}");
			} catch (Exception ex) {
				await SendTelegramMessage($"BidAuction failed. \"{ex.Message}\"");
			}
		}



		public void TelegramCollect() {
			timers.Add("TelegramCollect", new Timer(AutoRepatriate, null, 3000, Timeout.Infinite));

			return;
		}

		public async Task TelegramCancelGhostSleep() {
			if (!timers.TryGetValue("GhostSleepTimer", out Timer value)) {
				await SendTelegramMessage("No GhostSleep configured or too late to cancel (fleets already sent), Do a <code>/wakeup</code>");
				return;
			}

			timers.TryGetValue("GhostSleepTimer", out Timer value2);
			value2.Dispose();
			timers.Remove("GhostSleepTimer");
			telegramUserData.Mission = Missions.None;
			await SendTelegramMessage("Ghostsleep canceled!");

			return;
		}

		public async Task TelegramJumpGate(Celestial origin, Coordinate destination, string mode) {
			if (origin.Coordinate.Type == Celestials.Planet) {
				await SendTelegramMessage($"Current Celestial is not a moon.");
				return;
			}

			Celestial moondest = userData.celestials.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) destination.Galaxy)
				.Where(c => c.Coordinate.System == (int) destination.System)
				.Where(c => c.Coordinate.Position == (int) destination.Position)
				.Where(c => c.Coordinate.Type == Celestials.Moon)
				.SingleOrDefault() ?? new() { ID = 0 };

			if (moondest.ID == 0) {
				await SendTelegramMessage($"{destination.ToString()} -> Moon not found!");
				return;
			}

			if (moondest.Coordinate.ToString().Equals(origin.Coordinate.ToString())) {
				await SendTelegramMessage($"Origin and destination are the same! did you /celestial?");
				return;
			}

			origin = await UpdatePlanet(origin, UpdateTypes.Resources);
			origin = await UpdatePlanet(origin, UpdateTypes.Ships);

			if (origin.Ships.GetMovableShips().IsEmpty()) {
				await SendTelegramMessage($"No ships on {origin.Coordinate}, did you /celestial?");
				return;
			}

			var payload = origin.Resources;
			Ships ships = origin.Ships;
			if (mode.Equals("auto")) {
				long idealSmallCargo = _helpersService.CalcShipNumberForPayload(payload, Buildables.SmallCargo, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);

				if (idealSmallCargo <= origin.Ships.GetAmount(Buildables.SmallCargo)) {
					ships.SetAmount(Buildables.SmallCargo, origin.Ships.GetAmount(Buildables.SmallCargo) - (long) idealSmallCargo);
				} else {
					long idealLargeCargo = _helpersService.CalcShipNumberForPayload(payload, Buildables.LargeCargo, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);
					if (idealLargeCargo <= origin.Ships.GetAmount(Buildables.LargeCargo)) {
						ships.SetAmount(Buildables.LargeCargo, origin.Ships.GetAmount(Buildables.LargeCargo) - (long) idealLargeCargo);
					} else {
						ships.SetAmount(Buildables.SmallCargo, 0);
						ships.SetAmount(Buildables.LargeCargo, 0);
					}
				}
			}
			try {
				await _ogameService.JumpGate(origin, moondest, ships);
				await SendTelegramMessage($"JumGate Done!");
			} catch (Exception ex) {
				await SendTelegramMessage($"JumGate Failed! Error: {ex.Message}");
			}
		}

		public async Task TelegramPhalanx(Celestial origin, Coordinate target) {
			try {
				List<Fleet> phalanxed = await _ogameService.Phalanx(origin, target);
				await SendTelegramMessage($"Phalanxed {phalanxed.Count} fleets");
				foreach (Fleet fleetPhalanxed in phalanxed) {
					string currFleetStr =
						$"Mission {fleetPhalanxed.Mission.ToString()}\n" +
						$"Origin {fleetPhalanxed.Origin.ToString()}\n" +
						$"Destination {fleetPhalanxed.Destination.ToString()}\n" +
						$"Ships: {fleetPhalanxed.Ships.ToString()}";
					await SendTelegramMessage($"Phalanxed: {currFleetStr}");

				}
			} catch (Exception ex) {
				await SendTelegramMessage($"No fleet phalanxed. Client returned {ex.Message}");
			}
		}

		public async Task TelegramDeploy(Celestial celestial, Coordinate destination, decimal speed) {
			celestial = await UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await UpdatePlanet(celestial, UpdateTypes.Ships);

			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"[Deploy] From {celestial.Coordinate.ToString()}: No ships!");
				await SendTelegramMessage($"No ships on {celestial.Coordinate}, did you /celestial?");
				return;
			}
			var payload = celestial.Resources;
			if (celestial.Resources.Deuterium == 0) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"[Deploy] From {celestial.Coordinate.ToString()}: there is no fuel!");
				await SendTelegramMessage($"Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel.");
				return;
			}

			FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(celestial.Coordinate, destination, celestial.Ships, Missions.Deploy, speed, userData.researches, userData.serverData, userData.userInfo.Class);
			int fleetId = await SendFleet(celestial, celestial.Ships, destination, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

			if (fleetId != (int) SendFleetCode.GenericError ||
				fleetId != (int) SendFleetCode.AfterSleepTime ||
				fleetId != (int) SendFleetCode.NotEnoughSlots) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleet {fleetId} deployed from {celestial.Coordinate.Type} to {destination.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				await SendTelegramMessage($"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {destination.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				return;
			}

			return;
		}

		public async Task<bool> TelegramSwitch(decimal speed, Celestial attacked = null, bool fromTelegram = false) {
			Celestial celestial;

			if (attacked == null) {
				celestial = await TelegramGetCurrentCelestial();
			} else {
				celestial = attacked; //for autofleetsave func when under attack, last option if no other destination found.
			}

			if (celestial.Coordinate.Type == Celestials.Planet) {
				bool hasMoon = userData.celestials.Count(c => c.HasCoords(new Coordinate(celestial.Coordinate.Galaxy, celestial.Coordinate.System, celestial.Coordinate.Position, Celestials.Moon))) == 1;
				if (!hasMoon) {
					if (fromTelegram)
						await SendTelegramMessage($"This planet does not have a Moon! Switch impossible.");
					return false;
				}
			}

			Coordinate dest = new();
			dest.Galaxy = celestial.Coordinate.Galaxy;
			dest.System = celestial.Coordinate.System;
			dest.Position = celestial.Coordinate.Position;

			if (celestial.Coordinate.Type == Celestials.Planet) {
				dest.Type = Celestials.Moon;
			} else {
				dest.Type = Celestials.Planet;
			}

			celestial = await UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await UpdatePlanet(celestial, UpdateTypes.Ships);

			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"[Switch] Skipping fleetsave from {celestial.Coordinate.ToString()}: No ships!");
				if (fromTelegram)
					await SendTelegramMessage($"No ships on {celestial.Coordinate}, did you /celestial?");
				return false;
			}

			var payload = celestial.Resources;
			if (celestial.Resources.Deuterium == 0) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"[Switch] Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel!");
				if (fromTelegram)
					await SendTelegramMessage($"Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel.");
				return false;
			}

			FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(celestial.Coordinate, dest, celestial.Ships, Missions.Deploy, speed, userData.researches, userData.serverData, userData.userInfo.Class);
			int fleetId = await SendFleet(celestial, celestial.Ships, dest, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

			if (fleetId != (int) SendFleetCode.GenericError ||
				fleetId != (int) SendFleetCode.AfterSleepTime ||
				fleetId != (int) SendFleetCode.NotEnoughSlots) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {dest.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				if (fromTelegram)
					await SendTelegramMessage($"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {dest.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				return true;
			}

			return false;
		}

		public async void TelegramSetCurrentCelestial(Coordinate coord, string celestialType, Feature updateType = Feature.Null, bool editsettings = false) {
			userData.celestials = await UpdateCelestials();

			//check if no error in submitted celestial (belongs to the current player)
			telegramUserData.CurrentCelestial = userData.celestials
				.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) coord.Galaxy)
				.Where(c => c.Coordinate.System == (int) coord.System)
				.Where(c => c.Coordinate.Position == (int) coord.Position)
				.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>(celestialType))
				.SingleOrDefault() ?? new() { ID = 0 };

			if (telegramUserData.CurrentCelestial.ID == 0) {
				await SendTelegramMessage("Error! Wrong information. Verify coordinate.\n");
				return;
			}
			if (editsettings) {
				await EditSettings(telegramUserData.CurrentCelestial, updateType);
				await SendTelegramMessage($"JSON settings updated to: {telegramUserData.CurrentCelestial.Coordinate.ToString()}\nWait few seconds for Bot to reload before sending commands.");
			} else {
				await SendTelegramMessage($"Main celestial successfuly updated to {telegramUserData.CurrentCelestial.Coordinate.ToString()}");
			}
			return;
		}

		public async Task<Celestial> TelegramGetCurrentCelestial() {
			if (telegramUserData.CurrentCelestial == null) {
				Celestial celestial;
				celestial = userData.celestials
					.Unique()
					.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
					.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
					.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
					.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
					.SingleOrDefault() ?? new() { ID = 0 };

				if (celestial.ID == 0) {
					await SendTelegramMessage("Error! Could not parse Celestial from JSON settings. Need <code>/editsettings</code>");
					return new Celestial();
				}

				telegramUserData.CurrentCelestial = celestial;
			}

			return telegramUserData.CurrentCelestial;
		}

		public async Task TelegramGetInfo(Celestial celestial) {

			celestial = await UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await UpdatePlanet(celestial, UpdateTypes.Ships);
			string result = "";
			string resources = $"{celestial.Resources.Metal.ToString("#,#", CultureInfo.InvariantCulture)} Metal\n" +
								$"{celestial.Resources.Crystal.ToString("#,#", CultureInfo.InvariantCulture)} Crystal\n" +
								$"{celestial.Resources.Deuterium.ToString("#,#", CultureInfo.InvariantCulture)} Deuterium\n\n";
			string ships = celestial.Ships.GetMovableShips().ToString();

			if (celestial.Resources.TotalResources == 0)
				result += "No Resources." ?? resources;
			if (celestial.Ships.GetMovableShips().IsEmpty())
				result += "No ships." ?? ships;

			await SendTelegramMessage($"{celestial.Coordinate.ToString()}\n\n" +
				"Resources:\n" +
				$"{resources}" +
				"Ships:\n" +
				$"{ships}");

			return;
		}

		public async Task SpyCrash(Celestial fromCelestial, Coordinate target = null) {
			decimal speed = Speeds.HundredPercent;
			fromCelestial = await UpdatePlanet(fromCelestial, UpdateTypes.Ships);
			fromCelestial = await UpdatePlanet(fromCelestial, UpdateTypes.Resources);
			var payload = fromCelestial.Resources;
			Random random = new Random();

			if (fromCelestial.Ships.EspionageProbe == 0 || payload.Deuterium < 1) {
				log(LogLevel.Information, LogSender.FleetScheduler, $"No probes or no Fuel on {fromCelestial.Coordinate.ToString()}!");
				await SendTelegramMessage($"No probes or no Fuel on {fromCelestial.Coordinate.ToString()}!");
				return;
			}
			// spycrash auto part
			if (target == null) {
				List<Coordinate> spycrash = new();
				int playerid = userData.userInfo.PlayerID;
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
					await SendTelegramMessage($"No planet to spycrash on could be found over system -2 -> +2");
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
				log(LogLevel.Information, LogSender.FleetScheduler, $"EspionageProbe sent to crash on {target.ToString()}");

				await SendTelegramMessage($"EspionageProbe sent to crash on {target.ToString()}");
			}
			return;
		}

		public async Task TelegramCollectDeut(long MinAmount = 0) {
			userData.fleets = await UpdateFleets();
			long TotalDeut = 0;
			Coordinate destinationCoordinate;

			Celestial cel = userData.celestials
					.Unique()
					.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoRepatriate.Target.Galaxy)
					.Where(c => c.Coordinate.System == (int) settings.Brain.AutoRepatriate.Target.System)
					.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoRepatriate.Target.Position)
					.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoRepatriate.Target.Type))
					.SingleOrDefault() ?? new() { ID = 0 };

			if (cel.ID == 0) {
				await SendTelegramMessage("Error! Could not parse auto repatriate Celestial from JSON settings. Need <code>/editsettings</code>");
				return;
			} else {
				destinationCoordinate = cel.Coordinate;
			}

			foreach (Celestial celestial in userData.celestials.ToList()) {
				if (celestial.Coordinate.IsSame(destinationCoordinate)) {
					continue;
				}
				if (celestial is Moon) {
					continue;
				}

				var tempCelestial = await UpdatePlanet(celestial, UpdateTypes.Fast);
				userData.fleets = await UpdateFleets();

				tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Resources);
				tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Ships);

				Buildables preferredShip = Buildables.LargeCargo;
				if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
					preferredShip = Buildables.LargeCargo;
				}
				Resources payload = tempCelestial.Resources;
				payload.Metal = 0;
				payload.Crystal = 0;
				payload.Food = 0;

				if ((long) tempCelestial.Resources.Deuterium < (long) MinAmount || payload.IsEmpty()) {
					continue;
				}

				long idealShips = _helpersService.CalcShipNumberForPayload(payload, preferredShip, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);

				Ships ships = new();
				if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
					if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
						ships.Add(preferredShip, idealShips);
					} else {
						ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
					}
					payload = _helpersService.CalcMaxTransportableResources(ships, payload, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);

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
				await SendTelegramMessage($"{TotalDeut} Deuterium sent.");
			} else {
				await SendTelegramMessage("No resources sent");
			}
		}
		public async Task AutoFleetSave(Celestial celestial, bool isSleepTimeFleetSave = false, long minDuration = 0, bool forceUnsafe = false, bool WaitFleetsReturn = false, Missions TelegramMission = Missions.None, bool fromTelegram = false, bool saveall = false) {
			DateTime departureTime = await GetDateTime();
			duration = minDuration;

			if (WaitFleetsReturn) {

				userData.fleets = await UpdateFleets();
				long interval;
				try {
					interval = (userData.fleets.OrderBy(f => f.BackIn).Last().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				} catch {
					interval = 0;
				}

				if (interval > 0 && (!timers.TryGetValue("GhostSleepTimer", out Timer value))) {
					//Stop features which are sending fleets
					StopColonize();
					StopBrainAutoResearch();
					StopBrainAutoMine();
					StopBrainLifeformAutoMine();
					StopBrainLifeformAutoResearch();
					StopBrainRepatriate();
					StopAutoFarm();
					StopHarvest();
					StopExpeditions();

					interval += RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime TimeToGhost = departureTime.AddMilliseconds(interval);
					NextWakeUpTime = TimeToGhost.AddMilliseconds(minDuration * 1000);

					if (saveall)
						timers.Add("GhostSleepTimer", new Timer(GhostandSleepAfterFleetsReturnAll, null, interval, Timeout.Infinite));
					else
						timers.Add("GhostSleepTimer", new Timer(GhostandSleepAfterFleetsReturn, null, interval, Timeout.Infinite));

					log(LogLevel.Information, LogSender.SleepMode, $"Fleets active, Next check at {TimeToGhost.ToString()}");
					await SendTelegramMessage($"Waiting for fleets return, delaying ghosting at {TimeToGhost.ToString()}");

					return;
				} else if (interval == 0 && (!timers.TryGetValue("GhostSleepTimer", out Timer value2))) {

					log(LogLevel.Information, LogSender.SleepMode, $"No fleets active, Ghosting now.");
					NextWakeUpTime = departureTime.AddMilliseconds(minDuration * 1000);
					if (saveall)
						GhostandSleepAfterFleetsReturnAll(null);
					else
						GhostandSleepAfterFleetsReturn(null);

					return;
				} else if (timers.TryGetValue("GhostSleepTimer", out Timer value3)) {
					await SendTelegramMessage($"GhostSleep already planned, try /cancelghostsleep");
					return;
				}
			}

			celestial = await UpdatePlanet(celestial, UpdateTypes.Ships);
			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fleet to save!");
				if (fromTelegram)
					await SendTelegramMessage($"{celestial.ToString()}: there is no fleet!");
				return;
			}

			celestial = await UpdatePlanet(celestial, UpdateTypes.Resources);
			Celestial destination = new() { ID = 0 };
			if (!forceUnsafe)
				forceUnsafe = (bool) settings.SleepMode.AutoFleetSave.ForceUnsafe; //not used anymore


			if (celestial.Resources.Deuterium == 0) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fuel!");
				if (fromTelegram)
					await SendTelegramMessage($"{celestial.ToString()}: there is no fuel!");
				return;
			}

			long maxDeuterium = celestial.Resources.Deuterium;

			if (isSleepTimeFleetSave) {
				if (DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp)) {
					if (departureTime >= wakeUp)
						wakeUp = wakeUp.AddDays(1);
					minDuration = (long) wakeUp.Subtract(departureTime).TotalSeconds;
				} else {
					log(LogLevel.Warning, LogSender.FleetScheduler, $"Could not plan fleetsave from {celestial.ToString()}: unable to parse comeback time");
					return;
				}
			}

			var payload = celestial.Resources;
			if ((long) settings.SleepMode.AutoFleetSave.DeutToLeave > 0)
				payload.Deuterium -= (long) settings.SleepMode.AutoFleetSave.DeutToLeave;
			if (payload.Deuterium < 0)
				payload.Deuterium = 0;

			FleetHypotesis possibleFleet = new();
			int fleetId = (int) SendFleetCode.GenericError;
			bool AlreadySent = false; //permit to swith to Harvest mission if not enough fuel to Deploy if celestial far away

			//Doing DefaultMission or telegram /ghostto mission
			Missions mission;
			if (!Missions.TryParse(settings.SleepMode.AutoFleetSave.DefaultMission, out mission)) {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Error: Could not parse 'DefaultMission' from settings, value set to Harvest.");
				mission = Missions.Harvest;
			}

			if (TelegramMission != Missions.None)
				mission = TelegramMission;

			List<FleetHypotesis> fleetHypotesis = await GetFleetSaveDestination(userData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
			if (fleetHypotesis.Count() > 0) {
				foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
					log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
					if (CheckFuel(fleet, celestial)) {
						fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, userData.userInfo.Class, true);

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
				await SendTelegramMessage($"No debris field found for {mission}, try to /spycrash.");
				return;
			} else if (fromTelegram && !AlreadySent && fleetHypotesis.Count() >= 0) {
				await SendTelegramMessage($"Available fuel: {celestial.Resources.Deuterium}\nNo destination found for {mission}, try to reduce ghost time.");
				return;
			}

			//Doing Deploy
			if (!AlreadySent) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} possible, checking next mission..");
				if (mission == Missions.Harvest) { mission = Missions.Deploy; } else { mission = Missions.Harvest; };
				mission = Missions.Deploy;
				fleetHypotesis = await GetFleetSaveDestination(userData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, userData.userInfo.Class, true);

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
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Colonize destination...");
				mission = Missions.Colonize;
				fleetHypotesis = await GetFleetSaveDestination(userData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, userData.userInfo.Class, true);

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
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Spy destination...");
				mission = Missions.Spy;
				fleetHypotesis = await GetFleetSaveDestination(userData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						log(LogLevel.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = await SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, userData.userInfo.Class, true);

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
			bool hasMoon = userData.celestials.Count(c => c.HasCoords(new Coordinate(celestial.Coordinate.Galaxy, celestial.Coordinate.System, celestial.Coordinate.Position, Celestials.Moon))) == 1;
			if (!AlreadySent && hasMoon && !timers.TryGetValue("GhostSleepTimer", out Timer val)) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} possible (missing fuel?), checking for switch if has Moon");
				//var validSpeeds = userData.userInfo.Class == CharacterClass.General ? Speeds.GetGeneralSpeedsList() : Speeds.GetNonGeneralSpeedsList();
				//Random randomSpeed = new Random();
				//decimal speed = validSpeeds[randomSpeed.Next(validSpeeds.Count)];
				decimal speed = 10;
				AlreadySent = await TelegramSwitch(speed, celestial);
			}

			if (!AlreadySent) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.Coordinate.ToString()} no suitable destination found, you gonna get hit!");
				await SendTelegramMessage($"Fleetsave from {celestial.Coordinate.ToString()} No destination found!, you gonna get hit!");
				return;
			}


			if ((bool) settings.SleepMode.AutoFleetSave.Recall && AlreadySent) {
				if (fleetId != (int) SendFleetCode.GenericError ||
					fleetId != (int) SendFleetCode.AfterSleepTime ||
					fleetId != (int) SendFleetCode.NotEnoughSlots) {
					Fleet fleet = userData.fleets.Single(fleet => fleet.ID == fleetId);
					DateTime time = await GetDateTime();
					var interval = ((minDuration / 2) * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					timers.Add($"RecallTimer-{fleetId.ToString()}", new Timer(RetireFleet, fleet, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.FleetScheduler, $"The fleet will be recalled at {newTime.ToString()}");
					if (fromTelegram)
						await SendTelegramMessage($"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}, recalled at {newTime.ToString()}");
				}
			} else {
				if (fleetId != (int) SendFleetCode.GenericError ||
					fleetId != (int) SendFleetCode.AfterSleepTime ||
					fleetId != (int) SendFleetCode.NotEnoughSlots) {
					Fleet fleet = userData.fleets.Single(fleet => fleet.ID == fleetId);
					DateTime returntime = (DateTime) fleet.BackTime;
					log(LogLevel.Information, LogSender.FleetScheduler, $"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, arrive at {possibleFleet.Duration} fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}");
					if (fromTelegram)
						await SendTelegramMessage($"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, arrive at {possibleFleet.Duration.ToString()}, returned at {returntime.ToString()} fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}");
				}
			}

		}

		private bool CheckFuel(FleetHypotesis fleetHypotesis, Celestial celestial) {
			if (celestial.Resources.Deuterium < fleetHypotesis.Fuel) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: not enough fuel!");
				return false;
			}
			if (_helpersService.CalcFleetFuelCapacity(fleetHypotesis.Ships, userData.serverData, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo) < fleetHypotesis.Fuel) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: ships don't have enough fuel capacity!");
				return false;
			}
			return true;
		}

		private async Task<List<FleetHypotesis>> GetFleetSaveDestination(List<Celestial> source, Celestial origin, DateTime departureDate, long minFlightTime, Missions mission, long maxFuel, bool forceUnsafe = false) {
			var validSpeeds = userData.userInfo.Class == CharacterClass.General ? Speeds.GetGeneralSpeedsList() : Speeds.GetNonGeneralSpeedsList();
			List<FleetHypotesis> possibleFleets = new();
			List<Coordinate> possibleDestinations = new();
			GalaxyInfo galaxyInfo = new();
			origin = await UpdatePlanet(origin, UpdateTypes.Resources);
			origin = await UpdatePlanet(origin, UpdateTypes.Ships);

			switch (mission) {
				case Missions.Spy:
					if (origin.Ships.EspionageProbe == 0) {
						log(LogLevel.Information, LogSender.FleetScheduler, $"No espionageprobe available, skipping to next mission...");
						break;
					}
					Coordinate destination = new(origin.Coordinate.Galaxy, origin.Coordinate.System, 16, Celestials.Planet);
					foreach (var currentSpeed in validSpeeds) {
						FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(origin.Coordinate, destination, origin.Ships.GetMovableShips(), mission, currentSpeed, userData.researches, userData.serverData, userData.userInfo.Class);

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
					break;

				case Missions.Colonize:
					if (origin.Ships.ColonyShip == 0) {
						log(LogLevel.Information, LogSender.FleetScheduler, $"No colony ship available, skipping to next mission...");
						break;
					}
					galaxyInfo = await _ogameService.GetGalaxyInfo(origin.Coordinate);
					int pos = 1;
					foreach (var planet in galaxyInfo.Planets) {
						if (planet == null)
							possibleDestinations.Add(new(origin.Coordinate.Galaxy, origin.Coordinate.System, pos));
						pos = +1;
					}

					if (possibleDestinations.Count() > 0) {
						foreach (var possibleDestination in possibleDestinations) {
							foreach (var currentSpeed in validSpeeds) {
								FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, userData.researches, userData.serverData, userData.userInfo.Class);

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
						log(LogLevel.Information, LogSender.FleetScheduler, $"No recycler available, skipping to next mission...");
						break;
					}
					int playerid = userData.userInfo.PlayerID;
					int sys = 0;
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
								FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, userData.researches, userData.serverData, userData.userInfo.Class);

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
					possibleDestinations = userData.celestials
						.Where(planet => planet.ID != origin.ID)
						.Where(planet => (planet.Coordinate.Type == Celestials.Moon))
						.Select(planet => planet.Coordinate)
						.ToList();

					if (possibleDestinations.Count == 0) {
						possibleDestinations = userData.celestials
							.Where(planet => planet.ID != origin.ID)
							.Select(planet => planet.Coordinate)
							.ToList();
					}

					foreach (var possibleDestination in possibleDestinations) {
						foreach (var currentSpeed in validSpeeds) {
							FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, userData.researches, userData.serverData, userData.userInfo.Class);

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


		public async void GhostandSleepAfterFleetsReturnAll(object state) {
			if (timers.TryGetValue("GhostSleepTimer", out Timer value))
				value.Dispose();
			timers.Remove("GhostSleepTimer");


			var celestialsToFleetsave = await UpdateCelestials();
			celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
			if (celestialsToFleetsave.Count == 0)
				celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Planet).ToList();

			foreach (Celestial celestial in celestialsToFleetsave)
				await AutoFleetSave(celestial, false, duration, false, false, telegramUserData.Mission, true);

			await SleepNow(NextWakeUpTime);
		}

		public async void GhostandSleepAfterFleetsReturn(object state) {
			if (timers.TryGetValue("GhostSleepTimer", out Timer value))
				value.Dispose();
			timers.Remove("GhostSleepTimer");

			await AutoFleetSave(telegramUserData.CurrentCelestialToSave, false, duration, false, false, telegramUserData.Mission, true);

			await SleepNow(NextWakeUpTime);
		}

		public async Task SleepNow(DateTime WakeUpTime) {
			long interval;

			DateTime time = await GetDateTime();
			interval = (long) WakeUpTime.Subtract(time).TotalMilliseconds;
			timers.Add("TelegramSleepModeTimer", new Timer(WakeUpNow, null, interval, Timeout.Infinite));
			await SendTelegramMessage($"Going to sleep, Waking Up at {WakeUpTime.ToString()}");
			log(LogLevel.Information, LogSender.SleepMode, $"Going to sleep..., Waking Up at {WakeUpTime.ToString()}");
			if (userData.isSleeping == false) {
				if (
					SettingsService.IsSettingSet(settings, "SleepMode") &&
					SettingsService.IsSettingSet(settings.SleepMode, "LogoutOnSleep") &&
					(bool) settings.SleepMode.LogoutOnSleep
				) {
					loggedIn = false;
					await _ogameService.Logout();
					log(LogLevel.Information, LogSender.SleepMode, $"Logged out from ogamed.");
				}

				userData.isSleeping = true;
			}
		}


		private async void HandleSleepMode(object state) {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value)) {
				return;
			}

			try {
				await WaitFeature();

				DateTime time = await GetDateTime();

				if (!(bool) settings.SleepMode.Active) {
					log(LogLevel.Warning, LogSender.SleepMode, "Sleep mode is disabled");
					WakeUp(null);
				} else if (!DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep)) {
					log(LogLevel.Warning, LogSender.SleepMode, "Unable to parse GoToSleep time. Sleep mode will be disabled");
					WakeUp(null);
				} else if (!DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp)) {
					log(LogLevel.Warning, LogSender.SleepMode, "Unable to parse WakeUp time. Sleep mode will be disabled");
					WakeUp(null);
				} else if (goToSleep == wakeUp) {
					log(LogLevel.Warning, LogSender.SleepMode, "GoToSleep time and WakeUp time must be different. Sleep mode will be disabled");
					WakeUp(null);
				} else {
					long interval;

					if (time >= goToSleep) {
						if (time >= wakeUp) {
							if (goToSleep >= wakeUp) {
								// YES YES YES
								// ASLEEP
								// WAKE UP NEXT DAY
								interval = (long) wakeUp.AddDays(1).Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								GoToSleep(newTime);
							} else {
								// YES YES NO
								// AWAKE
								// GO TO SLEEP NEXT DAY
								interval = (long) goToSleep.AddDays(1).Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								WakeUp(newTime);
							}
						} else {
							if (goToSleep >= wakeUp) {
								// YES NO YES
								// THIS SHOULDNT HAPPEN
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
							} else {
								// YES NO NO
								// ASLEEP
								// WAKE UP SAME DAY
								interval = (long) wakeUp.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								GoToSleep(newTime);
							}
						}
					} else {
						if (time >= wakeUp) {
							if (goToSleep >= wakeUp) {
								// NO YES YES
								// AWAKE
								// GO TO SLEEP SAME DAY
								interval = (long) goToSleep.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								WakeUp(newTime);
							} else {
								// NO YES NO
								// THIS SHOULDNT HAPPEN
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
							}
						} else {
							if (goToSleep >= wakeUp) {
								// NO NO YES
								// ASLEEP
								// WAKE UP SAME DAY
								interval = (long) wakeUp.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								GoToSleep(newTime);
							} else {
								// NO NO NO
								// AWAKE
								// GO TO SLEEP SAME DAY
								interval = (long) goToSleep.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								WakeUp(newTime);
							}
						}
					}
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while handling sleep mode: {e.Message}");
				log(LogLevel.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = await GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await CheckCelestials();
			} finally {
				releaseFeature();
			}
		}

		private async void GoToSleep(object state) {
			try {
				userData.fleets = await UpdateFleets();
				bool delayed = false;
				if ((bool) settings.SleepMode.PreventIfThereAreFleets && userData.fleets.Count() > 0) {
					if (DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp) && DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep)) {
						DateTime time = await GetDateTime();
						if (time >= goToSleep && time >= wakeUp && goToSleep < wakeUp)
							goToSleep = goToSleep.AddDays(1);
						if (time >= goToSleep && time >= wakeUp && goToSleep >= wakeUp)
							wakeUp = wakeUp.AddDays(1);

						List<Fleet> tempFleets = new();
						var timeToWakeup = wakeUp.Subtract(time).TotalSeconds;
						// All Deployment Missions that will arrive during sleep
						tempFleets.AddRange(userData.fleets
							.Where(f => f.Mission == Missions.Deploy)
							.Where(f => f.ArriveIn <= timeToWakeup)
						);
						// All other Fleets.Mission
						tempFleets.AddRange(userData.fleets
							.Where(f => f.BackIn <= timeToWakeup)
						);
						if (tempFleets.Count() > 0) {
							log(LogLevel.Information, LogSender.SleepMode, "There are fleets that would come back during sleep time. Delaying sleep mode.");
							long interval = 0;
							foreach (Fleet tempFleet in tempFleets) {
								if (tempFleet.Mission == Missions.Deploy) {
									if (tempFleet.ArriveIn > interval)
										interval = (long) tempFleet.ArriveIn;
								} else {
									if (tempFleet.BackIn > interval)
										interval = (long) tempFleet.BackIn;
								}
							}
							interval *= (long) 1000;
							if (interval <= 0)
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							DateTime newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
							delayed = true;
							log(LogLevel.Information, LogSender.SleepMode, $"Fleets active, Next check at {newTime.ToString()}");
							if ((bool) settings.SleepMode.TelegramMessenger.Active) {
								await SendTelegramMessage($"Fleets active, Next check at {newTime.ToString()}");
							}
						}
					} else {
						log(LogLevel.Warning, LogSender.SleepMode, "Unable to parse WakeUp or GoToSleep time.");
					}
				}
				if (!delayed) {
					log(LogLevel.Information, LogSender.SleepMode, "Going to sleep...");
					log(LogLevel.Information, LogSender.SleepMode, $"Waking Up at {state.ToString()}");

					if ((bool) settings.SleepMode.AutoFleetSave.Active) {
						var celestialsToFleetsave = await UpdatePlanets(UpdateTypes.Ships);
						if ((bool) settings.SleepMode.AutoFleetSave.OnlyMoons)
							celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
						foreach (Celestial celestial in celestialsToFleetsave.OrderByDescending(c => c.Ships.GetFleetPoints())) {
							try {
								await AutoFleetSave(celestial, true);
							} catch (Exception e) {
								_logger.WriteLog(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while fleetsaving: {e.Message}");
							}
						}
					}

					if ((bool) settings.SleepMode.TelegramMessenger.Active && state != null) {
						await SendTelegramMessage($"[{userData.userInfo.PlayerName}{userData.serverData.Name}] Going to sleep, Waking Up at {state.ToString()}");
					}
					if (userData.isSleeping == false) {
						if (
							SettingsService.IsSettingSet(settings, "SleepMode") &&
							SettingsService.IsSettingSet(settings.SleepMode, "LogoutOnSleep") &&
							(bool) settings.SleepMode.LogoutOnSleep
						) {
							loggedIn = false;
							await _ogameService.Logout();
							log(LogLevel.Information, LogSender.SleepMode, $"Logged out from ogamed.");
						}
						userData.isSleeping = true;
					}
				}
				InitializeFeatures();
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while going to sleep: {e.Message}");
				log(LogLevel.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = await GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await CheckCelestials();
			}
		}


		public async Task<bool> TelegramIsUnderAttack() {
			bool result = await _ogameService.IsUnderAttack();

			return result;
		}


		public async void WakeUpNow(object state) {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value))
				value.Dispose();
			timers.Remove("TelegramSleepModeTimer");
			await SendTelegramMessage($"Bot woke up!");

			log(LogLevel.Information, LogSender.SleepMode, "Bot woke up!");

			if (userData.isSleeping) {
				userData.isSleeping = false;
				if (
					SettingsService.IsSettingSet(settings, "SleepMode") &&
					SettingsService.IsSettingSet(settings.SleepMode, "LogoutOnSleep") &&
					(bool) settings.SleepMode.LogoutOnSleep
				) {
					await _ogameService.Login();
					loggedIn = true;
					log(LogLevel.Information, LogSender.SleepMode, "Ogamed logged in again!");
				}
			}
			InitializeFeatures();
		}

		private async void WakeUp(object state) {
			try {
				log(LogLevel.Information, LogSender.SleepMode, "Waking Up...");
				if ((bool) settings.SleepMode.TelegramMessenger.Active && state != null) {
					await SendTelegramMessage($"Waking up");
					await SendTelegramMessage($"Going to sleep at {state.ToString()}");
				}
				if (userData.isSleeping) {
					userData.isSleeping = false;
					await _ogameService.Login();
					log(LogLevel.Information, LogSender.SleepMode, "Ogamed logged in again!");
				}
				InitializeFeatures();

			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while waking up: {e.Message}");
				log(LogLevel.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = await GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await CheckCelestials();
			}
		}

		private async Task FakeActivity() {
			//checking if under attack by making activity on planet/moon configured in settings (otherwise make acti on latest activated planet)
			// And make activity on one more random planet to fake real player
			Celestial celestial;
			Celestial randomCelestial;
			celestial = userData.celestials
				.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
				.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
				.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
				.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
				.SingleOrDefault() ?? new() { ID = 0 };

			if (celestial.ID != 0) {
				celestial = await UpdatePlanet(celestial, UpdateTypes.Defences);
			}
			randomCelestial = userData.celestials.Shuffle().FirstOrDefault() ?? new() { ID = 0 };
			if (randomCelestial.ID != 0) {
				randomCelestial = await UpdatePlanet(randomCelestial, UpdateTypes.Defences);
			}

			return;
		}

		private async void Defender(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Defender].WaitAsync();
				log(LogLevel.Information, LogSender.Defender, "Checking attacks...");

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Defender, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Defender].Release();
					return;
				}

				await FakeActivity();
				userData.fleets = await UpdateFleets();
				bool isUnderAttack = await _ogameService.IsUnderAttack();
				DateTime time = await GetDateTime();
				if (isUnderAttack) {
					if ((bool) settings.Defender.Alarm.Active)
						await Task.Factory.StartNew(() => ConsoleHelpers.PlayAlarm());
					// UpdateTitle(false, true);
					log(LogLevel.Warning, LogSender.Defender, "ENEMY ACTIVITY!!!");
					userData.attacks = await _ogameService.GetAttacks();
					foreach (AttackerFleet attack in userData.attacks) {
						HandleAttack(attack);
					}
				} else {
					log(LogLevel.Information, LogSender.Defender, "Your empire is safe");
				}
				long interval = RandomizeHelper.CalcRandomInterval((int) settings.Defender.CheckIntervalMin, (int) settings.Defender.CheckIntervalMax);
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.Defender, $"Next check at {newTime.ToString()}");
				await CheckCelestials();
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Defender, $"An error has occurred while checking for attacks: {e.Message}");
				log(LogLevel.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
				DateTime time = await GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.Defender, $"Next check at {newTime.ToString()}");
				await CheckCelestials();
			} finally {
				if (!userData.isSleeping)
					xaSem[Feature.Defender].Release();
			}
		}

		private async void BuyOfferOfTheDay(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.Brain.Active && (bool) settings.Brain.BuyOfferOfTheDay.Active) {
					log(LogLevel.Information, LogSender.Brain, "Buying offer of the day...");
					if (userData.isSleeping) {
						log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
						xaSem[Feature.Brain].Release();
						return;
					}
					try {
						await _ogameService.BuyOfferOfTheDay();
						log(LogLevel.Information, LogSender.Brain, "Offer of the day succesfully bought.");
					} catch {
						log(LogLevel.Information, LogSender.Brain, "Offer of the day already bought.");
					}

				} else {
					log(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"BuyOfferOfTheDay Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogLevel.Information, LogSender.Brain, $"Stopping feature.");
					} else {
						var time = await GetDateTime();
						var interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.BuyOfferOfTheDay.CheckIntervalMin, (int) settings.Brain.BuyOfferOfTheDay.CheckIntervalMax);
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("OfferOfTheDayTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Brain, $"Next BuyOfferOfTheDay check at {newTime.ToString()}");
						await CheckCelestials();
					}
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private async void AutoResearch(object state) {
			int fleetId = (int) SendFleetCode.GenericError;
			bool stop = false;
			bool delay = false;
			long delayResearch = 0;
			try {
				await xaSem[Feature.Brain].WaitAsync();
				log(LogLevel.Information, LogSender.Brain, "Running autoresearch...");

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.Brain.Active && (bool) settings.Brain.AutoResearch.Active || timers.TryGetValue("AutoResearchTimer", out Timer value)) {
					userData.researches = await _ogameService.GetResearches();
					Planet celestial;
					var parseSucceded = userData.celestials
						.Any(c => c.HasCoords(new(
							(int) settings.Brain.AutoResearch.Target.Galaxy,
							(int) settings.Brain.AutoResearch.Target.System,
							(int) settings.Brain.AutoResearch.Target.Position,
							Celestials.Planet
						))
					);
					if (parseSucceded) {
						celestial = userData.celestials
							.Unique()
							.Single(c => c.HasCoords(new(
								(int) settings.Brain.AutoResearch.Target.Galaxy,
								(int) settings.Brain.AutoResearch.Target.System,
								(int) settings.Brain.AutoResearch.Target.Position,
								Celestials.Planet
								)
							)) as Planet;
					} else {
						log(LogLevel.Warning, LogSender.Brain, "Unable to parse Brain.AutoResearch.Target. Falling back to planet with biggest Research Lab");
						userData.celestials = await UpdatePlanets(UpdateTypes.Facilities);
						celestial = userData.celestials
							.Where(c => c.Coordinate.Type == Celestials.Planet)
							.OrderByDescending(c => c.Facilities.ResearchLab)
							.First() as Planet;
					}

					celestial = await UpdatePlanet(celestial, UpdateTypes.Facilities) as Planet;
					if (celestial.Facilities.ResearchLab == 0) {
						log(LogLevel.Information, LogSender.Brain, "Skipping AutoResearch: Research Lab is missing on target planet.");
						return;
					}
					celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions) as Planet;
					if (celestial.Constructions.ResearchID != 0) {
						delayResearch = (long) celestial.Constructions.ResearchCountdown * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						log(LogLevel.Information, LogSender.Brain, "Skipping AutoResearch: there is already a research in progress.");
						return;
					}
					if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab) {
						log(LogLevel.Information, LogSender.Brain, "Skipping AutoResearch: the Research Lab is upgrading.");
						return;
					}
					userData.slots = await UpdateSlots();
					celestial = await UpdatePlanet(celestial, UpdateTypes.Facilities) as Planet;
					celestial = await UpdatePlanet(celestial, UpdateTypes.Resources) as Planet;
					celestial = await UpdatePlanet(celestial, UpdateTypes.ResourcesProduction) as Planet;

					Buildables research;

					if ((bool) settings.Brain.AutoResearch.PrioritizeAstrophysics || (bool) settings.Brain.AutoResearch.PrioritizePlasmaTechnology || (bool) settings.Brain.AutoResearch.PrioritizeEnergyTechnology || (bool) settings.Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork) {
						List<Celestial> planets = new();
						foreach (var p in userData.celestials) {
							if (p.Coordinate.Type == Celestials.Planet) {
								var newPlanet = await UpdatePlanet(p, UpdateTypes.Facilities);
								newPlanet = await UpdatePlanet(p, UpdateTypes.Buildings);
								planets.Add(newPlanet);
							}
						}
						var plasmaDOIR = _helpersService.CalcNextPlasmaTechDOIR(planets.Where(c => c is Planet).Cast<Planet>().ToList<Planet>(), userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
						log(LogLevel.Debug, LogSender.Brain, $"Next Plasma tech DOIR: {Math.Round(plasmaDOIR, 2).ToString()}");
						var astroDOIR = _helpersService.CalcNextAstroDOIR(planets.Where(c => c is Planet).Cast<Planet>().ToList<Planet>(), userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
						log(LogLevel.Debug, LogSender.Brain, $"Next Astro DOIR: {Math.Round(astroDOIR, 2).ToString()}");

						if (
							(bool) settings.Brain.AutoResearch.PrioritizePlasmaTechnology &&
							userData.lastDOIR > 0 &&
							plasmaDOIR <= userData.lastDOIR &&
							plasmaDOIR <= (float) settings.Brain.AutoMine.MaxDaysOfInvestmentReturn &&
							(int) settings.Brain.AutoResearch.MaxPlasmaTechnology >= userData.researches.PlasmaTechnology + 1 &&
							celestial.Facilities.ResearchLab >= 4 &&
							userData.researches.EnergyTechnology >= 8 &
							userData.researches.LaserTechnology >= 10 &&
							userData.researches.IonTechnology >= 5
						) {
							research = Buildables.PlasmaTechnology;
						} else if ((bool) settings.Brain.AutoResearch.PrioritizeEnergyTechnology && _helpersService.ShouldResearchEnergyTech(planets.Where(c => c.Coordinate.Type == Celestials.Planet).Cast<Planet>().ToList<Planet>(), userData.researches, (int) settings.Brain.AutoResearch.MaxEnergyTechnology, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull)) {
							research = Buildables.EnergyTechnology;
						} else if (
							(bool) settings.Brain.AutoResearch.PrioritizeAstrophysics &&
							userData.lastDOIR > 0 &&
							(int) settings.Brain.AutoResearch.MaxAstrophysics >= (userData.researches.Astrophysics % 2 == 0 ? userData.researches.Astrophysics + 1 : userData.researches.Astrophysics + 2) &&
							astroDOIR <= (float) settings.Brain.AutoMine.MaxDaysOfInvestmentReturn &&
							astroDOIR <= userData.lastDOIR &&
							celestial.Facilities.ResearchLab >= 3 &&
							userData.researches.EspionageTechnology >= 4 &&
							userData.researches.ImpulseDrive >= 3
						) {
							research = Buildables.Astrophysics;
						} else {
							research = _helpersService.GetNextResearchToBuild(celestial as Planet, userData.researches, (bool) settings.Brain.AutoMine.PrioritizeRobotsAndNanites, userData.slots, (int) settings.Brain.AutoResearch.MaxEnergyTechnology, (int) settings.Brain.AutoResearch.MaxLaserTechnology, (int) settings.Brain.AutoResearch.MaxIonTechnology, (int) settings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) settings.Brain.AutoResearch.MaxPlasmaTechnology, (int) settings.Brain.AutoResearch.MaxCombustionDrive, (int) settings.Brain.AutoResearch.MaxImpulseDrive, (int) settings.Brain.AutoResearch.MaxHyperspaceDrive, (int) settings.Brain.AutoResearch.MaxEspionageTechnology, (int) settings.Brain.AutoResearch.MaxComputerTechnology, (int) settings.Brain.AutoResearch.MaxAstrophysics, (int) settings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) settings.Brain.AutoResearch.MaxWeaponsTechnology, (int) settings.Brain.AutoResearch.MaxShieldingTechnology, (int) settings.Brain.AutoResearch.MaxArmourTechnology, (bool) settings.Brain.AutoResearch.OptimizeForStart, (bool) settings.Brain.AutoResearch.EnsureExpoSlots, userData.userInfo.Class, userData.staff.Geologist, userData.staff.Admiral);
						}
					} else {
						research = _helpersService.GetNextResearchToBuild(celestial as Planet, userData.researches, (bool) settings.Brain.AutoMine.PrioritizeRobotsAndNanites, userData.slots, (int) settings.Brain.AutoResearch.MaxEnergyTechnology, (int) settings.Brain.AutoResearch.MaxLaserTechnology, (int) settings.Brain.AutoResearch.MaxIonTechnology, (int) settings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) settings.Brain.AutoResearch.MaxPlasmaTechnology, (int) settings.Brain.AutoResearch.MaxCombustionDrive, (int) settings.Brain.AutoResearch.MaxImpulseDrive, (int) settings.Brain.AutoResearch.MaxHyperspaceDrive, (int) settings.Brain.AutoResearch.MaxEspionageTechnology, (int) settings.Brain.AutoResearch.MaxComputerTechnology, (int) settings.Brain.AutoResearch.MaxAstrophysics, (int) settings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) settings.Brain.AutoResearch.MaxWeaponsTechnology, (int) settings.Brain.AutoResearch.MaxShieldingTechnology, (int) settings.Brain.AutoResearch.MaxArmourTechnology, (bool) settings.Brain.AutoResearch.OptimizeForStart, (bool) settings.Brain.AutoResearch.EnsureExpoSlots, userData.userInfo.Class, userData.staff.Geologist, userData.staff.Admiral);
					}

					if (
						(bool) settings.Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork &&
						research != Buildables.Null &&
						research != Buildables.IntergalacticResearchNetwork &&
						celestial.Facilities.ResearchLab >= 10 &&
						userData.researches.ComputerTechnology >= 8 &&
						userData.researches.HyperspaceTechnology >= 8 &&
						(int) settings.Brain.AutoResearch.MaxIntergalacticResearchNetwork >= _helpersService.GetNextLevel(userData.researches, Buildables.IntergalacticResearchNetwork) &&
						userData.celestials.Any(c => c.Facilities != null)
					) {
						var cumulativeLabLevel = _helpersService.CalcCumulativeLabLevel(userData.celestials, userData.researches);
						var researchTime = _helpersService.CalcProductionTime(research, _helpersService.GetNextLevel(userData.researches, research), userData.serverData.SpeedResearch, celestial.Facilities, cumulativeLabLevel, userData.userInfo.Class == CharacterClass.Discoverer, userData.staff.Technocrat);
						var irnTime = _helpersService.CalcProductionTime(Buildables.IntergalacticResearchNetwork, _helpersService.GetNextLevel(userData.researches, Buildables.IntergalacticResearchNetwork), userData.serverData.SpeedResearch, celestial.Facilities, cumulativeLabLevel, userData.userInfo.Class == CharacterClass.Discoverer, userData.staff.Technocrat);
						if (irnTime < researchTime) {
							research = Buildables.IntergalacticResearchNetwork;
						}
					}

					int level = _helpersService.GetNextLevel(userData.researches, research);
					if (research != Buildables.Null) {
						celestial = await UpdatePlanet(celestial, UpdateTypes.Resources) as Planet;
						Resources cost = _helpersService.CalcPrice(research, level);
						if (celestial.Resources.IsEnoughFor(cost)) {
							try {
								await _ogameService.BuildCancelable(celestial, research);
								log(LogLevel.Information, LogSender.Brain, $"Research {research.ToString()} level {level.ToString()} started on {celestial.ToString()}");
							} catch {
								log(LogLevel.Warning, LogSender.Brain, $"Research {research.ToString()} level {level.ToString()} could not be started on {celestial.ToString()}");
							}
						} else {
							log(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {research.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {cost.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
							if ((bool) settings.Brain.AutoResearch.Transports.Active) {
								userData.fleets = await UpdateFleets();
								if (!_helpersService.IsThereTransportTowardsCelestial(celestial, userData.fleets)) {
									Celestial origin = userData.celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoResearch.Transports.Origin.Galaxy)
										.Where(c => c.Coordinate.System == (int) settings.Brain.AutoResearch.Transports.Origin.System)
										.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoResearch.Transports.Origin.Position)
										.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoResearch.Transports.Origin.Type))
										.SingleOrDefault() ?? new() { ID = 0 };
									fleetId = await HandleMinerTransport(origin, celestial, cost);
									if (fleetId == (int) SendFleetCode.AfterSleepTime) {
										stop = true;
									}
									if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
										delay = true;
									}
								} else {
									log(LogLevel.Information, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
									fleetId = (userData.fleets
										.Where(f => f.Mission == Missions.Transport)
										.Where(f => f.Resources.TotalResources > 0)
										.Where(f => f.ReturnFlight == false)
										.Where(f => f.Destination.Galaxy == celestial.Coordinate.Galaxy)
										.Where(f => f.Destination.System == celestial.Coordinate.System)
										.Where(f => f.Destination.Position == celestial.Coordinate.Position)
										.Where(f => f.Destination.Type == celestial.Coordinate.Type)
										.OrderByDescending(f => f.ArriveIn)
										.FirstOrDefault() ?? new() { ID = 0 })
										.ID;
								}
							}
						}
					}
				} else {
					log(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"AutoResearch Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogLevel.Information, LogSender.Brain, $"Stopping feature.");
					} else if (delay) {
						log(LogLevel.Information, LogSender.Brain, $"Delaying...");
						var time = await GetDateTime();
						userData.fleets = await UpdateFleets();
						long interval;
						try {
							interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) settings.AutoResearch.CheckIntervalMin, (int) settings.AutoResearch.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoResearchTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					} else if (delayResearch > 0) {
						var time = await GetDateTime();
						var newTime = time.AddMilliseconds(delayResearch);
						timers.GetValueOrDefault("AutoResearchTimer").Change(delayResearch, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					} else {
						long interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoResearch.CheckIntervalMin, (int) settings.Brain.AutoResearch.CheckIntervalMax);
						Planet celestial = userData.celestials
							.Unique()
							.SingleOrDefault(c => c.HasCoords(new(
								(int) settings.Brain.AutoResearch.Target.Galaxy,
								(int) settings.Brain.AutoResearch.Target.System,
								(int) settings.Brain.AutoResearch.Target.Position,
								Celestials.Planet
								)
							)) as Planet ?? new Planet() { ID = 0 };
						var time = await GetDateTime();
						if (celestial.ID != 0) {
							userData.fleets = await UpdateFleets();
							celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions) as Planet;
							var incomingFleets = _helpersService.GetIncomingFleets(celestial, userData.fleets);
							if (celestial.Constructions.ResearchCountdown != 0)
								interval = (long) ((long) celestial.Constructions.ResearchCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							else if (fleetId > (int) SendFleetCode.GenericError) {
								var fleet = userData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
								interval = (fleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							} else if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab)
								interval = (long) ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							else if (incomingFleets.Count() > 0) {
								var fleet = incomingFleets
									.OrderBy(f => (f.Mission == Missions.Transport || f.Mission == Missions.Deploy) ? f.ArriveIn : f.BackIn)
									.First();
								interval = (((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							} else {
								interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoResearch.CheckIntervalMin, (int) settings.Brain.AutoResearch.CheckIntervalMax);
							}
						}
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoResearchTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					}
					await CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private async void AutoFarm(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.AutoFarm].WaitAsync();

				log(LogLevel.Information, LogSender.AutoFarm, "Running autofarm...");

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.AutoFarm, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.AutoFarm.Active) {
					// If not enough slots are free, the farmer cannot run.
					userData.slots = await UpdateSlots();

					int freeSlots = userData.slots.Free;
					int slotsToLeaveFree = (int) settings.AutoFarm.SlotsToLeaveFree;
					int totalSlotsForProbing = userData.slots.Total - slotsToLeaveFree;
					if (freeSlots <= slotsToLeaveFree) {
						log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to start auto farm, no slots available");
						return;
					}

					try {
						// Prune all reports older than KeepReportFor and all reports of state AttackSent: information no longer actual.
						var newTime = await GetDateTime();
						var removeReports = userData.farmTargets.Where(t => t.State == FarmState.AttackSent || (t.Report != null && DateTime.Compare(t.Report.Date.AddMinutes((double) settings.AutoFarm.KeepReportFor), newTime) < 0)).ToList();
						foreach (var remove in removeReports) {
							var updateReport = remove;
							updateReport.State = FarmState.ProbesPending;
							updateReport.Report = null;
							userData.farmTargets.Remove(remove);
							userData.farmTargets.Add(updateReport);
						}

						// Keep local record of userData.celestials, to be updated by autofarmer itself, to reduce ogamed calls.
						var localCelestials = await UpdateCelestials();
						Dictionary<int, long> celestialProbes = new Dictionary<int, long>();
						foreach (var celestial in localCelestials) {
							Celestial tempCelestial = await UpdatePlanet(celestial, UpdateTypes.Fast);
							tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Ships);
							celestialProbes.Add(tempCelestial.ID, tempCelestial.Ships.EspionageProbe);
						}

						// Keep track of number of targets probed.
						int numProbed = 0;

						/// Galaxy scanning + target probing.
						log(LogLevel.Information, LogSender.AutoFarm, "Detecting farm targets...");
						foreach (var range in settings.AutoFarm.ScanRange) {
							if (SettingsService.IsSettingSet(settings.AutoFarm, "TargetsProbedBeforeAttack") && ((int) settings.AutoFarm.TargetsProbedBeforeAttack != 0) && numProbed >= (int) settings.AutoFarm.TargetsProbedBeforeAttack)
								break;

							int galaxy = (int) range.Galaxy;
							int startSystem = (int) range.StartSystem;
							int endSystem = (int) range.EndSystem;

							// Loop from start to end system.
							for (var system = startSystem; system <= endSystem; system++) {
								if (SettingsService.IsSettingSet(settings.AutoFarm, "TargetsProbedBeforeAttack") && ((int) settings.AutoFarm.TargetsProbedBeforeAttack != 0) && numProbed >= (int) settings.AutoFarm.TargetsProbedBeforeAttack)
									break;

								// Check excluded system.
								bool excludeSystem = false;
								foreach (var exclude in settings.AutoFarm.Exclude) {
									bool hasPosition = false;
									foreach (var value in exclude.Keys)
										if (value == "Position")
											hasPosition = true;
									if ((int) exclude.Galaxy == galaxy && (int) exclude.System == system && !hasPosition) {
										log(LogLevel.Information, LogSender.AutoFarm, $"Skipping system {system.ToString()}: system in exclude list.");
										excludeSystem = true;
										break;
									}
								}
								if (excludeSystem == true)
									continue;

								var galaxyInfo = await _ogameService.GetGalaxyInfo(galaxy, system);
								var planets = galaxyInfo.Planets.Where(p => p != null && p.Inactive && !p.Administrator && !p.Banned && !p.Vacation);
								List<Celestial> scannedTargets = planets.Cast<Celestial>().ToList();
								await UpdateFleets();
								//Remove all targets that are currently under attack (necessary if bot or instance is restarted)
								scannedTargets.RemoveAll(t => userData.fleets.Any(f => f.Destination.IsSame(t.Coordinate) && f.Mission == Missions.Attack));
								log(LogLevel.Debug, LogSender.AutoFarm, $"Found {scannedTargets.Count} targets on System {galaxy}:{system}");

								if (!planets.Any())
									continue;

								if ((bool) settings.AutoFarm.ExcludeMoons == false) {
									foreach (var t in planets) {
										if (t.Moon != null) {
											Celestial tempCelestial = t.Moon;
											tempCelestial.Coordinate = t.Coordinate;
											tempCelestial.Coordinate.Type = Celestials.Moon;
											scannedTargets.Add(tempCelestial);
										}
									}
								}

								// Add each planet that has inactive status to userData.farmTargets.
								foreach (Celestial planet in scannedTargets) {
									// Check if target is below set minimum rank.
									if (SettingsService.IsSettingSet(settings.AutoFarm, "MinimumPlayerRank") && settings.AutoFarm.MinimumPlayerRank != 0) {
										int rank = 1;
										if (planet.Coordinate.Type == Celestials.Planet) {
											rank = (planet as Planet).Player.Rank;
										} else {
											if (scannedTargets.Any(t => t.HasCoords(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet)))) {
												rank = (scannedTargets.Single(t => t.HasCoords(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet))) as Planet).Player.Rank;
											}
										}
										if ((int) settings.AutoFarm.MinimumPlayerRank < rank) {
											continue;
										}
									}

									if (SettingsService.IsSettingSet(settings.AutoFarm, "TargetsProbedBeforeAttack") &&
										settings.AutoFarm.TargetsProbedBeforeAttack != 0 && numProbed >= (int) settings.AutoFarm.TargetsProbedBeforeAttack) {
										log(LogLevel.Information, LogSender.AutoFarm, "Maximum number of targets to probe reached, proceeding to attack.");
										break;
									}

									// Check excluded planet.
									bool excludePlanet = false;
									foreach (var exclude in settings.AutoFarm.Exclude) {
										bool hasPosition = false;
										foreach (var value in exclude.Keys)
											if (value == "Position")
												hasPosition = true;
										if ((int) exclude.Galaxy == galaxy && (int) exclude.System == system && hasPosition && (int) exclude.Position == planet.Coordinate.Position) {
											log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {planet.ToString()}: celestial in exclude list.");
											excludePlanet = true;
											break;
										}
									}
									if (excludePlanet == true)
										continue;

									// Check if planet with coordinates exists already in userData.farmTargets list.
									var exists = userData.farmTargets.Where(t => t != null && t.Celestial.HasCoords(planet.Coordinate)).ToList();
									if (exists.Count() > 1) {
										// BUG: Same coordinates should never appear multiple times in userData.farmTargets. The list should only contain unique coordinates.
										//Remove all except the first to be able to continue
										log(LogLevel.Warning, LogSender.AutoFarm, "BUG: Same coordinates appeared multiple times within userData.farmTargets!");
										var firstExisting = exists.First();
										userData.farmTargets.RemoveAll(c => c.Celestial.HasCoords(planet.Coordinate) && c.Celestial.ID != firstExisting.Celestial.ID);
									}

									FarmTarget target = new(planet, FarmState.ProbesPending);

									if (!exists.Any()) {
										// Does not exist, add to userData.farmTargets list, set state to probes pending.
										userData.farmTargets.Add(target);
									} else {
										// Already exists, update userData.farmTargets list with updated planet.
										var farmTarget = exists.First();
										target = farmTarget;
										target.Celestial = planet;

										if (farmTarget.State == FarmState.Idle)
											target.State = FarmState.ProbesPending;

										userData.farmTargets.Remove(farmTarget);
										userData.farmTargets.Add(target);

										// If target marked not suitable based on a non-expired espionage report, skip probing.
										if (farmTarget.State == FarmState.NotSuitable && farmTarget.Report != null) {
											continue;
										}

										// If probes are already sent or if an attack is pending, skip probing.
										if (farmTarget.State == FarmState.ProbesSent || farmTarget.State == FarmState.AttackPending) {
											continue;
										}
									}

									// Send spy probe from closest celestial with available probes to the target.
									List<Celestial> tempCelestials = (settings.AutoFarm.Origin.Length > 0) ? _helpersService.ParseCelestialsList(settings.AutoFarm.Origin, userData.celestials) : userData.celestials;
									List<Celestial> closestCelestials = tempCelestials
										.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
										.OrderBy(c => _helpersService.CalcDistance(c.Coordinate, target.Celestial.Coordinate, userData.serverData)).ToList();

									Celestial bestOrigin = null;
									int neededProbes = (int) settings.AutoFarm.NumProbes;
									if (target.State == FarmState.ProbesRequired)
										neededProbes *= 3;
									if (target.State == FarmState.FailedProbesRequired)
										neededProbes *= 9;

									await UpdatePlanets(UpdateTypes.Ships);

									await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds));

									userData.fleets = await UpdateFleets();
									var probesInMission = userData.fleets.Select(c => c.Ships).Sum(c => c.EspionageProbe);
									long totalProbesInAllCelestials = closestCelestials.Sum(c => c.Ships.EspionageProbe) + probesInMission;
									KeyValuePair<Celestial, int> minBackIn = new KeyValuePair<Celestial, int>(null, int.MaxValue);
									foreach (var closest in closestCelestials) {
										// If local record indicate not enough espionage probes are available, update record to make sure this is correct.
										if (celestialProbes[closest.ID] < neededProbes) {
											var tempCelestial = await UpdatePlanet(closest, UpdateTypes.Ships);
											celestialProbes.Remove(closest.ID);
											celestialProbes.Add(closest.ID, tempCelestial.Ships.EspionageProbe);
										}

										if (celestialProbes[closest.ID] >= neededProbes) {
											//There are enough probes so it's the best origin and we can stop searching
											bestOrigin = closest;
											break;
										}

										// No probes available in this celestial
										userData.fleets = await UpdateFleets();

										// If there are no free slots, update the minimum time to wait for current missions return.
										// If there are no free slots, wait for probes to come back to current celestial.
										if (freeSlots <= slotsToLeaveFree) {
											var espionageMissions = _helpersService.GetMissionsInProgress(closest.Coordinate, Missions.Spy, userData.fleets);
											if (espionageMissions.Any()) {
												var returningProbes = espionageMissions.Sum(f => f.Ships.EspionageProbe);
												if (celestialProbes[closest.ID] + returningProbes >= neededProbes) {
													var returningFleets = espionageMissions.OrderBy(f => f.BackIn).ToArray();
													long probesCount = 0;
													for (int i = 0; i < returningFleets.Length; i++) {
														probesCount += returningFleets[i].Ships.EspionageProbe;
														if (probesCount >= neededProbes) {
															if (minBackIn.Value > returningFleets[i].BackIn)
																minBackIn = new KeyValuePair<Celestial, int>(closest, returningFleets[i].BackIn ?? int.MaxValue);
															continue;
														}
													}
												}
											}
										} else {
											//If no bestOrigin detected, the total number of probes is not enough but there are free slots, then calculate if can be built from this celestial
											log(LogLevel.Warning, LogSender.AutoFarm, $"Cannot spy {target.Celestial.Coordinate.ToString()} from {closest.Coordinate.ToString()}, insufficient probes ({celestialProbes[closest.ID]}/{neededProbes}).");
											if (bestOrigin != null)
												continue;

											//If total probes of all the planets is greater than the needed, then avoid building new ones.
											if (totalProbesInAllCelestials > totalSlotsForProbing * neededProbes) {
												log(LogLevel.Warning, LogSender.AutoFarm, $"There should be enough probes in other planets, so avoiding build new ones in {closest.Coordinate.ToString()}");
												continue;
											}

											//If there is no bestOrigin, check if can be a good origin (it has enough resources to build probes)
											var tempCelestial = await UpdatePlanet(closest, UpdateTypes.Constructions);
											if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
												Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
												log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");
												continue;
											}
											await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds));
											tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Productions);
											if (tempCelestial.Productions.Any(p => p.ID == (int) Buildables.EspionageProbe)) {
												log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: Probes already building.");
												continue;
											}

											await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds));
											var buildProbes = neededProbes - celestialProbes[closest.ID];
											var cost = _helpersService.CalcPrice(Buildables.EspionageProbe, (int) buildProbes);
											tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Resources);
											if (tempCelestial.Resources.IsEnoughFor(cost)) {
												bestOrigin = closest;
											}
										}
									}

									if (bestOrigin == null) {
										if (minBackIn.Value != int.MaxValue) {
											int interval = (int) ((1000 * minBackIn.Value) + RandomizeHelper.CalcRandomInterval(IntervalType.LessThanFiveSeconds));
											log(LogLevel.Information, LogSender.AutoFarm, $"Not enough free slots {freeSlots}/{slotsToLeaveFree}. Waiting {TimeSpan.FromMilliseconds(interval)} for probes to return...");
											await Task.Delay(interval);
											bestOrigin = await UpdatePlanet(minBackIn.Key, UpdateTypes.Ships);
											freeSlots++;
										} else {
											log(LogLevel.Information, LogSender.AutoFarm, $"No origin found to spy from. There are not enough probes or enough resources to build them. Using closest celestial as best origin.");
											bestOrigin = closestCelestials.First();
										}
									}

									log(LogLevel.Information, LogSender.AutoFarm, $"Best origin found: {bestOrigin.Name} ({bestOrigin.Coordinate.ToString()})");


									if (freeSlots <= slotsToLeaveFree) {
										userData.slots = await UpdateSlots();
										freeSlots = userData.slots.Free;
									}

									userData.fleets = await UpdateFleets();
									while (freeSlots <= slotsToLeaveFree) {
										// No slots available, wait for first fleet of any mission type to return.
										userData.fleets = await UpdateFleets();
										if (userData.fleets.Any()) {
											int interval = (int) ((1000 * userData.fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.LessThanASecond));
											log(LogLevel.Information, LogSender.AutoFarm, $"Out of fleet slots. Waiting {TimeSpan.FromMilliseconds(interval)} for fleet to return...");
											await Task.Delay(interval);
											userData.slots = await UpdateSlots();
											freeSlots = userData.slots.Free;
										} else {
											log(LogLevel.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
											return;
										}
									}

									if (_helpersService.GetMissionsInProgress(bestOrigin.Coordinate, Missions.Spy, userData.fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate))) {
										log(LogLevel.Warning, LogSender.AutoFarm, $"Probes already on route towards {target.ToString()}.");
										break;
									}
									if (_helpersService.GetMissionsInProgress(bestOrigin.Coordinate, Missions.Attack, userData.fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate) && f.ReturnFlight == false)) {
										log(LogLevel.Warning, LogSender.AutoFarm, $"Attack already on route towards {target.ToString()}.");
										break;
									}

									// If local record indicate not enough espionage probes are available, update record to make sure this is correct.
									if (celestialProbes[bestOrigin.ID] < neededProbes) {
										var tempCelestial = await UpdatePlanet(bestOrigin, UpdateTypes.Ships);
										celestialProbes.Remove(bestOrigin.ID);
										celestialProbes.Add(bestOrigin.ID, tempCelestial.Ships.EspionageProbe);
									}

									if (celestialProbes[bestOrigin.ID] >= neededProbes) {
										Ships ships = new();
										ships.Add(Buildables.EspionageProbe, neededProbes);

										log(LogLevel.Information, LogSender.AutoFarm, $"Spying {target.ToString()} from {bestOrigin.ToString()} with {neededProbes} probes.");

										userData.slots = await UpdateSlots();
										var fleetId = await SendFleet(bestOrigin, ships, target.Celestial.Coordinate, Missions.Spy, Speeds.HundredPercent);
										if (fleetId > (int) SendFleetCode.GenericError) {
											freeSlots--;
											numProbed++;
											celestialProbes[bestOrigin.ID] -= neededProbes;

											if (target.State == FarmState.ProbesRequired || target.State == FarmState.FailedProbesRequired)
												break;

											userData.farmTargets.Remove(target);
											target.State = FarmState.ProbesSent;
											userData.farmTargets.Add(target);

											break;
										} else if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										} else {
											continue;
										}
									} else {
										log(LogLevel.Warning, LogSender.AutoFarm, $"Insufficient probes ({celestialProbes[bestOrigin.ID]}/{neededProbes}).");
										if (SettingsService.IsSettingSet(settings.AutoFarm, "BuildProbes") && settings.AutoFarm.BuildProbes == true) {
											//Check if probes can be built
											var tempCelestial = await UpdatePlanet(bestOrigin, UpdateTypes.Constructions);
											if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
												Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
												log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");
												break;
											}

											tempCelestial = await UpdatePlanet(bestOrigin, UpdateTypes.Productions);
											if (tempCelestial.Productions.Any(p => p.ID == (int) Buildables.EspionageProbe)) {
												log(LogLevel.Information, LogSender.AutoFarm, $"Skipping {tempCelestial.ToString()}: Probes already building.");
												break;
											}

											var buildProbes = neededProbes - celestialProbes[bestOrigin.ID];
											var cost = _helpersService.CalcPrice(Buildables.EspionageProbe, (int) buildProbes);
											tempCelestial = await UpdatePlanet(bestOrigin, UpdateTypes.Resources);
											if (tempCelestial.Resources.IsEnoughFor(cost)) {
												log(LogLevel.Information, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {buildProbes}x{Buildables.EspionageProbe.ToString()}");
											} else {
												var buildableProbes = _helpersService.CalcMaxBuildableNumber(Buildables.EspionageProbe, tempCelestial.Resources);
												log(LogLevel.Warning, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Not enough resources to build {buildProbes}x{Buildables.EspionageProbe.ToString()}. {buildableProbes} will be built instead.");
												buildProbes = buildableProbes;
											}

											try {
												await _ogameService.BuildShips(tempCelestial, Buildables.EspionageProbe, buildProbes);
												tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
												int interval = (int) (_helpersService.CalcProductionTime(Buildables.EspionageProbe, (int) buildProbes, userData.serverData, tempCelestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
												log(LogLevel.Information, LogSender.AutoFarm, $"Production succesfully started. Waiting {TimeSpan.FromMilliseconds(interval)} for build order to finish...");
												await Task.Delay(interval);
											} catch {
												log(LogLevel.Warning, LogSender.AutoFarm, "Unable to start ship production.");
											}
										}
										break;
									}
								}
							}
						}
					} catch (Exception e) {
						log(LogLevel.Debug, LogSender.AutoFarm, $"Exception: {e.Message}");
						log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
						log(LogLevel.Warning, LogSender.AutoFarm, "Unable to parse scan range");
					}

					// Wait for all espionage fleets to return.
					userData.fleets = await UpdateFleets();
					Fleet firstReturning = _helpersService.GetFirstReturningEspionage(userData.fleets);
					if (firstReturning != null) {
						int interval = (int) ((1000 * firstReturning.BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
						log(LogLevel.Information, LogSender.AutoFarm, $"Waiting {TimeSpan.FromMilliseconds(interval)} for probes to return...");
						await Task.Delay(interval);
					}

					log(LogLevel.Information, LogSender.AutoFarm, "Processing espionage reports of found inactives...");

					/// Process reports.
					await AutoFarmProcessReports();

					/// Send attacks.
					List<FarmTarget> attackTargets;
					if (settings.AutoFarm.PreferedResource == "Metal")
						attackTargets = userData.farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(userData.userInfo.Class).Metal).ToList();
					else if (settings.AutoFarm.PreferedResource == "Crystal")
						attackTargets = userData.farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(userData.userInfo.Class).Crystal).ToList();
					else if (settings.AutoFarm.PreferedResource == "Deuterium")
						attackTargets = userData.farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(userData.userInfo.Class).Deuterium).ToList();
					else
						attackTargets = userData.farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(userData.userInfo.Class).TotalResources).ToList();

					if (attackTargets.Count() > 0) {
						log(LogLevel.Information, LogSender.AutoFarm, "Attacking suitable farm targets...");
					} else {
						log(LogLevel.Information, LogSender.AutoFarm, "No suitable targets found.");
						return;
					}

					Buildables cargoShip = Buildables.LargeCargo;
					if (!Enum.TryParse<Buildables>((string) settings.AutoFarm.CargoType, true, out cargoShip)) {
						log(LogLevel.Warning, LogSender.AutoFarm, "Unable to parse cargoShip. Falling back to default LargeCargo");
						cargoShip = Buildables.LargeCargo;
					}
					if (cargoShip == Buildables.Null) {
						log(LogLevel.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip is Null");
						return;
					}
					if (cargoShip == Buildables.EspionageProbe && userData.serverData.ProbeCargo == 0) {
						log(LogLevel.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip set to EspionageProbe, but this universe does not have probe cargo.");
						return;
					}

					userData.researches = await UpdateResearches();
					userData.celestials = await UpdateCelestials();
					int attackTargetsCount = 0;
					decimal lootFuelRatio = SettingsService.IsSettingSet(settings.AutoFarm, "MinLootFuelRatio") ? (decimal) settings.AutoFarm.MinLootFuelRatio : (decimal) 0.0001;
					decimal speed = 0;
					foreach (FarmTarget target in attackTargets) {
						attackTargetsCount++;
						log(LogLevel.Information, LogSender.AutoFarm, $"Attacking target {attackTargetsCount}/{attackTargets.Count()} at {target.Celestial.Coordinate.ToString()} for {target.Report.Loot(userData.userInfo.Class).TransportableResources}.");
						var loot = target.Report.Loot(userData.userInfo.Class);
						var numCargo = _helpersService.CalcShipNumberForPayload(loot, cargoShip, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);
						if (SettingsService.IsSettingSet(settings.AutoFarm, "CargoSurplusPercentage") && (double) settings.AutoFarm.CargoSurplusPercentage > 0) {
							numCargo = (long) Math.Round(numCargo + (numCargo / 100 * (double) settings.AutoFarm.CargoSurplusPercentage), 0);
						}
						var attackingShips = new Ships().Add(cargoShip, numCargo);

						List<Celestial> tempCelestials = (settings.AutoFarm.Origin.Length > 0) ? _helpersService.ParseCelestialsList(settings.AutoFarm.Origin, userData.celestials) : userData.celestials;
						List<Celestial> closestCelestials = tempCelestials
							.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
							.OrderBy(c => _helpersService.CalcDistance(c.Coordinate, target.Celestial.Coordinate, userData.serverData))
							.ToList();

						Celestial fromCelestial = null;
						foreach (var c in closestCelestials) {
							var tempCelestial = await UpdatePlanet(c, UpdateTypes.Ships);
							tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Resources);
							if (tempCelestial.Ships != null && tempCelestial.Ships.GetAmount(cargoShip) >= (numCargo + settings.AutoFarm.MinCargosToKeep)) {
								// TODO Future: If fleet composition is changed, update ships passed to CalcFlightTime.
								speed = 0;
								if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(settings.AutoFarm, "MinLootFuelRatio") && settings.AutoFarm.MinLootFuelRatio != 0) {
									long maxFlightTime = SettingsService.IsSettingSet(settings.AutoFarm, "MaxFlightTime") ? (long) settings.AutoFarm.MaxFlightTime : 86400;
									var optimalSpeed = _helpersService.CalcOptimalFarmSpeed(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(userData.userInfo.Class), lootFuelRatio, maxFlightTime, userData.researches, userData.serverData, userData.userInfo.Class);
									if (optimalSpeed == 0) {
										log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

									} else {
										log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
										speed = optimalSpeed;
									}
								}
								if (speed == 0) {
									if (SettingsService.IsSettingSet(settings.AutoFarm, "FleetSpeed") && settings.AutoFarm.FleetSpeed > 0) {
										speed = (int) settings.AutoFarm.FleetSpeed / 10;
										if (!_helpersService.GetValidSpeedsForClass(userData.userInfo.Class).Any(s => s == speed)) {
											log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
											speed = Speeds.HundredPercent;
										}
									} else {
										speed = Speeds.HundredPercent;
									}
								}
								FleetPrediction prediction = _helpersService.CalcFleetPrediction(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, userData.researches, userData.serverData, userData.userInfo.Class);

								if (
									(
										!SettingsService.IsSettingSet(settings.AutoFarm, "MaxFlightTime") ||
										(long) settings.AutoFarm.MaxFlightTime == 0 ||
										prediction.Time <= (long) settings.AutoFarm.MaxFlightTime
									) &&
									prediction.Fuel <= tempCelestial.Resources.Deuterium
								) {
									fromCelestial = tempCelestial;
									break;
								}
							}
						}

						if (fromCelestial == null) {
							log(LogLevel.Information, LogSender.AutoFarm, $"No origin celestial available near destination {target.Celestial.ToString()} with enough cargo ships.");
							// TODO Future: If prefered cargo ship is not available or not sufficient capacity, combine with other cargo type.
							foreach (var closest in closestCelestials) {
								Celestial tempCelestial = closest;
								tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Ships);
								tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Resources);
								// TODO Future: If fleet composition is changed, update ships passed to CalcFlightTime.
								speed = 0;
								if (SettingsService.IsSettingSet(settings.AutoFarm, "FleetSpeed") && settings.AutoFarm.FleetSpeed > 0) {
									speed = (int) settings.AutoFarm.FleetSpeed / 10;
									if (!_helpersService.GetValidSpeedsForClass(userData.userInfo.Class).Any(s => s == speed)) {
										log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
										speed = Speeds.HundredPercent;
									}
								} else {
									speed = 0;
									if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(settings.AutoFarm, "MinLootFuelRatio") && settings.AutoFarm.MinLootFuelRatio != 0) {
										long maxFlightTime = SettingsService.IsSettingSet(settings.AutoFarm, "MaxFlightTime") ? (long) settings.AutoFarm.MaxFlightTime : 86400;
										var optimalSpeed = _helpersService.CalcOptimalFarmSpeed(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(userData.userInfo.Class), lootFuelRatio, maxFlightTime, userData.researches, userData.serverData, userData.userInfo.Class);
										if (optimalSpeed == 0) {
											log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

										} else {
											log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
											speed = optimalSpeed;
										}
									}
									if (speed == 0) {
										if (SettingsService.IsSettingSet(settings.AutoFarm, "FleetSpeed") && settings.AutoFarm.FleetSpeed > 0) {
											speed = (int) settings.AutoFarm.FleetSpeed / 10;
											if (!_helpersService.GetValidSpeedsForClass(userData.userInfo.Class).Any(s => s == speed)) {
												log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
												speed = Speeds.HundredPercent;
											}
										} else {
											speed = Speeds.HundredPercent;
										}
									}
								}
								FleetPrediction prediction = _helpersService.CalcFleetPrediction(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, userData.researches, userData.serverData, userData.userInfo.Class);

								if (
									tempCelestial.Ships.GetAmount(cargoShip) < numCargo + (long) settings.AutoFarm.MinCargosToKeep &&
									tempCelestial.Resources.Deuterium >= prediction.Fuel &&
									(
										!SettingsService.IsSettingSet(settings.AutoFarm, "MaxFlightTime") ||
										(long) settings.AutoFarm.MaxFlightTime == 0 ||
										prediction.Time <= (long) settings.AutoFarm.MaxFlightTime
									)
								) {
									if (SettingsService.IsSettingSet(settings.AutoFarm, "BuildCargos") && settings.AutoFarm.BuildCargos == true) {
										var neededCargos = numCargo + (long) settings.AutoFarm.MinCargosToKeep - tempCelestial.Ships.GetAmount(cargoShip);
										var cost = _helpersService.CalcPrice(cargoShip, (int) neededCargos);
										if (tempCelestial.Resources.IsEnoughFor(cost)) {
											log(LogLevel.Information, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {neededCargos}x{cargoShip.ToString()}");
										} else {
											var buildableCargos = _helpersService.CalcMaxBuildableNumber(cargoShip, tempCelestial.Resources);
											log(LogLevel.Warning, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{cargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
											neededCargos = buildableCargos;
										}

										try {
											await _ogameService.BuildShips(tempCelestial, cargoShip, neededCargos);
											tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
											int interval = (int) (_helpersService.CalcProductionTime(cargoShip, (int) neededCargos, userData.serverData, tempCelestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
											log(LogLevel.Information, LogSender.AutoFarm, $"Production succesfully started. Waiting {TimeSpan.FromMilliseconds(interval)} for build order to finish...");
											await Task.Delay(interval);
										} catch {
											log(LogLevel.Warning, LogSender.AutoFarm, "Unable to start ship production.");
										}
									}

									if (tempCelestial.Ships.GetAmount(cargoShip) - (long) settings.AutoFarm.MinCargosToKeep < (long) settings.AutoFarm.MinCargosToSend) {
										log(LogLevel.Information, LogSender.AutoFarm, $"Insufficient {cargoShip.ToString()} on {tempCelestial.Coordinate}, require {numCargo + (long) settings.AutoFarm.MinCargosToKeep} {cargoShip.ToString()}.");
										continue;
									}

									numCargo = tempCelestial.Ships.GetAmount(cargoShip) - (long) settings.AutoFarm.MinCargosToKeep;
									fromCelestial = tempCelestial;
									break;
								}
							}
						}

						if (fromCelestial == null) {
							log(LogLevel.Information, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. No suitable origin celestial available near the destination.");
							continue;
						}

						// Only execute update slots if our local copy indicates we have run out.
						if (freeSlots <= slotsToLeaveFree) {
							userData.slots = await UpdateSlots();
							freeSlots = userData.slots.Free;
						}

						while (freeSlots <= slotsToLeaveFree) {
							userData.fleets = await UpdateFleets();
							// No slots free, wait for first fleet to come back.
							if (userData.fleets.Any()) {
								int interval = (int) ((1000 * userData.fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
								if (SettingsService.IsSettingSet(settings.AutoFarm, "MaxWaitTime") && (int) settings.AutoFarm.MaxWaitTime != 0 && interval > (int) settings.AutoFarm.MaxWaitTime) {
									log(LogLevel.Information, LogSender.AutoFarm, $"Out of fleet slots. Time to wait greater than set {(int) settings.AutoFarm.MaxWaitTime} seconds. Stopping autofarm.");
									return;
								} else {
									log(LogLevel.Information, LogSender.AutoFarm, $"Out of fleet slots. Waiting {TimeSpan.FromMilliseconds(interval)} for first fleet to return...");
									await Task.Delay(interval);
									userData.slots = await UpdateSlots();
									freeSlots = userData.slots.Free;
								}
							} else {
								log(LogLevel.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
								return;
							}
						}

						if (userData.slots.Free > slotsToLeaveFree) {
							log(LogLevel.Information, LogSender.AutoFarm, $"Attacking {target.ToString()} from {fromCelestial} with {numCargo} {cargoShip.ToString()}.");
							Ships ships = new();

							speed = 0;
							if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(settings.AutoFarm, "MinLootFuelRatio") && settings.AutoFarm.MinLootFuelRatio != 0) {
								long maxFlightTime = SettingsService.IsSettingSet(settings.AutoFarm, "MaxFlightTime") ? (long) settings.AutoFarm.MaxFlightTime : 86400;
								var optimalSpeed = _helpersService.CalcOptimalFarmSpeed(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(userData.userInfo.Class), lootFuelRatio, maxFlightTime, userData.researches, userData.serverData, userData.userInfo.Class);
								if (optimalSpeed == 0) {
									log(LogLevel.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

								} else {
									log(LogLevel.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
									speed = optimalSpeed;
								}
							}
							if (speed == 0) {
								if (SettingsService.IsSettingSet(settings.AutoFarm, "FleetSpeed") && settings.AutoFarm.FleetSpeed > 0) {
									speed = (int) settings.AutoFarm.FleetSpeed / 10;
									if (!_helpersService.GetValidSpeedsForClass(userData.userInfo.Class).Any(s => s == speed)) {
										log(LogLevel.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
										speed = Speeds.HundredPercent;
									}
								} else {
									speed = Speeds.HundredPercent;
								}
							}

							var fleetId = await SendFleet(fromCelestial, attackingShips, target.Celestial.Coordinate, Missions.Attack, speed);

							if (fleetId > (int) SendFleetCode.GenericError) {
								freeSlots--;
							} else if (fleetId == (int) SendFleetCode.AfterSleepTime) {
								stop = true;
								return;
							}

							userData.farmTargets.Remove(target);
							target.State = FarmState.AttackSent;
							userData.farmTargets.Add(target);
						} else {
							log(LogLevel.Information, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. {userData.slots.Free} free, {slotsToLeaveFree} required.");
							return;
						}
					}
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.AutoFarm, $"AutoFarm Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
			} finally {
				log(LogLevel.Information, LogSender.AutoFarm, $"Attacked targets: {userData.farmTargets.Where(t => t.State == FarmState.AttackSent).Count()}");
				if (!userData.isSleeping) {
					if (stop) {
						log(LogLevel.Information, LogSender.AutoFarm, $"Stopping feature.");
					} else {
						var time = await GetDateTime();
						var interval = RandomizeHelper.CalcRandomInterval((int) settings.AutoFarm.CheckIntervalMin, (int) settings.AutoFarm.CheckIntervalMax);
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoFarmTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.AutoFarm, $"Next autofarm check at {newTime.ToString()}");
						await CheckCelestials();
					}

					xaSem[Feature.AutoFarm].Release();
				}
			}
		}

		/// <summary>
		/// Checks all received espionage reports and updates userData.farmTargets to reflect latest data retrieved from reports.
		/// </summary>
		private async Task AutoFarmProcessReports() {
			// TODO Future: Read espionage reports in separate thread (concurently with probing itself).
			// TODO Future: Check if probes were destroyed, blacklist target if so to avoid additional kills.
			List<EspionageReportSummary> summaryReports = await _ogameService.GetEspionageReports();
			foreach (var summary in summaryReports) {
				if (summary.Type == EspionageReportType.Action)
					continue;

				try {
					var report = await _ogameService.GetEspionageReport(summary.ID);
					if (DateTime.Compare(report.Date.AddMinutes((double) settings.AutoFarm.KeepReportFor), await GetDateTime()) < 0) {
						await _ogameService.DeleteReport(report.ID);
						continue;
					}

					if (userData.farmTargets.Any(t => t.HasCoords(report.Coordinate))) {
						var matchingTarget = userData.farmTargets.Where(t => t.HasCoords(report.Coordinate));
						if (matchingTarget.Count() == 0) {
							// Report received of planet not in userData.farmTargets. If inactive: add, otherwise: ignore.
							if (!report.IsInactive)
								continue;
							// TODO: Get corresponding planet. Add to target list.
							continue;
						}

						var target = matchingTarget.First();
						var newFarmTarget = target;

						if (target.Report != null && DateTime.Compare(report.Date, target.Report.Date) < 0) {
							// Target has a more recent report. Delete report.
							await _ogameService.DeleteReport(report.ID);
							continue;
						}

						newFarmTarget.Report = report;
						if (settings.AutoFarm.PreferedResource == "Metal" && report.Loot(userData.userInfo.Class).Metal > settings.AutoFarm.MinimumResources
							|| settings.AutoFarm.PreferedResource == "Crystal" && report.Loot(userData.userInfo.Class).Crystal > settings.AutoFarm.MinimumResources
							|| settings.AutoFarm.PreferedResource == "Deuterium" && report.Loot(userData.userInfo.Class).Deuterium > settings.AutoFarm.MinimumResources
							|| (settings.AutoFarm.PreferedResource == "" && report.Loot(userData.userInfo.Class).TotalResources > settings.AutoFarm.MinimumResources)) {
							if (!report.HasFleetInformation || !report.HasDefensesInformation) {
								if (target.State == FarmState.ProbesRequired)
									newFarmTarget.State = FarmState.FailedProbesRequired;
								else if (target.State == FarmState.FailedProbesRequired)
									newFarmTarget.State = FarmState.NotSuitable;
								else
									newFarmTarget.State = FarmState.ProbesRequired;

								log(LogLevel.Information, LogSender.AutoFarm, $"Need more probes on {report.Coordinate}. Loot: {report.Loot(userData.userInfo.Class)}");
							} else if (report.IsDefenceless()) {
								newFarmTarget.State = FarmState.AttackPending;
								log(LogLevel.Information, LogSender.AutoFarm, $"Attack pending on {report.Coordinate}. Loot: {report.Loot(userData.userInfo.Class)}");
							} else {
								newFarmTarget.State = FarmState.NotSuitable;
								log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - defences present.");
							}
						} else {
							newFarmTarget.State = FarmState.NotSuitable;
							log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - insufficient loot ({report.Loot(userData.userInfo.Class)})");
						}

						userData.farmTargets.Remove(target);
						userData.farmTargets.Add(newFarmTarget);
					} else {
						log(LogLevel.Information, LogSender.AutoFarm, $"Target {report.Coordinate} not scanned by TBot, ignoring...");
					}
				} catch (Exception e) {
					log(LogLevel.Error, LogSender.AutoFarm, $"AutoFarmProcessReports Exception: {e.Message}");
					log(LogLevel.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
					continue;
				}
			}

			await _ogameService.DeleteAllEspionageReports();
		}

		private async void AutoMine(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogLevel.Information, LogSender.Brain, "Running automine...");

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if (((bool) settings.Brain.Active && (bool) settings.Brain.AutoMine.Active) && (timers.TryGetValue("AutoMineTimer", out Timer value))) {
					Buildings maxBuildings = new() {
						MetalMine = (int) settings.Brain.AutoMine.MaxMetalMine,
						CrystalMine = (int) settings.Brain.AutoMine.MaxCrystalMine,
						DeuteriumSynthesizer = (int) settings.Brain.AutoMine.MaxDeuteriumSynthetizer,
						SolarPlant = (int) settings.Brain.AutoMine.MaxSolarPlant,
						FusionReactor = (int) settings.Brain.AutoMine.MaxFusionReactor,
						MetalStorage = (int) settings.Brain.AutoMine.MaxMetalStorage,
						CrystalStorage = (int) settings.Brain.AutoMine.MaxCrystalStorage,
						DeuteriumTank = (int) settings.Brain.AutoMine.MaxDeuteriumTank
					};
					Facilities maxFacilities = new() {
						RoboticsFactory = (int) settings.Brain.AutoMine.MaxRoboticsFactory,
						Shipyard = (int) settings.Brain.AutoMine.MaxShipyard,
						ResearchLab = (int) settings.Brain.AutoMine.MaxResearchLab,
						MissileSilo = (int) settings.Brain.AutoMine.MaxMissileSilo,
						NaniteFactory = (int) settings.Brain.AutoMine.MaxNaniteFactory,
						Terraformer = (int) settings.Brain.AutoMine.MaxTerraformer,
						SpaceDock = (int) settings.Brain.AutoMine.MaxSpaceDock
					};
					Facilities maxLunarFacilities = new() {
						LunarBase = (int) settings.Brain.AutoMine.MaxLunarBase,
						RoboticsFactory = (int) settings.Brain.AutoMine.MaxLunarRoboticsFactory,
						SensorPhalanx = (int) settings.Brain.AutoMine.MaxSensorPhalanx,
						JumpGate = (int) settings.Brain.AutoMine.MaxJumpGate,
						Shipyard = (int) settings.Brain.AutoMine.MaxLunarShipyard
					};
					AutoMinerSettings autoMinerSettings = new() {
						OptimizeForStart = (bool) settings.Brain.AutoMine.OptimizeForStart,
						PrioritizeRobotsAndNanites = (bool) settings.Brain.AutoMine.PrioritizeRobotsAndNanites,
						MaxDaysOfInvestmentReturn = (float) settings.Brain.AutoMine.MaxDaysOfInvestmentReturn,
						DepositHours = (int) settings.Brain.AutoMine.DepositHours,
						BuildDepositIfFull = (bool) settings.Brain.AutoMine.BuildDepositIfFull,
						DeutToLeaveOnMoons = (int) settings.Brain.AutoMine.DeutToLeaveOnMoons
					};

					List<Celestial> celestialsToExclude = _helpersService.ParseCelestialsList(settings.Brain.AutoMine.Exclude, userData.celestials);
					List<Celestial> celestialsToMine = new();
					if (state == null) {
						foreach (Celestial celestial in userData.celestials.Where(p => p is Planet)) {
							var cel = await UpdatePlanet(celestial, UpdateTypes.Buildings);
							var nextMine = _helpersService.GetNextMineToBuild(cel as Planet, userData.researches, userData.serverData.Speed, 100, 100, 100, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull, true, int.MaxValue);
							var lv = _helpersService.GetNextLevel(cel, nextMine);
							var DOIR = _helpersService.CalcNextDaysOfInvestmentReturn(cel as Planet, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
							log(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Mine: {nextMine.ToString()} lv {lv.ToString()}; DOIR: {DOIR.ToString()}.");
							if (DOIR < userData.nextDOIR || userData.nextDOIR == 0) {
								userData.nextDOIR = DOIR;
							}
							celestialsToMine.Add(cel);
						}
						celestialsToMine = celestialsToMine.OrderBy(cel => _helpersService.CalcNextDaysOfInvestmentReturn(cel as Planet, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull)).ToList();
						celestialsToMine.AddRange(userData.celestials.Where(c => c is Moon));
					} else {
						celestialsToMine.Add(state as Celestial);
					}

					foreach (Celestial celestial in (bool) settings.Brain.AutoMine.RandomOrder ? celestialsToMine.Shuffle().ToList() : celestialsToMine) {
						if (celestialsToExclude.Has(celestial)) {
							log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}

						await AutoMineCelestial(celestial, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
					}
				} else {
					log(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"AutoMine Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					await CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private async Task AutoMineCelestial(Celestial celestial, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			int fleetId = (int) SendFleetCode.GenericError;
			Buildables buildable = Buildables.Null;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			long delayBuilding = 0;
			bool delayProduction = false;
			try {
				log(LogLevel.Information, LogSender.Brain, $"Running AutoMine on {celestial.ToString()}");
				celestial = await UpdatePlanet(celestial, UpdateTypes.Fast);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = await UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
				celestial = await UpdatePlanet(celestial, UpdateTypes.ResourceSettings);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Buildings);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Facilities);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Productions);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Ships);
				if (
					(!SettingsService.IsSettingSet(settings.Brain.AutoMine, "BuildCrawlers") || (bool) settings.Brain.AutoMine.BuildCrawlers) &&
					celestial.Coordinate.Type == Celestials.Planet &&
					userData.userInfo.Class == CharacterClass.Collector &&
					celestial.Facilities.Shipyard >= 5 &&
					userData.researches.CombustionDrive >= 4 &&
					userData.researches.ArmourTechnology >= 4 &&
					userData.researches.LaserTechnology >= 4 &&
					!celestial.Productions.Any(p => p.ID == (int) Buildables.Crawler) &&
					celestial.Constructions.BuildingID != (int) Buildables.Shipyard &&
					celestial.Constructions.BuildingID != (int) Buildables.NaniteFactory &&
					celestial.Ships.Crawler < _helpersService.CalcMaxCrawlers(celestial as Planet, userData.userInfo.Class, userData.staff.Geologist) &&
					_helpersService.CalcOptimalCrawlers(celestial as Planet, userData.userInfo.Class, userData.staff, userData.researches, userData.serverData) > celestial.Ships.Crawler
				) {
					buildable = Buildables.Crawler;
					level = _helpersService.CalcOptimalCrawlers(celestial as Planet, userData.userInfo.Class, userData.staff, userData.researches, userData.serverData);
				} else {
					if (celestial.Fields.Free == 0) {
						log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: not enough fields available.");
						return;
					}
					if (celestial.Constructions.BuildingID != 0) {
						log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a building in production.");
						if (
							celestial is Planet && (
								celestial.Constructions.BuildingID == (int) Buildables.MetalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.CrystalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.DeuteriumSynthesizer
							)
						) {
							var buildingBeingBuilt = (Buildables) celestial.Constructions.BuildingID;

							var levelBeingBuilt = _helpersService.GetNextLevel(celestial, buildingBeingBuilt);
							var DOIR = _helpersService.CalcDaysOfInvestmentReturn(celestial as Planet, buildingBeingBuilt, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
							if (DOIR > userData.lastDOIR) {
								userData.lastDOIR = DOIR;
							}
						}
						delayBuilding = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						return;
					}

					if (celestial is Planet) {

						buildable = _helpersService.GetNextBuildingToBuild(celestial as Planet, userData.researches, maxBuildings, maxFacilities, userData.userInfo.Class, userData.staff, userData.serverData, autoMinerSettings);
						level = _helpersService.GetNextLevel(celestial as Planet, buildable, userData.userInfo.Class == CharacterClass.Collector, userData.staff.Engineer, userData.staff.IsFull);
					} else {
						buildable = _helpersService.GetNextLunarFacilityToBuild(celestial as Moon, userData.researches, maxLunarFacilities);
						level = _helpersService.GetNextLevel(celestial as Moon, buildable, userData.userInfo.Class == CharacterClass.Collector, userData.staff.Engineer, userData.staff.IsFull);
					}
				}

				if (buildable != Buildables.Null && level > 0) {
					log(LogLevel.Information, LogSender.Brain, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
					if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
						float DOIR = _helpersService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
						log(LogLevel.Debug, LogSender.Brain, $"Days of investment return: {Math.Round(DOIR, 2).ToString()} days.");
					}

					Resources xCostBuildable = _helpersService.CalcPrice(buildable, level);
					if (celestial is Moon)
						xCostBuildable.Deuterium += (long) autoMinerSettings.DeutToLeaveOnMoons;

					if (buildable == Buildables.Terraformer) {
						if (xCostBuildable.Energy > celestial.ResourcesProduction.Energy.CurrentProduction) {
							log(LogLevel.Information, LogSender.Brain, $"Not enough energy to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							buildable = Buildables.SolarSatellite;
							level = _helpersService.CalcNeededSolarSatellites(celestial as Planet, xCostBuildable.Energy - celestial.ResourcesProduction.Energy.CurrentProduction, userData.userInfo.Class == CharacterClass.Collector, userData.staff.Engineer, userData.staff.IsFull);
							xCostBuildable = _helpersService.CalcPrice(buildable, level);
						}
					}

					if (celestial.Resources.IsEnoughFor(xCostBuildable)) {
						bool result = false;
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							if (!celestial.HasProduction()) {
								log(LogLevel.Information, LogSender.Brain, $"Building {level.ToString()} x {buildable.ToString()} on {celestial.ToString()}");
								try {
									await _ogameService.BuildShips(celestial, buildable, level);
									result = true;
								} catch { }
							} else {
								log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: There is already a production ongoing.");
								delayProduction = true;
							}
						} else {
							log(LogLevel.Information, LogSender.Brain, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							try {
								await _ogameService.BuildConstruction(celestial, buildable);
								result = true;
							} catch { }
						}

						if (result) {
							if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
								float DOIR = _helpersService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
								if (DOIR > userData.lastDOIR) {
									userData.lastDOIR = DOIR;
								}
							}
							if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
								celestial = await UpdatePlanet(celestial, UpdateTypes.Productions);
								try {
									if (celestial.Productions.First().ID == (int) buildable) {
										started = true;
										log(LogLevel.Information, LogSender.Brain, $"{celestial.Productions.First().Nbr.ToString()}x {buildable.ToString()} succesfully started.");
									} else {
										celestial = await UpdatePlanet(celestial, UpdateTypes.Resources);
										if (celestial.Resources.Energy >= 0) {
											started = true;
											log(LogLevel.Information, LogSender.Brain, $"{level.ToString()}x {buildable.ToString()} succesfully built");
										} else {
											log(LogLevel.Warning, LogSender.Brain, $"Unable to start {level.ToString()}x {buildable.ToString()} construction: an unknown error has occurred");
										}
									}
								} catch {
									started = true;
									log(LogLevel.Information, LogSender.Brain, $"Unable to determine if the production has started.");
								}
							} else {
								celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.BuildingID == (int) buildable) {
									started = true;
									log(LogLevel.Information, LogSender.Brain, "Building succesfully started.");
								} else {
									celestial = await UpdatePlanet(celestial, UpdateTypes.Buildings);
									celestial = await UpdatePlanet(celestial, UpdateTypes.Facilities);
									if (celestial.GetLevel(buildable) != level)
										log(LogLevel.Warning, LogSender.Brain, "Unable to start building construction: an unknown error has occurred");
									else {
										started = true;
										log(LogLevel.Information, LogSender.Brain, "Building succesfully started.");
									}
								}
							}
						} else if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler)
							log(LogLevel.Warning, LogSender.Brain, "Unable to start building construction: a network error has occurred");
					} else {
						if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
							float DOIR = _helpersService.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
							if (DOIR < userData.nextDOIR || userData.nextDOIR == 0) {
								userData.nextDOIR = DOIR;
							}
						}
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							log(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {level.ToString()}x {buildable.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");

						} else {
							log(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
						}
						if ((bool) settings.Brain.AutoMine.Transports.Active) {
							userData.fleets = await UpdateFleets();
							if (!_helpersService.IsThereTransportTowardsCelestial(celestial, userData.fleets)) {
								Celestial origin = userData.celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
										.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
										.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
										.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
										.SingleOrDefault() ?? new() { ID = 0 };
								fleetId = await HandleMinerTransport(origin, celestial, xCostBuildable, buildable, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);

								if (fleetId == (int) SendFleetCode.AfterSleepTime) {
									stop = true;
									return;
								}
								if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
									delay = true;
									return;
								}
							} else {
								log(LogLevel.Information, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
							}
						}
					}
				} else {
					log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build.");
					if (celestial.Coordinate.Type == Celestials.Planet) {
						var nextDOIR = _helpersService.CalcNextDaysOfInvestmentReturn(celestial as Planet, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
						if (
							(celestial as Planet).HasFacilities(maxFacilities) && (
								(celestial as Planet).HasMines(maxBuildings) ||
								nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn
							)
						) {
							if (nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn) {
								var nextMine = _helpersService.GetNextMineToBuild(celestial as Planet, userData.researches, userData.serverData.Speed, 100, 100, 100, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull, autoMinerSettings.OptimizeForStart, float.MaxValue);
								var nexMineLevel = _helpersService.GetNextLevel(celestial, nextMine);
								if (nextDOIR < userData.nextDOIR || userData.nextDOIR == 0) {
									userData.nextDOIR = nextDOIR;
								}
								log(LogLevel.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine.MaxDaysOfInvestmentReturn to at least {Math.Round(nextDOIR, 2, MidpointRounding.ToPositiveInfinity).ToString()}.");
								log(LogLevel.Debug, LogSender.Brain, $"Next mine to build: {nextMine.ToString()} lv {nexMineLevel.ToString()}.");

							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								log(LogLevel.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine mines max levels");
							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								log(LogLevel.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine facilities max levels");
							}
							stop = true;
						}
					} else if (celestial.Coordinate.Type == Celestials.Moon) {
						if ((celestial as Moon).HasLunarFacilities(maxLunarFacilities)) {
							log(LogLevel.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine lunar facilities max levels");
						}
						stop = true;
					}
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"AutoMineCelestial Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await GetDateTime();
				string autoMineTimer = $"AutoMineTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					log(LogLevel.Information, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}.");
					if (timers.TryGetValue($"AutoMineTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					celestial = await UpdatePlanet(celestial, UpdateTypes.Productions);
					celestial = await UpdatePlanet(celestial, UpdateTypes.Facilities);
					log(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await GetDateTime();
					long interval;
					try {
						interval = _helpersService.CalcProductionTime((Buildables) celestial.Productions.First().ID, celestial.Productions.First().Nbr, userData.serverData, celestial.Facilities) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					log(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await GetDateTime();
					userData.fleets = await UpdateFleets();
					long interval;
					try {
						interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (started) {
					long interval = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval > System.Int32.MaxValue ? System.Int32.MaxValue : interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (userData.lastDOIR >= userData.nextDOIR) {
						userData.nextDOIR = 0;
					}
				} else if (delayBuilding > 0) {
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(delayBuilding);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, delayBuilding > System.Int32.MaxValue ? System.Int32.MaxValue : delayBuilding, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else {
					long interval = await CalcAutoMineTimer(celestial, buildable, level, started, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);

					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						userData.fleets = await UpdateFleets();
						var transportfleet = userData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);

					} else {
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
					}

					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (userData.lastDOIR >= userData.nextDOIR) {
						userData.nextDOIR = 0;
					}
					//log(LogLevel.Debug, LogSender.Brain, $"Last DOIR: {Math.Round(userData.lastDOIR, 2)}");
					//log(LogLevel.Debug, LogSender.Brain, $"Next DOIR: {Math.Round(userData.nextDOIR, 2)}");

				}
			}
		}

		private async void LifeformAutoResearch(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogLevel.Information, LogSender.Brain, "Running Lifeform autoresearch...");

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if (((bool) settings.Brain.Active && (bool) settings.Brain.LifeformAutoResearch.Active) && (timers.TryGetValue("LifeformAutoResearchTimer", out Timer value))) {
					AutoMinerSettings autoMinerSettings = new() {
						DeutToLeaveOnMoons = (int) settings.Brain.AutoMine.DeutToLeaveOnMoons
					};
					int maxResearchLevel = (int) settings.Brain.LifeformAutoResearch.MaxResearchLevel;
					List<Celestial> celestialsToMine = new();
					LFBuildings maxLFBuildings = new();
					if (state == null) {
						foreach (Celestial celestial in userData.celestials.Where(p => p is Planet)) {
							var cel = await UpdatePlanet(celestial, UpdateTypes.LFBuildings);
							cel = await UpdatePlanet(celestial, UpdateTypes.LFTechs);
							cel = await UpdatePlanet(celestial, UpdateTypes.Resources);

							if (cel.LFtype == LFTypes.None) {
								log(LogLevel.Information, LogSender.Brain, $"Skipping {cel.ToString()}: No Lifeform active on this planet.");
								continue;
							}
							var nextLFTechToBuild = _helpersService.GetNextLFTechToBuild(cel, maxResearchLevel);
							if (nextLFTechToBuild != LFTechno.None) {
								var level = _helpersService.GetNextLevel(cel, nextLFTechToBuild);
								Resources nextLFTechCost = await _ogameService.GetPrice(nextLFTechToBuild, level);
								var isLessCostLFTechToBuild = await _helpersService.GetLessExpensiveLFTechToBuild(cel, nextLFTechCost, maxResearchLevel);
								if (isLessCostLFTechToBuild != LFTechno.None) {
									level = _helpersService.GetNextLevel(cel, isLessCostLFTechToBuild);
									nextLFTechToBuild = isLessCostLFTechToBuild;
								}

								log(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Lifeform Research: {nextLFTechToBuild.ToString()} lv {level.ToString()}.");
								celestialsToMine.Add(celestial);
							} else {
								log(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: No Next Lifeform technology to build found. All research reached settings MaxResearchLevel ?");
							}

						}
					} else {
						celestialsToMine.Add(state as Celestial);
					}
					foreach (Celestial celestial in celestialsToMine) {
						await LifeformAutoResearchCelestial(celestial);
					}
				} else {
					log(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"Lifeform AutoMine Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					await CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private async Task LifeformAutoResearchCelestial(Celestial celestial) {
			int fleetId = (int) SendFleetCode.GenericError;
			LFTechno buildable = LFTechno.None;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			bool delayProduction = false;
			long delayTime = 0;
			long interval = 0;
			try {
				log(LogLevel.Information, LogSender.Brain, $"Running Lifeform AutoResearch on {celestial.ToString()}");
				celestial = await UpdatePlanet(celestial, UpdateTypes.Fast);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = await UpdatePlanet(celestial, UpdateTypes.LFTechs);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions);

				if (celestial.Constructions.LFResearchID != 0) {
					log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a Lifeform research in production.");
					delayProduction = true;
					delayTime = (long) celestial.Constructions.LFResearchCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					return;
				}
				int maxResearchLevel = (int) settings.Brain.LifeformAutoResearch.MaxResearchLevel;
				if (celestial is Planet) {
					buildable = _helpersService.GetNextLFTechToBuild(celestial, maxResearchLevel);

					if (buildable != LFTechno.None) {
						level = _helpersService.GetNextLevel(celestial, buildable);
						Resources nextLFTechCost = await _ogameService.GetPrice(buildable, level);
						var isLessCostLFTechToBuild = await _helpersService.GetLessExpensiveLFTechToBuild(celestial, nextLFTechCost, maxResearchLevel);
						if (isLessCostLFTechToBuild != LFTechno.None) {
							level = _helpersService.GetNextLevel(celestial, isLessCostLFTechToBuild);
							buildable = isLessCostLFTechToBuild;
						}
						log(LogLevel.Information, LogSender.Brain, $"Best Lifeform Research for {celestial.ToString()}: {buildable.ToString()}");

						Resources xCostBuildable = await _ogameService.GetPrice(buildable, level);

						if (celestial.Resources.IsEnoughFor(xCostBuildable)) {
							bool result = false;
							log(LogLevel.Information, LogSender.Brain, $"Lifeform Research {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							try {
								await _ogameService.BuildCancelable(celestial, (LFTechno) buildable);
								celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.LFResearchID == (int) buildable) {
									started = true;
									log(LogLevel.Information, LogSender.Brain, "Lifeform Research succesfully started.");
								} else {
									celestial = await UpdatePlanet(celestial, UpdateTypes.LFTechs);
									if (celestial.GetLevel(buildable) != level)
										log(LogLevel.Warning, LogSender.Brain, "Unable to start Lifeform Research construction: an unknown error has occurred");
									else {
										started = true;
										log(LogLevel.Information, LogSender.Brain, "Lifeform Research succesfully started.");
									}
								}

							} catch {
								log(LogLevel.Warning, LogSender.Brain, "Unable to start Lifeform Research: a network error has occurred");
							}
						} else {
							log(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.LFBuildingCostResources.ToString()} - Available: {celestial.Resources.LFBuildingCostResources.ToString()}");

							if ((bool) settings.Brain.LifeformAutoResearch.Transports.Active) {
								userData.fleets = await UpdateFleets();
								if (!_helpersService.IsThereTransportTowardsCelestial(celestial, userData.fleets)) {
									Celestial origin = userData.celestials
											.Unique()
											.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
											.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
											.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
											.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
											.SingleOrDefault() ?? new() { ID = 0 };
									fleetId = await HandleMinerTransport(origin, celestial, xCostBuildable);
									if (fleetId == (int) SendFleetCode.AfterSleepTime) {
										stop = true;
										return;
									}
									if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
										delay = true;
										return;
									}
								} else {
									log(LogLevel.Information, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
								}
							}
						}
					} else {
						log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build. All research reached settings MaxResearchLevel ?");
						stop = true;
					}
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"LifeformAutoResearch Celestial Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await GetDateTime();
				string autoMineTimer = $"LifeformAutoResearchTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					log(LogLevel.Information, LogSender.Brain, $"Stopping Lifeform AutoResearch check for {celestial.ToString()}.");
					if (timers.TryGetValue($"LifeformAutoResearchTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					log(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await GetDateTime();
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(delayTime);
					timers.Add(autoMineTimer, new Timer(LifeformAutoResearch, celestial, delayTime, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform Research check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					log(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await GetDateTime();
					userData.fleets = await UpdateFleets();
					try {
						interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.LifeformAutoResearch.CheckIntervalMin, (int) settings.Brain.LifeformAutoResearch.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoResearch, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (started) {
					interval = ((long) celestial.Constructions.LFResearchCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.LifeformAutoResearch.CheckIntervalMin, (int) settings.Brain.LifeformAutoResearch.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoResearch, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");
				} else {
					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						userData.fleets = await UpdateFleets();
						var transportfleet = userData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} else {
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.LifeformAutoMine.CheckIntervalMin, (int) settings.Brain.LifeformAutoMine.CheckIntervalMax);
					}

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoResearch, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}

		private async void LifeformAutoMine(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogLevel.Information, LogSender.Brain, "Running Lifeform automine...");

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if (((bool) settings.Brain.Active && (bool) settings.Brain.LifeformAutoMine.Active) && (timers.TryGetValue("LifeformAutoMineTimer", out Timer value))) {
					AutoMinerSettings autoMinerSettings = new() {
						DeutToLeaveOnMoons = (int) settings.Brain.AutoMine.DeutToLeaveOnMoons
					};

					List<Celestial> celestialsToMine = new();
					LFBuildings maxLFBuildings = new();
					if (state == null) {
						foreach (Celestial celestial in userData.celestials.Where(p => p is Planet)) {
							var cel = await UpdatePlanet(celestial, UpdateTypes.Buildings);

							if ((int) settings.Brain.LifeformAutoMine.StartFromCrystalMineLvl > (int) cel.Buildings.CrystalMine) {
								log(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()} did not reach required CrystalMine level. Skipping..");
								continue;
							}
							int maxTechFactory = (int) settings.Brain.LifeformAutoMine.MaxBaseTechBuilding;
							int maxPopuFactory = (int) settings.Brain.LifeformAutoMine.MaxBaseFoodBuilding;
							int maxFoodFactory = (int) settings.Brain.LifeformAutoMine.MaxBasePopulationBuilding;

							cel = await UpdatePlanet(celestial, UpdateTypes.LFBuildings);
							cel = await UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
							var nextLFBuilding = await _helpersService.GetNextLFBuildingToBuild(cel, maxPopuFactory, maxFoodFactory, maxTechFactory);
							if (nextLFBuilding != LFBuildables.None) {
								var lv = _helpersService.GetNextLevel(celestial, nextLFBuilding);
								log(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Mine: {nextLFBuilding.ToString()} lv {lv.ToString()}.");

								celestialsToMine.Add(celestial);
							} else {
								log(LogLevel.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: No Next Lifeform building to build found.");
							}
						}
					} else {
						celestialsToMine.Add(state as Celestial);
					}

					foreach (Celestial celestial in celestialsToMine) {
						await LifeformAutoMineCelestial(celestial);
					}
				} else {
					log(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"Lifeform AutoMine Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					await CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private async Task LifeformAutoMineCelestial(Celestial celestial) {
			int fleetId = (int) SendFleetCode.GenericError;
			LFBuildables buildable = LFBuildables.None;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			bool delayProduction = false;
			long delayTime = 0;
			long interval = 0;
			try {
				int maxTechFactory = (int) settings.Brain.LifeformAutoMine.MaxBaseTechBuilding;
				int maxPopuFactory = (int) settings.Brain.LifeformAutoMine.MaxBaseFoodBuilding;
				int maxFoodFactory = (int) settings.Brain.LifeformAutoMine.MaxBasePopulationBuilding;

				log(LogLevel.Information, LogSender.Brain, $"Running Lifeform AutoMine on {celestial.ToString()}");
				celestial = await UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = await UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
				celestial = await UpdatePlanet(celestial, UpdateTypes.LFBuildings);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Buildings);
				celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions);

				if (celestial.Constructions.LFBuildingID != 0 || celestial.Constructions.BuildingID == (int) Buildables.RoboticsFactory || celestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
					log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a building (LF, robotic or nanite) in production.");
					delayProduction = true;
					if (celestial.Constructions.LFBuildingID != 0) {
						delayTime = (long) celestial.Constructions.LFBuildingCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} else {
						delayTime = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					}
				}
				if (delayTime == 0) {
					if (celestial is Planet) {
						buildable = await _helpersService.GetNextLFBuildingToBuild(celestial, maxPopuFactory, maxFoodFactory, maxTechFactory);

						if (buildable != LFBuildables.None) {
							level = _helpersService.GetNextLevel(celestial, buildable);
							log(LogLevel.Information, LogSender.Brain, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
							Resources xCostBuildable = await _ogameService.GetPrice(buildable, level);

							if (celestial.Resources.IsBuildable(xCostBuildable)) {
								bool result = false;
								log(LogLevel.Information, LogSender.Brain, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
								try {
									await _ogameService.BuildCancelable(celestial, buildable);
									celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions);
									if (celestial.Constructions.LFBuildingID == (int) buildable) {
										started = true;
										log(LogLevel.Information, LogSender.Brain, "Building succesfully started.");
									} else {
										celestial = await UpdatePlanet(celestial, UpdateTypes.LFBuildings);
										if (celestial.GetLevel(buildable) != level)
											log(LogLevel.Warning, LogSender.Brain, "Unable to start building construction: an unknown error has occurred");
										else {
											started = true;
											log(LogLevel.Information, LogSender.Brain, "Building succesfully started.");
										}
									}

								} catch {
									log(LogLevel.Warning, LogSender.Brain, "Unable to start building construction: a network error has occurred");
								}
							} else {
								log(LogLevel.Information, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.LFBuildingCostResources.ToString()} - Available: {celestial.Resources.LFBuildingCostResources.ToString()}");

								if ((bool) settings.Brain.LifeformAutoMine.Transports.Active) {
									userData.fleets = await UpdateFleets();
									if (!_helpersService.IsThereTransportTowardsCelestial(celestial, userData.fleets)) {
										Celestial origin = userData.celestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
												.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
												.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
												.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
												.SingleOrDefault() ?? new() { ID = 0 };
										fleetId = await HandleMinerTransport(origin, celestial, xCostBuildable);
										if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										}
										if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											delay = true;
											return;
										}
									} else {
										log(LogLevel.Information, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
									}
								}
							}
						} else {
							log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build. Check max Lifeform base building max level in settings file?");
							stop = true;
						}
					}
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"LifeformAutoMine Celestial Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = await GetDateTime();
				string autoMineTimer = $"LifeformAutoMineTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					log(LogLevel.Information, LogSender.Brain, $"Stopping Lifeform AutoMine check for {celestial.ToString()}.");
					if (timers.TryGetValue($"LifeformAutoMineTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					log(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await GetDateTime();
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(delayTime);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, delayTime, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					log(LogLevel.Information, LogSender.Brain, $"Delaying...");
					time = await GetDateTime();
					userData.fleets = await UpdateFleets();
					try {
						interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.LifeformAutoMine.CheckIntervalMin, (int) settings.Brain.LifeformAutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (started) {
					interval = ((long) celestial.Constructions.LFBuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					if (interval == long.MaxValue || interval == long.MinValue)
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.LifeformAutoMine.CheckIntervalMin, (int) settings.Brain.LifeformAutoMine.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (delayTime > 0) {
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(delayTime);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, delayTime, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else {
					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						userData.fleets = await UpdateFleets();
						var transportfleet = userData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					} else {
						interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.LifeformAutoMine.CheckIntervalMin, (int) settings.Brain.LifeformAutoMine.CheckIntervalMax);
					}

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, interval, Timeout.Infinite));
					log(LogLevel.Information, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}

		private async Task<long> CalcAutoMineTimer(Celestial celestial, Buildables buildable, int level, bool started, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			long interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
			try {
				if (celestial.Fields.Free == 0) {
					interval = long.MaxValue;
					log(LogLevel.Information, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}: not enough fields available.");
				}

				celestial = await UpdatePlanet(celestial, UpdateTypes.Constructions);
				if (started) {
					if (buildable == Buildables.SolarSatellite) {
						celestial = await UpdatePlanet(celestial, UpdateTypes.Productions);
						celestial = await UpdatePlanet(celestial, UpdateTypes.Facilities);
						interval = _helpersService.CalcProductionTime(buildable, level, userData.serverData, celestial.Facilities) * 1000;
					} else if (buildable == Buildables.Crawler) {
						interval = (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
					} else {
						if (celestial.HasConstruction())
							interval = ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
						else
							interval = 0;
					}
				} else if (celestial.HasConstruction()) {
					interval = ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				} else {
					celestial = await UpdatePlanet(celestial, UpdateTypes.Buildings);
					celestial = await UpdatePlanet(celestial, UpdateTypes.Facilities);

					if (buildable != Buildables.Null) {
						var price = _helpersService.CalcPrice(buildable, level);
						var productionTime = long.MaxValue;
						var transportTime = long.MaxValue;
						var returningExpoTime = long.MaxValue;
						var transportOriginTime = long.MaxValue;
						var returningExpoOriginTime = long.MaxValue;

						celestial = await UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
						DateTime now = await GetDateTime();
						if (
							celestial.Coordinate.Type == Celestials.Planet &&
							(price.Metal <= celestial.ResourcesProduction.Metal.StorageCapacity || price.Metal <= celestial.Resources.Metal) &&
							(price.Crystal <= celestial.ResourcesProduction.Crystal.StorageCapacity || price.Crystal <= celestial.Resources.Crystal) &&
							(price.Deuterium <= celestial.ResourcesProduction.Deuterium.StorageCapacity || price.Deuterium <= celestial.Resources.Deuterium)
						) {
							var missingResources = price.Difference(celestial.Resources);
							float metProdInASecond = celestial.ResourcesProduction.Metal.CurrentProduction / (float) 3600;
							float cryProdInASecond = celestial.ResourcesProduction.Crystal.CurrentProduction / (float) 3600;
							float deutProdInASecond = celestial.ResourcesProduction.Deuterium.CurrentProduction / (float) 3600;
							if (
								!(
									(missingResources.Metal > 0 && (metProdInASecond == 0 && celestial.Resources.Metal < price.Metal)) ||
									(missingResources.Crystal > 0 && (cryProdInASecond == 0 && celestial.Resources.Crystal < price.Crystal)) ||
									(missingResources.Deuterium > 0 && (deutProdInASecond == 0 && celestial.Resources.Deuterium < price.Deuterium))
								)
							) {
								float metProductionTime = float.IsNaN(missingResources.Metal / metProdInASecond) ? 0.0F : missingResources.Metal / metProdInASecond;
								float cryProductionTime = float.IsNaN(missingResources.Crystal / cryProdInASecond) ? 0.0F : missingResources.Crystal / cryProdInASecond;
								float deutProductionTime = float.IsNaN(missingResources.Deuterium / deutProdInASecond) ? 0.0F : missingResources.Deuterium / deutProdInASecond;
								productionTime = (long) (Math.Round(Math.Max(Math.Max(metProductionTime, cryProductionTime), deutProductionTime), 0) * 1000);
								//log(LogLevel.Debug, LogSender.Brain, $"The required resources will be produced by {now.AddMilliseconds(productionTime).ToString()}");
							}
						}

						userData.fleets = await UpdateFleets();
						var incomingFleets = _helpersService.GetIncomingFleetsWithResources(celestial, userData.fleets);
						if (incomingFleets.Any()) {
							var fleet = incomingFleets.First();
							transportTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
							//log(LogLevel.Debug, LogSender.Brain, $"Next fleet with resources arriving by {now.AddMilliseconds(transportTime).ToString()}");
						}

						var returningExpo = _helpersService.GetFirstReturningExpedition(celestial.Coordinate, userData.fleets);
						if (returningExpo != null) {
							returningExpoTime = (long) (returningExpo.BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
							//log(LogLevel.Debug, LogSender.Brain, $"Next expedition returning by {now.AddMilliseconds(returningExpoTime).ToString()}");
						}

						if ((bool) settings.Brain.AutoMine.Transports.Active) {
							Celestial origin = userData.celestials
									.Unique()
									.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
									.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
									.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
									.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
									.SingleOrDefault() ?? new() { ID = 0 };
							var returningExpoOrigin = _helpersService.GetFirstReturningExpedition(origin.Coordinate, userData.fleets);
							if (returningExpoOrigin != null) {
								returningExpoOriginTime = (long) (returningExpoOrigin.BackIn * 1000) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								//log(LogLevel.Debug, LogSender.Brain, $"Next expedition returning in transport origin celestial by {now.AddMilliseconds(returningExpoOriginTime).ToString()}");
							}

							var incomingOriginFleets = _helpersService.GetIncomingFleetsWithResources(origin, userData.fleets);
							if (incomingOriginFleets.Any()) {
								var fleet = incomingOriginFleets.First();
								transportOriginTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
								//log(LogLevel.Debug, LogSender.Brain, $"Next fleet with resources arriving in transport origin celestial by {DateTime.Now.AddMilliseconds(transportOriginTime).ToString()}");
							}
						}

						productionTime = productionTime < 0 || double.IsNaN(productionTime) ? long.MaxValue : productionTime;
						transportTime = transportTime < 0 || double.IsNaN(transportTime) ? long.MaxValue : transportTime;
						returningExpoTime = returningExpoTime < 0 || double.IsNaN(returningExpoTime) ? long.MaxValue : returningExpoTime;
						returningExpoOriginTime = returningExpoOriginTime < 0 || double.IsNaN(returningExpoOriginTime) ? long.MaxValue : returningExpoOriginTime;
						transportOriginTime = transportOriginTime < 0 || double.IsNaN(transportOriginTime) ? long.MaxValue : transportOriginTime;

						interval = Math.Min(Math.Min(Math.Min(Math.Min(productionTime, transportTime), returningExpoTime), returningExpoOriginTime), transportOriginTime);
					}
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"AutoMineCelestial Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
				return interval + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			}
			if (interval < 0)
				interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			if (interval == long.MaxValue)
				return interval;
			return interval + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
		}

		private async Task<int> HandleMinerTransport(Celestial origin, Celestial destination, Resources resources, Buildables buildable = Buildables.Null, Buildings maxBuildings = null, Facilities maxFacilities = null, Facilities maxLunarFacilities = null, AutoMinerSettings autoMinerSettings = null) {
			try {
				if (origin.ID == destination.ID) {
					log(LogLevel.Warning, LogSender.Brain, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					log(LogLevel.Warning, LogSender.Brain, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = resources.Difference(destination.Resources);
					Resources resToLeave = new(0, 0, 0);
					if ((long) settings.Brain.AutoMine.Transports.DeutToLeave > 0)
						resToLeave.Deuterium = (long) settings.Brain.AutoMine.Transports.DeutToLeave;

					origin = await UpdatePlanet(origin, UpdateTypes.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = await UpdatePlanet(origin, UpdateTypes.Ships);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoMine.Transports.CargoType, true, out preferredShip)) {
							log(LogLevel.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}

						long idealShips = _helpersService.CalcShipNumberForPayload(missingResources, preferredShip, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);
						Ships ships = new();
						Ships tempShips = new();
						tempShips.Add(preferredShip, 1);
						var flightPrediction = _helpersService.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, tempShips, Missions.Transport, Speeds.HundredPercent, userData.researches, userData.serverData, userData.userInfo.Class);
						long flightTime = flightPrediction.Time;
						idealShips = _helpersService.CalcShipNumberForPayload(missingResources, preferredShip, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);
						var availableShips = origin.Ships.GetAmount(preferredShip);
						if (buildable != Buildables.Null) {
							int level = _helpersService.GetNextLevel(destination, buildable);
							long buildTime = _helpersService.CalcProductionTime(buildable, level, userData.serverData, destination.Facilities);
							if (maxBuildings != null && maxFacilities != null && maxLunarFacilities != null && autoMinerSettings != null) {
								var tempCelestial = destination;
								while (flightTime * 2 >= buildTime && idealShips <= availableShips) {
									tempCelestial.SetLevel(buildable, level);
									if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler && buildable != Buildables.SpaceDock) {
										tempCelestial.Fields.Built += 1;
									}
									var nextBuildable = Buildables.Null;
									if (tempCelestial.Coordinate.Type == Celestials.Planet) {
										tempCelestial.Resources.Energy += _helpersService.GetProductionEnergyDelta(buildable, level, userData.researches.EnergyTechnology, 1, userData.userInfo.Class, userData.staff.Engineer, userData.staff.IsFull);
										tempCelestial.ResourcesProduction.Energy.Available += _helpersService.GetProductionEnergyDelta(buildable, level, userData.researches.EnergyTechnology, 1, userData.userInfo.Class, userData.staff.Engineer, userData.staff.IsFull);
										tempCelestial.Resources.Energy -= _helpersService.GetRequiredEnergyDelta(buildable, level);
										tempCelestial.ResourcesProduction.Energy.Available -= _helpersService.GetRequiredEnergyDelta(buildable, level);
										nextBuildable = _helpersService.GetNextBuildingToBuild(tempCelestial as Planet, userData.researches, maxBuildings, maxFacilities, userData.userInfo.Class, userData.staff, userData.serverData, autoMinerSettings, 1);
									} else {
										nextBuildable = _helpersService.GetNextLunarFacilityToBuild(tempCelestial as Moon, userData.researches, maxLunarFacilities);
									}
									if ((nextBuildable != Buildables.Null) && (buildable != Buildables.SolarSatellite)) {
										var nextLevel = _helpersService.GetNextLevel(tempCelestial, nextBuildable);
										var newMissingRes = missingResources.Sum(_helpersService.CalcPrice(nextBuildable, nextLevel));

										if (origin.Resources.IsEnoughFor(newMissingRes, resToLeave)) {
											var newIdealShips = _helpersService.CalcShipNumberForPayload(newMissingRes, preferredShip, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);
											if (newIdealShips <= origin.Ships.GetAmount(preferredShip)) {
												idealShips = newIdealShips;
												missingResources = newMissingRes;
												buildTime += _helpersService.CalcProductionTime(nextBuildable, nextLevel, userData.serverData, tempCelestial.Facilities);
												log(LogLevel.Information, LogSender.Brain, $"Sending resources for {nextBuildable.ToString()} level {nextLevel} too");
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

						if (SettingsService.IsSettingSet(settings.Brain.AutoMine.Transports, "RoundResources") && (bool) settings.Brain.AutoMine.Transports.RoundResources) {
							missingResources = missingResources.Round();
							idealShips = _helpersService.CalcShipNumberForPayload(missingResources, preferredShip, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);
						}

						if (idealShips <= origin.Ships.GetAmount(preferredShip)) {
							ships.Add(preferredShip, idealShips);

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = await UpdatePlanet(destination, UpdateTypes.ResourceSettings);
								destination = await UpdatePlanet(destination, UpdateTypes.Buildings);
								destination = await UpdatePlanet(destination, UpdateTypes.ResourcesProduction);

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
									log(LogLevel.Information, LogSender.Brain, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
									return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, userData.userInfo.Class);
								} else {
									log(LogLevel.Information, LogSender.Brain, "Skipping transport: it is quicker to wait for production.");
									return 0;
								}
							} else {
								log(LogLevel.Information, LogSender.Brain, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
								return await SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, userData.userInfo.Class);
							}
						} else {
							log(LogLevel.Information, LogSender.Brain, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						log(LogLevel.Information, LogSender.Brain, $"Skipping transport: not enough resources in origin. Needed: {missingResources.TransportableResources} - Available: {origin.Resources.TransportableResources}");
						return 0;
					}
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"HandleMinerTransport Exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}

		private async void AutoBuildCargo(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogLevel.Information, LogSender.Brain, "Running autocargo...");

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.Brain.Active && (bool) settings.Brain.AutoCargo.Active) {
					userData.fleets = await UpdateFleets();
					List<Celestial> newCelestials = userData.celestials.ToList();
					List<Celestial> celestialsToExclude = _helpersService.ParseCelestialsList(settings.Brain.AutoCargo.Exclude, userData.celestials);

					foreach (Celestial celestial in (bool) settings.Brain.AutoCargo.RandomOrder ? userData.celestials.Shuffle().ToList() : userData.celestials) {
						if (celestialsToExclude.Has(celestial)) {
							log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}

						var tempCelestial = await UpdatePlanet(celestial, UpdateTypes.Fast);

						userData.fleets = await UpdateFleets();
						if ((bool) settings.Brain.AutoCargo.SkipIfIncomingTransport && _helpersService.IsThereTransportTowardsCelestial(tempCelestial, userData.fleets)) {
							log(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
							continue;
						}

						tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Productions);
						if (tempCelestial.HasProduction()) {
							log(LogLevel.Warning, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is already a production ongoing.");
							foreach (Production production in tempCelestial.Productions) {
								Buildables productionType = (Buildables) production.ID;
								log(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are already in production.");
							}
							continue;
						}
						tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Constructions);
						if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
							Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
							log(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");

						}

						tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Ships);
						tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Resources);
						var capacity = _helpersService.CalcFleetCapacity(tempCelestial.Ships, userData.serverData, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo);
						if (tempCelestial.Coordinate.Type == Celestials.Moon && (bool) settings.Brain.AutoCargo.ExcludeMoons) {
							log(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
							continue;
						}
						long neededCargos;
						Buildables preferredCargoShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoCargo.CargoType, true, out preferredCargoShip)) {
							log(LogLevel.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredCargoShip = Buildables.SmallCargo;
						}
						if (capacity <= tempCelestial.Resources.TotalResources && (bool) settings.Brain.AutoCargo.LimitToCapacity) {
							long difference = tempCelestial.Resources.TotalResources - capacity;
							int oneShipCapacity = _helpersService.CalcShipCapacity(preferredCargoShip, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);
							neededCargos = (long) Math.Round((float) difference / (float) oneShipCapacity, MidpointRounding.ToPositiveInfinity);
							log(LogLevel.Information, LogSender.Brain, $"{difference.ToString("N0")} more capacity is needed, {neededCargos} more {preferredCargoShip.ToString()} are needed.");
						} else {
							neededCargos = (long) settings.Brain.AutoCargo.MaxCargosToKeep - tempCelestial.Ships.GetAmount(preferredCargoShip);
						}
						if (neededCargos > 0) {
							if (neededCargos > (long) settings.Brain.AutoCargo.MaxCargosToBuild)
								neededCargos = (long) settings.Brain.AutoCargo.MaxCargosToBuild;

							if (tempCelestial.Ships.GetAmount(preferredCargoShip) + neededCargos > (long) settings.Brain.AutoCargo.MaxCargosToKeep)
								neededCargos = (long) settings.Brain.AutoCargo.MaxCargosToKeep - tempCelestial.Ships.GetAmount(preferredCargoShip);

							var cost = _helpersService.CalcPrice(preferredCargoShip, (int) neededCargos);
							if (tempCelestial.Resources.IsEnoughFor(cost))
								log(LogLevel.Information, LogSender.Brain, $"{tempCelestial.ToString()}: Building {neededCargos}x{preferredCargoShip.ToString()}");
							else {
								var buildableCargos = _helpersService.CalcMaxBuildableNumber(preferredCargoShip, tempCelestial.Resources);
								log(LogLevel.Information, LogSender.Brain, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{preferredCargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
								neededCargos = buildableCargos;
							}

							if (neededCargos > 0) {
								try {
									await _ogameService.BuildShips(tempCelestial, preferredCargoShip, neededCargos);
									log(LogLevel.Information, LogSender.Brain, "Production succesfully started.");
								} catch {
									log(LogLevel.Warning, LogSender.Brain, "Unable to start ship production.");
								}
							}

							tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Productions);
							foreach (Production production in tempCelestial.Productions) {
								Buildables productionType = (Buildables) production.ID;
								log(LogLevel.Information, LogSender.Brain, $"{tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are in production.");
							}
						} else {
							log(LogLevel.Information, LogSender.Brain, $"{tempCelestial.ToString()}: No ships will be built.");
						}

						newCelestials.Remove(celestial);
						newCelestials.Add(tempCelestial);
					}
					userData.celestials = newCelestials;
				} else {
					log(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Brain, $"Unable to complete autocargo: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogLevel.Information, LogSender.Brain, $"Stopping feature.");
					} else {
						var time = await GetDateTime();
						var interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoCargo.CheckIntervalMin, (int) settings.Brain.AutoCargo.CheckIntervalMax);
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("CapacityTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Brain, $"Next capacity check at {newTime.ToString()}");
						await CheckCelestials();
					}
					xaSem[Feature.Brain].Release();
				}
			}
		}

		public async void AutoRepatriate(object state) {
			bool stop = false;
			bool delay = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogLevel.Information, LogSender.Brain, "Repatriating resources...");

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if (((bool) settings.Brain.Active && (bool) settings.Brain.AutoRepatriate.Active) || (timers.TryGetValue("TelegramCollect", out Timer value))) {
					//log(LogLevel.Information, LogSender.Telegram, $"Telegram collect initated..");
					if (settings.Brain.AutoRepatriate.Target) {
						userData.fleets = await UpdateFleets();
						long TotalMet = 0;
						long TotalCri = 0;
						long TotalDeut = 0;

						Coordinate destinationCoordinate = new(
							(int) settings.Brain.AutoRepatriate.Target.Galaxy,
							(int) settings.Brain.AutoRepatriate.Target.System,
							(int) settings.Brain.AutoRepatriate.Target.Position,
							Enum.Parse<Celestials>((string) settings.Brain.AutoRepatriate.Target.Type)
						);
						List<Celestial> newCelestials = userData.celestials.ToList();
						List<Celestial> celestialsToExclude = _helpersService.ParseCelestialsList(settings.Brain.AutoRepatriate.Exclude, userData.celestials);

						foreach (Celestial celestial in (bool) settings.Brain.AutoRepatriate.RandomOrder ? userData.celestials.Shuffle().ToList() : userData.celestials.OrderBy(c => _helpersService.CalcDistance(c.Coordinate, destinationCoordinate, userData.serverData)).ToList()) {
							if (celestialsToExclude.Has(celestial)) {
								log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
								continue;
							}
							if (celestial.Coordinate.IsSame(destinationCoordinate)) {
								log(LogLevel.Information, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial is the target.");
								continue;
							}

							var tempCelestial = await UpdatePlanet(celestial, UpdateTypes.Fast);

							userData.fleets = await UpdateFleets();
							if ((bool) settings.Brain.AutoRepatriate.SkipIfIncomingTransport && _helpersService.IsThereTransportTowardsCelestial(celestial, userData.fleets) && (!timers.TryGetValue("TelegramCollect", out Timer value2))) {
								log(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
								continue;
							}
							if (celestial.Coordinate.Type == Celestials.Moon && (bool) settings.Brain.AutoRepatriate.ExcludeMoons) {
								log(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
								continue;
							}

							tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Resources);
							tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Ships);

							Buildables preferredShip = Buildables.SmallCargo;
							if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
								log(LogLevel.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
								preferredShip = Buildables.SmallCargo;
							}
							Resources payload = tempCelestial.Resources;

							if ((long) settings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave > 0) {
								if ((bool) settings.Brain.AutoRepatriate.LeaveDeut.OnlyOnMoons) {
									if (tempCelestial.Coordinate.Type == Celestials.Moon) {
										payload = payload.Difference(new(0, 0, (long) settings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave));
									}
								} else {
									payload = payload.Difference(new(0, 0, (long) settings.Brain.AutoRepatriate.LeaveDeut.DeutToLeave));
								}
							}

							if (payload.TotalResources < (long) settings.Brain.AutoRepatriate.MinimumResources || payload.IsEmpty()) {
								log(LogLevel.Information, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: resources under set limit");
								continue;
							}

							long idealShips = _helpersService.CalcShipNumberForPayload(payload, preferredShip, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);

							Ships ships = new();
							if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
								if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
									ships.Add(preferredShip, idealShips);
								} else {
									ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
								}
								payload = _helpersService.CalcMaxTransportableResources(ships, payload, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class, userData.serverData.ProbeCargo);

								if (payload.TotalResources > 0) {
									var fleetId = await SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
									if (fleetId == (int) SendFleetCode.AfterSleepTime) {
										stop = true;
										return;
									}
									if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
										delay = true;
										return;
									}
									TotalMet += payload.Metal;
									TotalCri += payload.Crystal;
									TotalDeut += payload.Deuterium;
								}
							} else {
								log(LogLevel.Warning, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there are no {preferredShip.ToString()}");
							}

							newCelestials.Remove(celestial);
							newCelestials.Add(tempCelestial);
						}
						userData.celestials = newCelestials;
						//send notif only if sent via telegram
						if (timers.TryGetValue("TelegramCollect", out Timer value1)) {
							if ((TotalMet > 0) || (TotalCri > 0) || (TotalDeut > 0)) {
								await SendTelegramMessage($"Resources sent!:\n{TotalMet} Metal\n{TotalCri} Crystal\n{TotalDeut} Deuterium");
							} else {
								await SendTelegramMessage("No resources sent");
							}
						}
					} else {
						log(LogLevel.Warning, LogSender.Brain, "Skipping autorepatriate: unable to parse custom destination");
					}
				} else {
					log(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Brain, $"Unable to complete repatriate: {e.Message}");
				log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					if (timers.TryGetValue("TelegramCollect", out Timer val)) {
						val.Dispose();
						timers.Remove("TelegramCollect");
					} else {
						if (stop) {
							log(LogLevel.Information, LogSender.Brain, $"Stopping feature.");
						} else if (delay) {
							log(LogLevel.Information, LogSender.Brain, $"Delaying...");
							userData.fleets = await UpdateFleets();
							var time = await GetDateTime();
							long interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
							log(LogLevel.Information, LogSender.Brain, $"Next repatriate check at {newTime.ToString()}");
						} else {
							var time = await GetDateTime();
							var interval = RandomizeHelper.CalcRandomInterval((int) settings.Brain.AutoRepatriate.CheckIntervalMin, (int) settings.Brain.AutoRepatriate.CheckIntervalMax);
							if (interval <= 0)
								interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
							log(LogLevel.Information, LogSender.Brain, $"Next repatriate check at {newTime.ToString()}");
						}
					}
					await CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private async Task<int> SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Resources payload = null, CharacterClass playerClass = CharacterClass.NoClass, bool force = false) {
			log(LogLevel.Information, LogSender.FleetScheduler, $"Sending fleet from {origin.Coordinate.ToString()} to {destination.ToString()}. Mission: {mission.ToString()}. Speed: {(speed * 10).ToString()}% Ships: {ships.ToString()}");

			if (playerClass == CharacterClass.NoClass)
				playerClass = userData.userInfo.Class;

			if (!ships.HasMovableFleet()) {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: there are no ships to send");
				return (int) SendFleetCode.GenericError;
			}
			if (mission == Missions.Expedition && ships.IsOnlyProbes()) {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: cannot send an expedition with no ships");
				return (int) SendFleetCode.GenericError;
			}
			if (origin.Coordinate.IsSame(destination)) {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: origin and destination are the same");
				return (int) SendFleetCode.GenericError;
			}
			if (destination.Galaxy <= 0 || destination.Galaxy > userData.serverData.Galaxies || destination.Position <= 0 || destination.Position > 17) {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
				return (int) SendFleetCode.GenericError;
			}
			if (destination.System <= 0 || destination.System > userData.serverData.Systems) {
				if (userData.serverData.DonutGalaxy) {
					if (destination.System <= 0) {
						destination.System += userData.serverData.Systems;
					} else if (destination.System > userData.serverData.Systems) {
						destination.System -= userData.serverData.Systems;
					}
				} else {
					log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
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

			if (!_helpersService.GetValidSpeedsForClass(playerClass).Any(s => s == speed)) {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: speed not available for your class");
				return (int) SendFleetCode.GenericError;
			}
			FleetPrediction fleetPrediction = _helpersService.CalcFleetPrediction(origin.Coordinate, destination, ships, mission, speed, userData.researches, userData.serverData, userData.userInfo.Class);
			log(LogLevel.Debug, LogSender.FleetScheduler, $"Calculated flight time (one-way): {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");

			var flightTime = mission switch {
				Missions.Deploy => fleetPrediction.Time,
				Missions.Expedition => (long) Math.Round((double) (2 * fleetPrediction.Time) + 3600, 0, MidpointRounding.ToPositiveInfinity),
				_ => (long) Math.Round((double) (2 * fleetPrediction.Time), 0, MidpointRounding.ToPositiveInfinity),
			};
			log(LogLevel.Debug, LogSender.FleetScheduler, $"Calculated flight time (full trip): {TimeSpan.FromSeconds(flightTime).ToString()}");
			log(LogLevel.Debug, LogSender.FleetScheduler, $"Calculated flight fuel: {fleetPrediction.Fuel.ToString()}");

			origin = await UpdatePlanet(origin, UpdateTypes.Resources);
			if (origin.Resources.Deuterium < fleetPrediction.Fuel) {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: not enough deuterium!");
				return (int) SendFleetCode.GenericError;
			}

			if (_helpersService.CalcFleetFuelCapacity(ships, userData.serverData, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo) != 0 && _helpersService.CalcFleetFuelCapacity(ships, userData.serverData, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo) < fleetPrediction.Fuel) {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: ships don't have enough fuel capacity!");
				return (int) SendFleetCode.GenericError;
			}

			if (
				(bool) settings.SleepMode.Active &&
				DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep) &&
				DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp) &&
				!force
			) {
				DateTime time = await GetDateTime();

				if (GeneralHelper.ShouldSleep(time, goToSleep, wakeUp)) {
					log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: bed time has passed");
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
				log(LogLevel.Debug, LogSender.FleetScheduler, $"goToSleep : {goToSleep.ToString()}");
				log(LogLevel.Debug, LogSender.FleetScheduler, $"wakeUp : {wakeUp.ToString()}");

				DateTime returnTime = time.AddSeconds(flightTime);
				log(LogLevel.Debug, LogSender.FleetScheduler, $"returnTime : {returnTime.ToString()}");

				if (returnTime >= goToSleep && returnTime <= wakeUp) {
					log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet: it would come back during sleep time");
					return (int) SendFleetCode.AfterSleepTime;
				}
			}
			userData.slots = await UpdateSlots();
			int slotsToLeaveFree = (int) settings.General.SlotsToLeaveFree;
			if (userData.slots.Free == 0) {
				_logger.WriteLog(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet, no slots available");
				return (int) SendFleetCode.NotEnoughSlots;
			} else if (userData.slots.Free > slotsToLeaveFree || force) {
				if (payload == null)
					payload = new();
				try {
					Fleet fleet = await _ogameService.SendFleet(origin, ships, destination, mission, speed, payload);
					log(LogLevel.Information, LogSender.FleetScheduler, "Fleet succesfully sent");
					userData.fleets = await _ogameService.GetFleets();
					userData.slots = await UpdateSlots();
					return fleet.ID;
				} catch (Exception e) {
					log(LogLevel.Error, LogSender.FleetScheduler, $"Unable to send fleet: an exception has occurred: {e.Message}");
					log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
					return (int) SendFleetCode.GenericError;
				}
			} else {
				log(LogLevel.Warning, LogSender.FleetScheduler, "Unable to send fleet, no slots available");
				return (int) SendFleetCode.NotEnoughSlots;
			}
		}

		private async void CancelFleet(Fleet fleet) {
			//log(LogLevel.Information, LogSender.FleetScheduler, $"Recalling fleet id {fleet.ID} originally from {fleet.Origin.ToString()} to {fleet.Destination.ToString()} with mission: {fleet.Mission.ToString()}. Start time: {fleet.StartTime.ToString()} - Arrival time: {fleet.ArrivalTime.ToString()} - Ships: {fleet.Ships.ToString()}");
			userData.slots = await UpdateSlots();
			try {
				await _ogameService.CancelFleet(fleet);
				await Task.Delay((int) IntervalType.AFewSeconds);
				userData.fleets = await UpdateFleets();
				Fleet recalledFleet = userData.fleets.SingleOrDefault(f => f.ID == fleet.ID) ?? new() { ID = (int) SendFleetCode.GenericError };
				if (recalledFleet.ID == (int) SendFleetCode.GenericError) {
					log(LogLevel.Error, LogSender.FleetScheduler, "Unable to recall fleet: an unknon error has occurred, already recalled ?.");
				} else {
					log(LogLevel.Information, LogSender.FleetScheduler, $"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
					if ((bool) settings.Defender.TelegramMessenger.Active) {
						await SendTelegramMessage($"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
					}
					return;
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.FleetScheduler, $"Unable to recall fleet: an exception has occurred: {e.Message}");
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return;
			} finally {
				if (timers.TryGetValue($"RecallTimer-{fleet.ID.ToString()}", out Timer value)) {
					value.Dispose();
					timers.Remove($"RecallTimer-{fleet.ID.ToString()}");
				}

			}
		}

		public async Task TelegramRetireFleet(int fleetId) {
			userData.fleets = await UpdateFleets();
			Fleet ToRecallFleet = userData.fleets.SingleOrDefault(f => f.ID == fleetId) ?? new() { ID = (int) SendFleetCode.GenericError };
			if (ToRecallFleet.ID == (int) SendFleetCode.GenericError) {
				await SendTelegramMessage($"Unable to recall fleet! Already recalled?");
				return;
			}
			RetireFleet(ToRecallFleet);
		}

		private void RetireFleet(object fleet) {
			CancelFleet((Fleet) fleet);
		}


		public async Task TelegramMesgAttacker(string message) {
			userData.attacks = await _ogameService.GetAttacks();
			List<int> playerid = new List<int>();

			foreach (AttackerFleet attack in userData.attacks) {
				if (attack.AttackerID != 0 && !playerid.Any(s => s == attack.AttackerID)) {
					try {
						await _ogameService.SendMessage(attack.AttackerID, message);
						playerid.Add(attack.AttackerID);

						await SendTelegramMessage($"Message succesfully sent to {attack.AttackerName}.");
					} catch {
						await SendTelegramMessage($"Unable to send message.");
					}
				} else {
					await SendTelegramMessage($"Unable send message, AttackerID error.");
				}
			}
		}

		private async void HandleAttack(AttackerFleet attack) {
			if (userData.celestials.Count() == 0) {
				DateTime time = await GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Warning, LogSender.Defender, "Unable to handle attack at the moment: bot is still getting account info.");
				log(LogLevel.Information, LogSender.Defender, $"Next check at {newTime.ToString()}");
				return;
			}

			Celestial attackedCelestial = userData.celestials.Unique().SingleOrDefault(planet => planet.HasCoords(attack.Destination));
			attackedCelestial = await UpdatePlanet(attackedCelestial, UpdateTypes.Ships);

			try {
				if ((settings.Defender.WhiteList as long[]).Any()) {
					foreach (int playerID in (long[]) settings.Defender.WhiteList) {
						if (attack.AttackerID == playerID) {
							log(LogLevel.Information, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: attacker {attack.AttackerName} whitelisted.");
							return;
						}
					}
				}
			} catch {
				log(LogLevel.Warning, LogSender.Defender, "An error has occurred while checking Defender WhiteList");
			}

			try {
				if (attack.MissionType == Missions.MissileAttack) {
					if (
						!SettingsService.IsSettingSet(settings.Defender, "IgnoreMissiles") ||
						(SettingsService.IsSettingSet(settings.Defender, "IgnoreMissiles") && (bool) settings.Defender.IgnoreMissiles)
					) {
						log(LogLevel.Information, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: missiles attack.");
						return;
					}
				}
				if (attack.Ships != null && userData.researches.EspionageTechnology >= 8) {
					if (SettingsService.IsSettingSet(settings.Defender, "IgnoreProbes") && (bool) settings.Defender.IgnoreProbes && attack.IsOnlyProbes()) {
						if (attack.MissionType == Missions.Spy)
							log(LogLevel.Information, LogSender.Defender, "Attacker sent only Probes! Espionage action skipped.");
						else
							log(LogLevel.Information, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: only Espionage Probes.");

						return;
					}
					if (
						(bool) settings.Defender.IgnoreWeakAttack &&
						attack.Ships.GetFleetPoints() < (attackedCelestial.Ships.GetFleetPoints() / (int) settings.Defender.WeakAttackRatio)
					) {
						log(LogLevel.Information, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: weak attack.");
						return;
					}
				} else {
					log(LogLevel.Information, LogSender.Defender, "Unable to detect fleet composition.");
				}
			} catch {
				log(LogLevel.Warning, LogSender.Defender, "An error has occurred while checking attacker fleet composition");
			}

			if ((bool) settings.Defender.TelegramMessenger.Active) {
				await SendTelegramMessage($"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} arriving at {attack.ArrivalTime.ToString()}");
				if (attack.Ships != null)
					await Task.Delay(1000);
				await SendTelegramMessage($"The attack is composed by: {attack.Ships.ToString()}");
			}
			log(LogLevel.Warning, LogSender.Defender, $"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attackedCelestial.ToString()} arriving at {attack.ArrivalTime.ToString()}");
			if (attack.Ships != null)
				await Task.Delay(1000);
			log(LogLevel.Warning, LogSender.Defender, $"The attack is composed by: {attack.Ships.ToString()}");

			if ((bool) settings.Defender.SpyAttacker.Active) {
				userData.slots = await UpdateSlots();
				if (attackedCelestial.Ships.EspionageProbe == 0) {
					log(LogLevel.Warning, LogSender.Defender, "Could not spy attacker: no probes available.");
				} else {
					try {
						Coordinate destination = attack.Origin;
						Ships ships = new() { EspionageProbe = (int) settings.Defender.SpyAttacker.Probes };
						int fleetId = await SendFleet(attackedCelestial, ships, destination, Missions.Spy, Speeds.HundredPercent, new Resources(), userData.userInfo.Class);
						Fleet fleet = userData.fleets.Single(fleet => fleet.ID == fleetId);
						log(LogLevel.Information, LogSender.Defender, $"Spying attacker from {attackedCelestial.ToString()} to {destination.ToString()} with {settings.Defender.SpyAttacker.Probes} probes. Arrival at {fleet.ArrivalTime.ToString()}");
					} catch (Exception e) {
						log(LogLevel.Error, LogSender.Defender, $"Could not spy attacker: an exception has occurred: {e.Message}");
						log(LogLevel.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
					}
				}
			}

			if ((bool) settings.Defender.MessageAttacker.Active) {
				try {
					if (attack.AttackerID != 0) {
						Random random = new();
						string[] messages = settings.Defender.MessageAttacker.Messages;
						string message = messages.ToList().Shuffle().First();
						log(LogLevel.Information, LogSender.Defender, $"Sending message \"{message}\" to attacker {attack.AttackerName}");
						try {
							await _ogameService.SendMessage(attack.AttackerID, message);
							log(LogLevel.Information, LogSender.Defender, "Message succesfully sent.");
						} catch {
							log(LogLevel.Warning, LogSender.Defender, "Unable send message.");
						}
					} else {
						log(LogLevel.Warning, LogSender.Defender, "Unable send message.");
					}

				} catch (Exception e) {
					log(LogLevel.Error, LogSender.Defender, $"Could not message attacker: an exception has occurred: {e.Message}");
					log(LogLevel.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
				}
			}

			if ((bool) settings.Defender.Autofleet.Active) {
				var minFlightTime = attack.ArriveIn + (attack.ArriveIn / 100 * 30) + (RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds) / 1000);
				await AutoFleetSave(attackedCelestial, false, minFlightTime, true);
			}
		}

		private async void HandleExpeditions(object state) {
			bool stop = false;
			bool delay = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Expeditions].WaitAsync();
				long interval;
				DateTime time;
				DateTime newTime;

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Expeditions, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Expeditions].Release();
					return;
				}

				if ((bool) settings.Expeditions.Active && timers.TryGetValue("ExpeditionsTimer", out Timer value)) {
					userData.researches = await UpdateResearches();
					if (userData.researches.Astrophysics == 0) {
						log(LogLevel.Information, LogSender.Expeditions, "Skipping: Astrophysics not yet researched!");
						time = await GetDateTime();
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.AboutHalfAnHour);
						newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
						return;
					}

					userData.slots = await UpdateSlots();
					userData.fleets = await UpdateFleets();
					userData.serverData = await _ogameService.GetServerData();
					int expsToSend;
					if (SettingsService.IsSettingSet(settings.Expeditions, "WaitForAllExpeditions") && (bool) settings.Expeditions.WaitForAllExpeditions) {
						if (userData.slots.ExpInUse == 0)
							expsToSend = userData.slots.ExpTotal;
						else
							expsToSend = 0;
					} else {
						expsToSend = Math.Min(userData.slots.ExpFree, userData.slots.Free);
					}
					log(LogLevel.Debug, LogSender.Expeditions, $"Expedition slot free: {expsToSend}");
					if (SettingsService.IsSettingSet(settings.Expeditions, "WaitForMajorityOfExpeditions") && (bool) settings.Expeditions.WaitForMajorityOfExpeditions) {
						if ((double) expsToSend < Math.Round((double) userData.slots.ExpTotal / 2D, 0, MidpointRounding.ToZero) + 1D) {
							log(LogLevel.Debug, LogSender.Expeditions, $"Majority of expedition already in flight, Skipping...");
							expsToSend = 0;
						}
					}

					if (expsToSend > 0) {
						if (userData.slots.ExpFree > 0) {
							if (userData.slots.Free > 0) {
								List<Celestial> origins = new();
								if (settings.Expeditions.Origin.Length > 0) {
									try {
										foreach (var origin in settings.Expeditions.Origin) {
											Coordinate customOriginCoords = new(
												(int) origin.Galaxy,
												(int) origin.System,
												(int) origin.Position,
												Enum.Parse<Celestials>(origin.Type.ToString())
											);
											Celestial customOrigin = userData.celestials
												.Unique()
												.Single(planet => planet.HasCoords(customOriginCoords));
											customOrigin = await UpdatePlanet(customOrigin, UpdateTypes.Ships);
											origins.Add(customOrigin);
										}
									} catch (Exception e) {
										log(LogLevel.Debug, LogSender.Expeditions, $"Exception: {e.Message}");
										log(LogLevel.Warning, LogSender.Expeditions, $"Stacktrace: {e.StackTrace}");
										log(LogLevel.Warning, LogSender.Expeditions, "Unable to parse custom origin");

										userData.celestials = await UpdatePlanets(UpdateTypes.Ships);
										origins.Add(userData.celestials
											.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
											.OrderByDescending(planet => _helpersService.CalcFleetCapacity(planet.Ships, userData.serverData, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo))
											.First()
										);
									}
								} else {
									userData.celestials = await UpdatePlanets(UpdateTypes.Ships);
									origins.Add(userData.celestials
										.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
										.OrderByDescending(planet => _helpersService.CalcFleetCapacity(planet.Ships, userData.serverData, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo))
										.First()
									);
								}
								if ((bool) settings.Expeditions.RandomizeOrder) {
									origins = origins.Shuffle().ToList();
								}
								foreach (var origin in origins) {
									int expsToSendFromThisOrigin;
									if (origins.Count() >= expsToSend) {
										expsToSendFromThisOrigin = 1;
									} else {
										expsToSendFromThisOrigin = (int) Math.Round((float) expsToSend / (float) origins.Count(), MidpointRounding.ToZero);
										if (origin == origins.Last()) {
											expsToSendFromThisOrigin = (int) Math.Round((float) expsToSend / (float) origins.Count(), MidpointRounding.ToZero) + (expsToSend % origins.Count());
										}
									}
									if (origin.Ships.IsEmpty()) {
										log(LogLevel.Warning, LogSender.Expeditions, "Unable to send expeditions: no ships available");
										continue;
									} else {
										Ships fleet;
										if ((bool) settings.Expeditions.ManualShips.Active) {
											fleet = new(
												(long) settings.Expeditions.ManualShips.Ships.LightFighter,
												(long) settings.Expeditions.ManualShips.Ships.HeavyFighter,
												(long) settings.Expeditions.ManualShips.Ships.Cruiser,
												(long) settings.Expeditions.ManualShips.Ships.Battleship,
												(long) settings.Expeditions.ManualShips.Ships.Battlecruiser,
												(long) settings.Expeditions.ManualShips.Ships.Bomber,
												(long) settings.Expeditions.ManualShips.Ships.Destroyer,
												(long) settings.Expeditions.ManualShips.Ships.Deathstar,
												(long) settings.Expeditions.ManualShips.Ships.SmallCargo,
												(long) settings.Expeditions.ManualShips.Ships.LargeCargo,
												(long) settings.Expeditions.ManualShips.Ships.ColonyShip,
												(long) settings.Expeditions.ManualShips.Ships.Recycler,
												(long) settings.Expeditions.ManualShips.Ships.EspionageProbe,
												0,
												0,
												(long) settings.Expeditions.ManualShips.Ships.Reaper,
												(long) settings.Expeditions.ManualShips.Ships.Pathfinder
											);
											if (!origin.Ships.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
												log(LogLevel.Warning, LogSender.Expeditions, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
												continue;
											}
										} else {
											Buildables primaryShip = Buildables.LargeCargo;
											if (!Enum.TryParse<Buildables>(settings.Expeditions.PrimaryShip.ToString(), true, out primaryShip)) {
												log(LogLevel.Warning, LogSender.Expeditions, "Unable to parse PrimaryShip. Falling back to default LargeCargo");
												primaryShip = Buildables.LargeCargo;
											}
											if (primaryShip == Buildables.Null) {
												log(LogLevel.Warning, LogSender.Expeditions, "Unable to send expeditions: primary ship is Null");
												continue;
											}

											var availableShips = origin.Ships.GetMovableShips();
											if (SettingsService.IsSettingSet(settings.Expeditions, "PrimaryToKeep") && (int) settings.Expeditions.PrimaryToKeep > 0) {
												availableShips.SetAmount(primaryShip, availableShips.GetAmount(primaryShip) - (long) settings.Expeditions.PrimaryToKeep);
											}
											log(LogLevel.Warning, LogSender.Expeditions, $"Available {primaryShip.ToString()} in origin {origin.ToString()}: {availableShips.GetAmount(primaryShip)}");
											fleet = _helpersService.CalcFullExpeditionShips(availableShips, primaryShip, expsToSendFromThisOrigin, userData.serverData, userData.researches, userData.userInfo.Class, userData.serverData.ProbeCargo);
											if (fleet.GetAmount(primaryShip) < (long) settings.Expeditions.MinPrimaryToSend) {
												fleet.SetAmount(primaryShip, (long) settings.Expeditions.MinPrimaryToSend);
												if (!availableShips.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
													log(LogLevel.Warning, LogSender.Expeditions, $"Unable to send expeditions: available {primaryShip.ToString()} in origin {origin.ToString()} under set min number of {(long) settings.Expeditions.MinPrimaryToSend}");
													continue;
												}
											}
											Buildables secondaryShip = Buildables.Null;
											if (!Enum.TryParse<Buildables>(settings.Expeditions.SecondaryShip, true, out secondaryShip)) {
												log(LogLevel.Warning, LogSender.Expeditions, "Unable to parse SecondaryShip. Falling back to default Null");
												secondaryShip = Buildables.Null;
											}
											if (secondaryShip != Buildables.Null) {
												long secondaryToSend = Math.Min(
													(long) Math.Round(
														availableShips.GetAmount(secondaryShip) / (float) expsToSendFromThisOrigin,
														0,
														MidpointRounding.ToZero
													),
													(long) Math.Round(
														fleet.GetAmount(primaryShip) * (float) settings.Expeditions.SecondaryToPrimaryRatio,
														0,
														MidpointRounding.ToZero
													)
												);
												if (secondaryToSend < (long) settings.Expeditions.MinSecondaryToSend) {
													log(LogLevel.Warning, LogSender.Expeditions, $"Unable to send expeditions: available {secondaryShip.ToString()} in origin {origin.ToString()} under set number of {(long) settings.Expeditions.MinSecondaryToSend}");
													continue;
												} else {
													fleet.Add(secondaryShip, secondaryToSend);
													if (!availableShips.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
														log(LogLevel.Warning, LogSender.Expeditions, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
														continue;
													}
												}
											}
										}

										log(LogLevel.Information, LogSender.Expeditions, $"{expsToSendFromThisOrigin.ToString()} expeditions with {fleet.ToString()} will be sent from {origin.ToString()}");
										List<int> syslist = new();
										for (int i = 0; i < expsToSendFromThisOrigin; i++) {
											Coordinate destination;
											if ((bool) settings.Expeditions.SplitExpeditionsBetweenSystems.Active) {
												var rand = new Random();

												int range = (int) settings.Expeditions.SplitExpeditionsBetweenSystems.Range;
												while (expsToSendFromThisOrigin > range * 2)
													range += 1;

												destination = new Coordinate {
													Galaxy = origin.Coordinate.Galaxy,
													System = rand.Next(origin.Coordinate.System - range, origin.Coordinate.System + range + 1),
													Position = 16,
													Type = Celestials.DeepSpace
												};
												destination.System = GeneralHelper.WrapSystem(destination.System);
												while (syslist.Contains(destination.System))
													destination.System = rand.Next(origin.Coordinate.System - range, origin.Coordinate.System + range + 1);
												syslist.Add(destination.System);
											} else {
												destination = new Coordinate {
													Galaxy = origin.Coordinate.Galaxy,
													System = origin.Coordinate.System,
													Position = 16,
													Type = Celestials.DeepSpace
												};
											}
											userData.slots = await UpdateSlots();
											Resources payload = new();
											if ((long) settings.Expeditions.FuelToCarry > 0) {
												payload.Deuterium = (long) settings.Expeditions.FuelToCarry;
											}
											if (userData.slots.ExpFree > 0) {
												var fleetId = await SendFleet(origin, fleet, destination, Missions.Expedition, Speeds.HundredPercent, payload);

												if (fleetId == (int) SendFleetCode.AfterSleepTime) {
													stop = true;
													return;
												}
												if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
													delay = true;
													return;
												}
												await Task.Delay((int) IntervalType.AFewSeconds);
											} else {
												log(LogLevel.Information, LogSender.Expeditions, "Unable to send expeditions: no expedition slots available.");
												break;
											}
										}
									}
								}
							} else {
								log(LogLevel.Warning, LogSender.Expeditions, "Unable to send expeditions: no fleet slots available");
							}
						} else {
							log(LogLevel.Warning, LogSender.Expeditions, "Unable to send expeditions: no expeditions slots available");
						}
					}

					userData.fleets = await UpdateFleets();
					List<Fleet> orderedFleets = userData.fleets
						.Where(fleet => fleet.Mission == Missions.Expedition)
						.ToList();
					if ((bool) settings.Expeditions.WaitForAllExpeditions) {
						orderedFleets = orderedFleets
							.OrderByDescending(fleet => fleet.BackIn)
							.ToList();
					} else {
						orderedFleets = orderedFleets
						.OrderBy(fleet => fleet.BackIn)
							.ToList();
					}

					userData.slots = await UpdateSlots();
					if ((orderedFleets.Count() == 0) || (userData.slots.ExpFree > 0)) {
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.AboutFiveMinutes);
					} else {
						interval = (int) ((1000 * orderedFleets.First().BackIn) + RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo));
					}
					time = await GetDateTime();
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
					log(LogLevel.Information, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
					await CheckCelestials();
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Expeditions, $"HandleExpeditions exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Expeditions, $"Stacktrace: {e.StackTrace}");
				long interval = (long) (RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo));
				var time = await GetDateTime();
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogLevel.Information, LogSender.Expeditions, $"Stopping feature.");
					}
					if (delay) {
						log(LogLevel.Information, LogSender.Expeditions, $"Delaying...");
						var time = await GetDateTime();
						userData.fleets = await UpdateFleets();
						long interval;
						try {
							interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) settings.Expeditions.CheckIntervalMin, (int) settings.Expeditions.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
					}
					await CheckCelestials();
					xaSem[Feature.Expeditions].Release();
				}
			}
		}

		private async void HandleHarvest(object state) {
			bool stop = false;
			bool delay = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Harvest].WaitAsync();

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Harvest, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Harvest].Release();
					return;
				}

				if ((bool) settings.AutoHarvest.Active) {
					log(LogLevel.Information, LogSender.Harvest, "Detecting harvest targets");

					List<Celestial> newCelestials = userData.celestials.ToList();
					var dic = new Dictionary<Coordinate, Celestial>();

					userData.fleets = await UpdateFleets();

					foreach (Planet planet in userData.celestials.Where(c => c is Planet)) {
						Planet tempCelestial = await UpdatePlanet(planet, UpdateTypes.Fast) as Planet;
						tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Ships) as Planet;
						Moon moon = new() {
							Ships = new()
						};

						bool hasMoon = userData.celestials.Count(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) == 1;
						if (hasMoon) {
							moon = userData.celestials.Unique().Single(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) as Moon;
							moon = await UpdatePlanet(moon, UpdateTypes.Ships) as Moon;
						}

						if ((bool) settings.AutoHarvest.HarvestOwnDF) {
							Coordinate dest = new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris);
							if (dic.Keys.Any(d => d.IsSame(dest)))
								continue;
							if (userData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
								continue;
							tempCelestial = await UpdatePlanet(tempCelestial, UpdateTypes.Debris) as Planet;
							if (tempCelestial.Debris != null && tempCelestial.Debris.Resources.TotalResources >= (long) settings.AutoHarvest.MinimumResourcesOwnDF) {
								if (moon.Ships.Recycler >= tempCelestial.Debris.RecyclersNeeded)
									dic.Add(dest, moon);
								else if (moon.Ships.Recycler > 0)
									dic.Add(dest, moon);
								else if (tempCelestial.Ships.Recycler >= tempCelestial.Debris.RecyclersNeeded)
									dic.Add(dest, tempCelestial);
								else if (tempCelestial.Ships.Recycler > 0)
									dic.Add(dest, tempCelestial);
								else
									log(LogLevel.Information, LogSender.Harvest, $"Skipping harvest in {dest.ToString()}: not enough recyclers.");
							}
						}

						if ((bool) settings.AutoHarvest.HarvestDeepSpace) {
							List<Coordinate> destinations = new List<Coordinate>();
							if ((bool) settings.Expeditions.SplitExpeditionsBetweenSystems.Active) {
								int range = (int) settings.Expeditions.SplitExpeditionsBetweenSystems.Range;

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
								if (userData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
									continue;
								ExpeditionDebris expoDebris = (await _ogameService.GetGalaxyInfo(dest)).ExpeditionDebris;
								if (expoDebris != null && expoDebris.Resources.TotalResources >= (long) settings.AutoHarvest.MinimumResourcesDeepSpace) {
									if (moon.Ships.Pathfinder >= expoDebris.PathfindersNeeded)
										dic.Add(dest, moon);
									else if (moon.Ships.Pathfinder > 0)
										dic.Add(dest, moon);
									else if (tempCelestial.Ships.Pathfinder >= expoDebris.PathfindersNeeded)
										dic.Add(dest, tempCelestial);
									else if (tempCelestial.Ships.Pathfinder > 0)
										dic.Add(dest, tempCelestial);
									else
										log(LogLevel.Information, LogSender.Harvest, $"Skipping harvest in {dest.ToString()}: not enough pathfinders.");
								}
							}
						}

						newCelestials.Remove(planet);
						newCelestials.Add(tempCelestial);
					}
					userData.celestials = newCelestials;

					if (dic.Count() == 0)
						log(LogLevel.Information, LogSender.Harvest, "Skipping harvest: there are no fields to harvest.");

					foreach (Coordinate destination in dic.Keys) {
						var fleetId = (int) SendFleetCode.GenericError;
						Celestial origin = dic[destination];
						if (destination.Position == 16) {
							ExpeditionDebris debris = (await _ogameService.GetGalaxyInfo(destination)).ExpeditionDebris;
							long pathfindersToSend = Math.Min(_helpersService.CalcShipNumberForPayload(debris.Resources, Buildables.Pathfinder, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class), origin.Ships.Pathfinder);
							log(LogLevel.Information, LogSender.Harvest, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {pathfindersToSend.ToString()} {Buildables.Pathfinder.ToString()}");
							fleetId = await SendFleet(origin, new Ships { Pathfinder = pathfindersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
						} else {
							if (userData.celestials.Any(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet)))) {
								Debris debris = (userData.celestials.Where(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet))).First() as Planet).Debris;
								long recyclersToSend = Math.Min(_helpersService.CalcShipNumberForPayload(debris.Resources, Buildables.Recycler, userData.researches.HyperspaceTechnology, userData.serverData, userData.userInfo.Class), origin.Ships.Recycler);
								log(LogLevel.Information, LogSender.Harvest, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {recyclersToSend.ToString()} {Buildables.Recycler.ToString()}");
								fleetId = await SendFleet(origin, new Ships { Recycler = recyclersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
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

					userData.fleets = await UpdateFleets();
					long interval;
					if (userData.fleets.Any(f => f.Mission == Missions.Harvest)) {
						interval = (userData.fleets
							.Where(f => f.Mission == Missions.Harvest)
							.OrderBy(f => f.BackIn)
							.First()
							.BackIn ?? 0) * 1000;
					} else {
						interval = (int) RandomizeHelper.CalcRandomInterval((int) settings.AutoHarvest.CheckIntervalMin, (int) settings.AutoHarvest.CheckIntervalMax);
					}
					var time = await GetDateTime();
					if (interval <= 0)
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
					log(LogLevel.Information, LogSender.Harvest, $"Next check at {newTime.ToString()}");
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Harvest, $"HandleHarvest exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Harvest, $"Stacktrace: {e.StackTrace}");
				long interval = (int) RandomizeHelper.CalcRandomInterval((int) settings.AutoHarvest.CheckIntervalMin, (int) settings.AutoHarvest.CheckIntervalMax);
				var time = await GetDateTime();
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.Harvest, $"Next check at {newTime.ToString()}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogLevel.Information, LogSender.Harvest, $"Stopping feature.");
					}
					if (delay) {
						log(LogLevel.Information, LogSender.Harvest, $"Delaying...");
						var time = await GetDateTime();
						userData.fleets = await UpdateFleets();
						long interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Harvest, $"Next check at {newTime.ToString()}");
					}
					await CheckCelestials();
					xaSem[Feature.Harvest].Release();
				}
			}
		}
		private async void HandleColonize(object state) {
			bool stop = false;
			bool delay = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Colonize].WaitAsync();

				if (userData.isSleeping) {
					log(LogLevel.Information, LogSender.Colonize, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Colonize].Release();
					return;
				}

				if ((bool) settings.AutoColonize.Active) {
					long interval = RandomizeHelper.CalcRandomInterval((int) settings.AutoColonize.CheckIntervalMin, (int) settings.AutoColonize.CheckIntervalMax);
					log(LogLevel.Information, LogSender.Colonize, "Checking if a new planet is needed...");

					userData.researches = await UpdateResearches();
					var maxPlanets = _helpersService.CalcMaxPlanets(userData.researches);
					var currentPlanets = userData.celestials.Where(c => c.Coordinate.Type == Celestials.Planet).Count();
					var slotsToLeaveFree = (int) (settings.AutoColonize.SlotsToLeaveFree ?? 0);
					if (currentPlanets + slotsToLeaveFree < maxPlanets) {
						log(LogLevel.Information, LogSender.Colonize, "A new planet is needed.");

						userData.fleets = await UpdateFleets();
						if (userData.fleets.Any(f => f.Mission == Missions.Colonize && !f.ReturnFlight)) {
							log(LogLevel.Information, LogSender.Colonize, "Colony Ship(s) already in flight.");
							interval = userData.fleets
								.OrderBy(f => f.ArriveIn)
								.Last(f => !f.ReturnFlight)
								.ArriveIn * 1000;
						} else {
							Coordinate originCoords = new(
								(int) settings.AutoColonize.Origin.Galaxy,
								(int) settings.AutoColonize.Origin.System,
								(int) settings.AutoColonize.Origin.Position,
								Enum.Parse<Celestials>((string) settings.AutoColonize.Origin.Type)
							);
							Celestial origin = userData.celestials.Single(c => c.HasCoords(originCoords));
							await UpdatePlanet(origin, UpdateTypes.Ships);

							var neededColonizers = maxPlanets - currentPlanets - slotsToLeaveFree;

							if (origin.Ships.ColonyShip >= neededColonizers) {
								List<Coordinate> targets = new();
								foreach (var t in settings.AutoColonize.Targets) {
									Coordinate targetCoords = new(
										(int) t.Galaxy,
										(int) t.System,
										(int) t.Position,
										Celestials.Planet
									);
									targets.Add(targetCoords);
								}
								List<Coordinate> filteredTargets = new();
								foreach (Coordinate t in targets) {
									if (userData.celestials.Any(c => c.HasCoords(t))) {
										continue;
									}
									GalaxyInfo galaxy = await _ogameService.GetGalaxyInfo(t);
									if (galaxy.Planets.Any(p => p != null && p.HasCoords(t))) {
										continue;
									}
									filteredTargets.Add(t);
								}
								if (filteredTargets.Count() > 0) {
									filteredTargets = filteredTargets
										.OrderBy(t => _helpersService.CalcDistance(origin.Coordinate, t, userData.serverData))
										.Take(maxPlanets - currentPlanets)
										.ToList();
									foreach (var target in filteredTargets) {
										Ships ships = new() { ColonyShip = 1 };
										var fleetId = await SendFleet(origin, ships, target, Missions.Colonize, Speeds.HundredPercent);

										if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										}
										if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											delay = true;
											return;
										}
									}
								} else {
									log(LogLevel.Information, LogSender.Colonize, "No valid coordinate in target list.");
								}
							} else {
								await UpdatePlanet(origin, UpdateTypes.Productions);
								await UpdatePlanet(origin, UpdateTypes.Facilities);
								if (origin.Productions.Any()) {
									log(LogLevel.Information, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed. {origin.Productions.Where(p => p.ID == (int) Buildables.ColonyShip).Sum(p => p.Nbr)} colony ship(s) already in production.");
									foreach (var prod in origin.Productions) {
										if (prod == origin.Productions.First()) {
											interval += (int) _helpersService.CalcProductionTime((Buildables) prod.ID, prod.Nbr - 1, userData.serverData, origin.Facilities) * 1000;
										} else {
											interval += (int) _helpersService.CalcProductionTime((Buildables) prod.ID, prod.Nbr, userData.serverData, origin.Facilities) * 1000;
										}
										if (prod.ID == (int) Buildables.ColonyShip) {
											break;
										}
									}
								} else {
									log(LogLevel.Information, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed.");
									await UpdatePlanet(origin, UpdateTypes.Resources);
									var cost = _helpersService.CalcPrice(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip);
									if (origin.Resources.IsEnoughFor(cost)) {
										await UpdatePlanet(origin, UpdateTypes.Constructions);
										if (origin.HasConstruction() && (origin.Constructions.BuildingID == (int) Buildables.Shipyard || origin.Constructions.BuildingID == (int) Buildables.NaniteFactory)) {
											log(LogLevel.Information, LogSender.Colonize, $"Unable to build colony ship: {((Buildables) origin.Constructions.BuildingID).ToString()} is in construction");
											interval = (long) origin.Constructions.BuildingCountdown * (long) 1000;
										} else if (origin.HasProduction()) {
											log(LogLevel.Information, LogSender.Colonize, $"Unable to build colony ship: there is already something in production");
											interval = (long) _helpersService.CalcProductionTime((Buildables) origin.Productions.First().ID, origin.Productions.First().Nbr - 1, userData.serverData, origin.Facilities) * 1000;
										} else if (origin.Facilities.Shipyard >= 4 && userData.researches.ImpulseDrive >= 3) {
											log(LogLevel.Information, LogSender.Colonize, $"Building {neededColonizers - origin.Ships.ColonyShip}....");
											await _ogameService.BuildShips(origin, Buildables.ColonyShip, neededColonizers - origin.Ships.ColonyShip);
											interval = (int) _helpersService.CalcProductionTime(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip, userData.serverData, origin.Facilities) * 1000;
										} else {
											log(LogLevel.Information, LogSender.Colonize, $"Requirements to build colony ship not met");
										}
									} else {
										log(LogLevel.Information, LogSender.Colonize, $"Not enough resources to build {neededColonizers} colony ship(s). Needed: {cost.TransportableResources} - Available: {origin.Resources.TransportableResources}");
									}
								}
							}
						}
					} else {
						log(LogLevel.Information, LogSender.Colonize, "No new planet is needed.");
					}

					DateTime time = await GetDateTime();
					if (interval <= 0) {
						interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					}

					DateTime newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("ColonizeTimer").Change(interval + RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite);
					log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.Colonize, $"HandleColonize exception: {e.Message}");
				log(LogLevel.Warning, LogSender.Colonize, $"Stacktrace: {e.StackTrace}");
				long interval = RandomizeHelper.CalcRandomInterval((int) settings.AutoColonize.CheckIntervalMin, (int) settings.AutoColonize.CheckIntervalMax);
				DateTime time = await GetDateTime();
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("ColonizeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogLevel.Information, LogSender.Colonize, $"Stopping feature.");
					}
					if (delay) {
						log(LogLevel.Information, LogSender.Colonize, $"Delaying...");
						var time = await GetDateTime();
						userData.fleets = await UpdateFleets();
						long interval;
						try {
							interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = RandomizeHelper.CalcRandomInterval((int) settings.AutoColonize.CheckIntervalMin, (int) settings.AutoColonize.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("ColonizeTimer").Change(interval, Timeout.Infinite);
						log(LogLevel.Information, LogSender.Colonize, $"Next check at {newTime}");
					}
					await CheckCelestials();
					xaSem[Feature.Colonize].Release();
				}
			}
		}

		private async void ScheduleFleet(object scheduledFleet) {
			FleetSchedule _scheduledFleet = scheduledFleet as FleetSchedule;
			try {
				await xaSem[Feature.FleetScheduler].WaitAsync();
				userData.scheduledFleets.Add(_scheduledFleet);
				await SendFleet(_scheduledFleet.Origin, _scheduledFleet.Ships, _scheduledFleet.Destination, _scheduledFleet.Mission, _scheduledFleet.Speed, _scheduledFleet.Payload, userData.userInfo.Class);
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"ScheduleFleet exception: {e.Message}");
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
			} finally {
				userData.scheduledFleets = userData.scheduledFleets.OrderBy(f => f.Departure).ToList();
				if (userData.scheduledFleets.Count() > 0) {
					long nextTime = (long) userData.scheduledFleets.FirstOrDefault().Departure.Subtract(await GetDateTime()).TotalMilliseconds;
					timers.GetValueOrDefault("FleetSchedulerTimer").Change(nextTime, Timeout.Infinite);
					log(LogLevel.Information, LogSender.FleetScheduler, $"Next scheduled fleet at {userData.scheduledFleets.First().ToString()}");
				}
				xaSem[Feature.FleetScheduler].Release();
			}
		}

		private async void HandleScheduledFleet(object scheduledFleet) {
			FleetSchedule _scheduledFleet = scheduledFleet as FleetSchedule;
			try {
				await xaSem[Feature.FleetScheduler].WaitAsync();
				await SendFleet(_scheduledFleet.Origin, _scheduledFleet.Ships, _scheduledFleet.Destination, _scheduledFleet.Mission, _scheduledFleet.Speed, _scheduledFleet.Payload, userData.userInfo.Class);
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.FleetScheduler, $"HandleScheduledFleet exception: {e.Message}");
				log(LogLevel.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
			} finally {
				userData.scheduledFleets.Remove(_scheduledFleet);
				userData.scheduledFleets = userData.scheduledFleets.OrderBy(f => f.Departure).ToList();
				if (userData.scheduledFleets.Count() > 0) {
					long nextTime = (long) userData.scheduledFleets.FirstOrDefault().Departure.Subtract(await GetDateTime()).TotalMilliseconds;
					timers.GetValueOrDefault("FleetSchedulerTimer").Change(nextTime, Timeout.Infinite);
					log(LogLevel.Information, LogSender.FleetScheduler, $"Next scheduled fleet at {userData.scheduledFleets.First().ToString()}");
				}
				xaSem[Feature.FleetScheduler].Release();
			}
		}
	}
}
