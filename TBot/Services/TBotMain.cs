using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using Tbot.Includes;
using Tbot.Model;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using static System.Reflection.Metadata.BlobBuilder;

namespace Tbot.Services {
	class TBotMain : IEquatable<TBotMain>, IAsyncDisposable {
		private OgamedService ogamedService;
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

		public string settingsPath { get; private set; } = "";
		public string instanceAlias { get; private set; } = "Main";

		private dynamic settings;
		private TelegramMessenger telegramMessenger;

		public TBotMain(string settingPath, string alias, TelegramMessenger telegramHandler) {
			settingsPath = settingPath;
			instanceAlias = alias;
			settings = SettingsService.GetSettings(settingPath);

			telegramMessenger = telegramHandler;
		}

		public async Task<bool> init() {
			Credentials credentials = new() {
				Universe = ((string) settings.Credentials.Universe).FirstCharToUpper(),
				Username = (string) settings.Credentials.Email,
				Password = (string) settings.Credentials.Password,
				Language = ((string) settings.Credentials.Language).ToLower(),
				IsLobbyPioneers = (bool) settings.Credentials.LobbyPioneers,
				BasicAuthUsername = (string) settings.Credentials.BasicAuth.Username,
				BasicAuthPassword = (string) settings.Credentials.BasicAuth.Password
			};

			try {
				string host = (string) settings.General.Host ?? "localhost";
				string port = (string) settings.General.Port ?? "8080";
				if (!OgamedService.IsPortAvailable(host, int.Parse(port))) {
					throw new Exception("Port " + port + " is not available");
				}
				
				string captchaKey = (string) settings.General.CaptchaAPIKey ?? "";
				ProxySettings proxy = new();
				string cookiesPath = "cookies" + (string) settings.Credentials.Email + ".txt";

				if ((bool) settings.General.Proxy.Enabled && (string) settings.General.Proxy.Address != "") {
					log(LogType.Info, LogSender.Tbot, "Initializing proxy");
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
						log(LogType.Warning, LogSender.Tbot, "Unable to initialize proxy: unsupported proxy type");
						log(LogType.Warning, LogSender.Tbot, "Press enter to continue");
					}
				}

				if (SettingsService.IsSettingSet(settings.General, "CookiesPath") && (string) settings.General.CookiesPath != "") {
					// Cookies are defined relative to the settings file
					cookiesPath = Path.Combine(Path.GetDirectoryName(settingsPath), (string) settings.General.CookiesPath);
				}

				ogamedService = new OgamedService(credentials, (string) host, int.Parse(port), (string) captchaKey, proxy, cookiesPath);
			} catch (Exception e) {
				log(LogType.Error, LogSender.Tbot, $"Unable to start ogamed: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			}

			ogamedService.SetUserAgent((string) settings.General.UserAgent);
			await Task.Delay(Helpers.CalcRandomInterval(IntervalType.LessThanASecond));


			loggedIn = false;
			loggedIn = ogamedService.Login(out string loginErrMessage);
			await Task.Delay(Helpers.CalcRandomInterval(IntervalType.AFewSeconds));

			if (!loggedIn) {
				log(LogType.Warning, LogSender.Tbot, $"Unable to login (\"{loginErrMessage}\"). Checking captcha...");
				var captchaChallenge = ogamedService.GetCaptchaChallenge();
				if (captchaChallenge.Id == "") {
					log(LogType.Warning, LogSender.Tbot, "No captcha found. Unable to login.");
					log(LogType.Warning, LogSender.Tbot, "Please check your credentials, language and universe name.");
					log(LogType.Warning, LogSender.Tbot, "If your credentials are correct try refreshing your IP address.");
					log(LogType.Warning, LogSender.Tbot, "If you are using a proxy, a VPN or hosting TBot on a VPS, be warned that Ogame blocks datacenters' IPs. You probably need a residential proxy.");
				} else {
					log(LogType.Info, LogSender.Tbot, "Trying to solve captcha...");
					int answer = 0;
					if (captchaChallenge.Icons != "" && captchaChallenge.Question != "" && captchaChallenge.Icons != null && captchaChallenge.Question != null) {
						answer = OgameCaptchaSolver.GetCapcthaSolution(captchaChallenge.Icons, captchaChallenge.Question);
					}
					ogamedService.SolveCaptcha(captchaChallenge.Id, answer);
					await Task.Delay(Helpers.CalcRandomInterval(IntervalType.AFewSeconds));
					loggedIn = ogamedService.Login(out loginErrMessage);
				}
			}

			if (loggedIn) {
				log(LogType.Info, LogSender.Tbot, "Logged in!");
				userData.serverInfo = UpdateServerInfo();
				userData.serverData = UpdateServerData();
				userData.userInfo = UpdateUserInfo();
				userData.staff = UpdateStaff();

				log(LogType.Info, LogSender.Tbot, $"Server time: {GetDateTime().ToString()}");

				log(LogType.Info, LogSender.Tbot, $"Player name: {userData.userInfo.PlayerName}");
				log(LogType.Info, LogSender.Tbot, $"Player class: {userData.userInfo.Class.ToString()}");
				log(LogType.Info, LogSender.Tbot, $"Player rank: {userData.userInfo.Rank}");
				log(LogType.Info, LogSender.Tbot, $"Player points: {userData.userInfo.Points}");
				log(LogType.Info, LogSender.Tbot, $"Player honour points: {userData.userInfo.HonourPoints}");

				if (!ogamedService.IsVacationMode()) {
					if (telegramMessenger != null) {
						telegramMessenger.AddTBotInstance(this);
					}
					userData.lastDOIR = 0;
					userData.nextDOIR = 0;

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

					features = new();
					InitializeFeatures(new List<Feature>() {
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
						Feature.Colonize,
						Feature.Harvest,
						Feature.FleetScheduler,
						Feature.SleepMode,
					});


					log(LogType.Info, LogSender.Tbot, "Initializing data...");
					userData.celestials = GetPlanets();
					userData.researches = UpdateResearches();
					userData.scheduledFleets = new();
					userData.farmTargets = new();
					// UpdateTitle(false);

					if (userData.celestials.Count == 1) {
						await EditSettings(userData.celestials.First());
						settings = SettingsService.GetSettings(settingsPath);
					}

					log(LogType.Info, LogSender.Tbot, "Initializing features...");
					InitializeSleepMode();

					// Up and running. Lets initialize notification for settings file
					settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, settingsPath);
				} else {
					log(LogType.Warning, LogSender.Tbot, "Account in vacation mode");
					loggedIn = false;
					ogamedService.Logout();
					return false;
				}

				return true;
			} else {
				log(LogType.Warning, LogSender.Tbot, $"Unable to login. (\"{loginErrMessage}\")");
				return false;
			}
		}

		public async ValueTask DisposeAsync() {
			log(LogType.Info, LogSender.Tbot, "Deinitializing instance...");

			settingsWatcher.deinitWatch();

			if (telegramMessenger != null) {
				log(LogType.Info, LogSender.Tbot, "Removing instance from telegram...");
				telegramMessenger.RemoveTBotInstance(this);
			}

			foreach (var sem in xaSem) {
				log(LogType.Info, LogSender.Tbot, $"Deinitializing feature {sem.Key.ToString()}");
				await sem.Value.WaitAsync();
			}

			log(LogType.Info, LogSender.Tbot, "Deinitializing timers...");
			foreach (KeyValuePair<string, Timer> entry in timers) {
				log(LogType.Info, LogSender.Tbot, $"Disposing timer \"{entry.Key}\"");
				entry.Value.Dispose();
			}
			timers.Clear();

			log(LogType.Info, LogSender.Tbot, "Deinitializing ogamed...");
			if (ogamedService != null) {
				if (loggedIn) {
					loggedIn = false;
					log(LogType.Info, LogSender.Tbot, "Logging out");
					ogamedService.Logout();
				}

				log(LogType.Info, LogSender.Tbot, "Killing ogamed instance");
				ogamedService.KillOgamedExecutable();
				ogamedService = null;
			}

			log(LogType.Info, LogSender.Tbot, "Deinitialization completed");

			GC.SuppressFinalize(this);
		}

		public bool Equals(TBotMain other) {
			if (other == null)
				return false;
			return object.ReferenceEquals(this, other);
		}

		private void log(LogType type, LogSender sender, string format) {
			if(loggedIn && (userData.userInfo != null) && (userData.serverData != null) )
				LoggerService.Logger.WriteLog(type, sender, $"[{userData.userInfo.PlayerName}@{userData.serverData.Name}] {format}");
			else
				LoggerService.Logger.WriteLog(type, sender, $"[{instanceAlias}] {format}");
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

		public void WaitFeature() {
			Task.WaitAll(
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

			log(LogType.Info, LogSender.Tbot, "Settings file change detected! Waiting workers to complete ongoing activities...");

			List<Feature> featuresToHandle = new List<Feature>{
				Feature.Defender,
				Feature.Brain,
				Feature.Expeditions,
				Feature.Harvest,
				Feature.Colonize,
				Feature.AutoFarm,
				Feature.SleepMode
			};

			// Wait on features to be locked
			foreach(var feature in featuresToHandle) {
				log(LogType.Info, LogSender.Tbot, $"Waiting on feature {feature.ToString()}...");
				await xaSem[feature].WaitAsync();
				log(LogType.Info, LogSender.Tbot, $"Feature {feature.ToString()} locked for settings reload!");
			}

			log(LogType.Info, LogSender.Tbot, "Reloading Settings file");
			settings = SettingsService.GetSettings(settingsPath);

			// Release features lock!
			foreach (var feature in featuresToHandle) {
				log(LogType.Info, LogSender.Tbot, $"Unlocking feature {feature.ToString()}...");
				xaSem[feature].Release();
				log(LogType.Info, LogSender.Tbot, $"Feature {feature.ToString()} unlocked!");
			}

			InitializeSleepMode();
		}

		public DateTime GetDateTime() {
			try {
				DateTime dateTime = ogamedService.GetServerTime();
				if (dateTime.Kind == DateTimeKind.Utc)
					return dateTime.ToLocalTime();
				else
					return dateTime;
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"GetDateTime() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				var fallback = DateTime.Now;
				if (fallback.Kind == DateTimeKind.Utc)
					return fallback.ToLocalTime();
				else
					return fallback;
			}
		}

		private Slots UpdateSlots() {
			try {
				return ogamedService.GetSlots();
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdateSlots() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private List<Fleet> UpdateFleets() {
			try {
				return ogamedService.GetFleets();
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdateFleets() Exception: {e.Message}");
				log(LogType.Debug, LogSender.Tbot, $"UpdateFleets() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private List<GalaxyInfo> UpdateGalaxyInfos() {
			try {
				List<GalaxyInfo> galaxyInfos = new();
				Planet newPlanet = new();
				List<Celestial> newCelestials = userData.celestials.ToList();
				foreach (Planet planet in userData.celestials.Where(p => p is Planet)) {
					newPlanet = planet;
					var gi = ogamedService.GetGalaxyInfo(planet.Coordinate);
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
				log(LogType.Debug, LogSender.Tbot, $"UpdateGalaxyInfos() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private ServerData UpdateServerData() {
			try {
				return ogamedService.GetServerData();
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdateServerData() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private Server UpdateServerInfo() {
			try {
				return ogamedService.GetServerInfo();
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdateServerInfo() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private UserInfo UpdateUserInfo() {
			try {
				UserInfo user = ogamedService.GetUserInfo();
				user.Class = ogamedService.GetUserClass();
				return user;
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdateUserInfo() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
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

		public List<Celestial> UpdateCelestials() {
			try {
				return ogamedService.GetCelestials();
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdateCelestials() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return userData.celestials ?? new();
			}
		}

		private Researches UpdateResearches() {
			try {
				return ogamedService.GetResearches();
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdateResearches() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private Staff UpdateStaff() {
			try {
				return ogamedService.GetStaff();
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdateStaff() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private List<Celestial> GetPlanets() {
			List<Celestial> localPlanets = userData.celestials ?? new();
			try {
				List<Celestial> ogamedPlanets = ogamedService.GetCelestials();
				if (localPlanets.Count() == 0 || ogamedPlanets.Count() != userData.celestials.Count) {
					localPlanets = ogamedPlanets.ToList();
				}
				return localPlanets;
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"GetPlanets() Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return localPlanets;
			}
		}

		private List<Celestial> UpdatePlanets(UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogType.Info, LogSender.Tbot, $"Updating userData.celestials... Mode: {UpdateTypes.ToString()}");
			List<Celestial> localPlanets = GetPlanets();
			List<Celestial> newPlanets = new();
			try {
				foreach (Celestial planet in localPlanets) {
					newPlanets.Add(UpdatePlanet(planet, UpdateTypes));
				}
				return newPlanets;
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"UpdatePlanets({UpdateTypes.ToString()}) Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return newPlanets;
			}
		}

		private Celestial UpdatePlanet(Celestial planet, UpdateTypes UpdateTypes = UpdateTypes.Full) {
			// log(LogType.Info, LogSender.Tbot, $"Updating {planet.ToString()}. Mode: {UpdateTypes.ToString()}");
			try {
				switch (UpdateTypes) {
					case UpdateTypes.Fast:
						planet = ogamedService.GetCelestial(planet);
						break;
					case UpdateTypes.Resources:
						planet.Resources = ogamedService.GetResources(planet);
						break;
					case UpdateTypes.Buildings:
						planet.Buildings = ogamedService.GetBuildings(planet);
						break;
					case UpdateTypes.LFBuildings:
						planet.LFBuildings = ogamedService.GetLFBuildings(planet);
						planet.LFtype = planet.SetLFType();
						break;
					case UpdateTypes.LFTechs:
						planet.LFTechs = ogamedService.GetLFTechs(planet);
						break;
					case UpdateTypes.Ships:
						planet.Ships = ogamedService.GetShips(planet);
						break;
					case UpdateTypes.Facilities:
						planet.Facilities = ogamedService.GetFacilities(planet);
						break;
					case UpdateTypes.Defences:
						planet.Defences = ogamedService.GetDefences(planet);
						break;
					case UpdateTypes.Productions:
						planet.Productions = ogamedService.GetProductions(planet);
						break;
					case UpdateTypes.Constructions:
						planet.Constructions = ogamedService.GetConstructions(planet);
						break;
					case UpdateTypes.ResourceSettings:
						if (planet is Planet) {
							planet.ResourceSettings = ogamedService.GetResourceSettings(planet as Planet);
						}
						break;
					case UpdateTypes.ResourcesProduction:
						if (planet is Planet) {
							planet.ResourcesProduction = ogamedService.GetResourcesProduction(planet as Planet);
						}
						break;
					case UpdateTypes.Techs:
						var techs = ogamedService.GetTechs(planet);
						planet.Defences = techs.defenses;
						planet.Facilities = techs.facilities;
						planet.Ships = techs.ships;
						planet.Buildings = techs.supplies;
						break;
					case UpdateTypes.Debris:
						if (planet is Moon)
							break;
						var galaxyInfo = ogamedService.GetGalaxyInfo(planet.Coordinate);
						var thisPlanetGalaxyInfo = galaxyInfo.Planets.Single(p => p != null && p.Coordinate.IsSame(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet)));
						planet.Debris = thisPlanetGalaxyInfo.Debris;
						break;
					case UpdateTypes.Full:
					default:
						planet.Resources = ogamedService.GetResources(planet);
						planet.Productions = ogamedService.GetProductions(planet);
						planet.Constructions = ogamedService.GetConstructions(planet);
						if (planet is Planet) {
							planet.ResourceSettings = ogamedService.GetResourceSettings(planet as Planet);
							planet.ResourcesProduction = ogamedService.GetResourcesProduction(planet as Planet);
						}
						planet.Buildings = ogamedService.GetBuildings(planet);
						planet.Facilities = ogamedService.GetFacilities(planet);
						planet.Ships = ogamedService.GetShips(planet);
						planet.Defences = ogamedService.GetDefences(planet);
						break;
				}
			} catch (Exception e) {
				log(LogType.Debug, LogSender.Tbot, $"Exception: {e.Message}");
				log(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				log(LogType.Warning, LogSender.Tbot, $"An error has occurred with update {UpdateTypes.ToString()}. Skipping update");
			}
			return planet;
		}

		private void CheckCelestials() {
			try {
				if (!userData.isSleeping) {
					var newCelestials = UpdateCelestials();
					if (userData.celestials.Count() != newCelestials.Count) {
						userData.celestials = newCelestials.Unique().ToList();
						if (userData.celestials.Count() > newCelestials.Count) {
							log(LogType.Warning, LogSender.Tbot, "Less userData.celestials than last check detected!!");
						} else {
							log(LogType.Info, LogSender.Tbot, "More userData.celestials than last check detected");
						}
						InitializeSleepMode();
					}
				}
			} catch {
				userData.celestials = userData.celestials.Unique().ToList();
			}
		}

		public void InitializeDefender() {
			log(LogType.Info, LogSender.Tbot, "Initializing defender...");
			StopDefender(false);
			timers.Add("DefenderTimer", new Timer(Defender, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		public void StopDefender(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping defender...");
			if (timers.TryGetValue("DefenderTimer", out Timer value))
				value.Dispose();
			timers.Remove("DefenderTimer");
		}

		private void InitializeBrainAutoCargo() {
			log(LogType.Info, LogSender.Tbot, "Initializing autocargo...");
			StopBrainAutoCargo(false);
			timers.Add("CapacityTimer", new Timer(AutoBuildCargo, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Timeout.Infinite));
		}

		private void StopBrainAutoCargo(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping autocargo...");
			if (timers.TryGetValue("CapacityTimer", out Timer value))
				value.Dispose();
			timers.Remove("CapacityTimer");
		}

		public void InitializeBrainRepatriate() {
			log(LogType.Info, LogSender.Tbot, "Initializing repatriate...");
			StopBrainRepatriate(false);
			if (!timers.TryGetValue("RepatriateTimer", out Timer value))
				timers.Add("RepatriateTimer", new Timer(AutoRepatriate, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		public void StopBrainRepatriate(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping repatriate...");
			if (timers.TryGetValue("RepatriateTimer", out Timer value))
				value.Dispose();
			timers.Remove("RepatriateTimer");
		}

		public void InitializeBrainAutoMine() {
			log(LogType.Info, LogSender.Tbot, "Initializing automine...");
			StopBrainAutoMine(false);
			timers.Add("AutoMineTimer", new Timer(AutoMine, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		public void StopBrainAutoMine(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping automine...");
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
			log(LogType.Info, LogSender.Tbot, "Initializing Lifeform autoMine...");
			StopBrainLifeformAutoMine(false);
			timers.Add("LifeformAutoMineTimer", new Timer(LifeformAutoMine, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		public void StopBrainLifeformAutoMine(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping Lifeform autoMine...");
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
			log(LogType.Info, LogSender.Tbot, "Initializing Lifeform autoResearch...");
			StopBrainLifeformAutoResearch(false);
			timers.Add("LifeformAutoResearchTimer", new Timer(LifeformAutoResearch, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		public void StopBrainLifeformAutoResearch(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping Lifeform autoResearch...");
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
			log(LogType.Info, LogSender.Tbot, "Initializing offer of the day...");
			StopBrainOfferOfTheDay(false);
			timers.Add("OfferOfTheDayTimer", new Timer(BuyOfferOfTheDay, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private void StopBrainOfferOfTheDay(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping offer of the day...");
			if (timers.TryGetValue("OfferOfTheDayTimer", out Timer value))
				value.Dispose();
			timers.Remove("OfferOfTheDayTimer");
		}

		public void InitializeBrainAutoResearch() {
			log(LogType.Info, LogSender.Tbot, "Initializing autoresearch...");
			StopBrainAutoResearch(false);
			timers.Add("AutoResearchTimer", new Timer(AutoResearch, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		public void StopBrainAutoResearch(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping autoresearch...");
			if (timers.TryGetValue("AutoResearchTimer", out Timer value))
				value.Dispose();
			timers.Remove("AutoResearchTimer");
		}

		public void InitializeAutoFarm() {
			log(LogType.Info, LogSender.Tbot, "Initializing autofarm...");
			StopAutoFarm(false);
			timers.Add("AutoFarmTimer", new Timer(AutoFarm, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Timeout.Infinite));
		}

		public void StopAutoFarm(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping autofarm...");
			if (timers.TryGetValue("AutoFarmTimer", out Timer value))
				value.Dispose();
			timers.Remove("AutoFarmTimer");
		}

		public void InitializeExpeditions() {
			log(LogType.Info, LogSender.Tbot, "Initializing expeditions...");
			StopExpeditions(false);
			timers.Add("ExpeditionsTimer", new Timer(HandleExpeditions, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		public void StopExpeditions(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping expeditions...");
			if (timers.TryGetValue("ExpeditionsTimer", out Timer value))
				value.Dispose();
			timers.Remove("ExpeditionsTimer");
		}

		private void InitializeHarvest() {
			log(LogType.Info, LogSender.Tbot, "Initializing harvest...");
			StopHarvest(false);
			timers.Add("HarvestTimer", new Timer(HandleHarvest, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private void StopHarvest(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping harvest...");
			if (timers.TryGetValue("HarvestTimer", out Timer value))
				value.Dispose();
			timers.Remove("HarvestTimer");
		}

		private void InitializeColonize() {
			log(LogType.Info, LogSender.Tbot, "Initializing colonize...");
			StopColonize(false);
			timers.Add("ColonizeTimer", new Timer(HandleColonize, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private void StopColonize(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping colonize...");
			if (timers.TryGetValue("ColonizeTimer", out Timer value))
				value.Dispose();
			timers.Remove("ColonizeTimer");
		}

		private void InitializeSleepMode() {
			log(LogType.Info, LogSender.Tbot, "Initializing sleep mode...");
			StopSleepMode(false);
			timers.Add("SleepModeTimer", new Timer(HandleSleepMode, null, 0, Timeout.Infinite));
		}

		private void StopSleepMode(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping sleep mode...");
			if (timers.TryGetValue("SleepModeTimer", out Timer value))
				value.Dispose();
			timers.Remove("SleepModeTimer");
		}

		private void InitializeFleetScheduler() {
			log(LogType.Info, LogSender.Tbot, "Initializing fleet scheduler...");
			userData.scheduledFleets = new();
			StopFleetScheduler(false);
			timers.Add("FleetSchedulerTimer", new Timer(HandleScheduledFleet, null, Timeout.Infinite, Timeout.Infinite));
		}

		private void StopFleetScheduler(bool echo = true) {
			if (echo)
				log(LogType.Info, LogSender.Tbot, "Stopping fleet scheduler...");
			if (timers.TryGetValue("FleetSchedulerTimer", out Timer value))
				value.Dispose();
			timers.Remove("FleetSchedulerTimer");
		}

		public void SendTelegramMessage(string fmt) {
			// We may consider any filter logic to be put here IE telegram notification disabled from settings
			if(telegramMessenger != null) {
				string finalStr = $"<code>[{userData.userInfo.PlayerName}@{userData.serverData.Name}]</code>\n" +
					fmt;
				telegramMessenger.SendMessage(finalStr).Wait();
			}
		}

		public void TelegramGetFleets() {
			userData.fleets = UpdateFleets().Where(f => !f.ReturnFlight).ToList();
			string message = "";
			foreach (Fleet fleet in userData.fleets) {
				message += $"{fleet.ID} -> Origin: {fleet.Origin.ToString()}, Dest: {fleet.Destination.ToString()}, Type: {fleet.Mission}, ArrivalTime: {fleet.ArrivalTime.ToString()}\n\n";
			}
			SendTelegramMessage(message);

		}

		public void TelegramBuild(Buildables buildable, decimal num = 0) {
			string results = "";
			decimal MaxNumToBuild = 0;
			Resources cost = Helpers.CalcPrice(buildable, 1);
			foreach (Celestial celestial in userData.celestials.Where(c => c is Planet).ToList()) {
				List<decimal> MaxNumber = new();
				UpdatePlanet(celestial, UpdateTypes.Constructions);
				if ((int) celestial.Constructions.BuildingID == (int) Buildables.NaniteFactory || (int) celestial.Constructions.BuildingID == (int) Buildables.Shipyard) {
					results += $"{celestial.Coordinate.ToString()}: Shipyard or Nanite in construction\n";
					continue;
				}
				UpdatePlanet(celestial, UpdateTypes.Resources);
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
						bool rez = ogamedService.BuildDefences(celestial, buildable, (long) MaxNumToBuild);
					} else {
						bool rez = ogamedService.BuildShips(celestial, buildable, (long) MaxNumToBuild);
						results += $"{celestial.Coordinate.ToString()}: {MaxNumToBuild} started\n";
					}
				} else {
					results += $"{celestial.Coordinate.ToString()}: Not enough resources\n";
				}
			}
			if (results != "") {
				SendTelegramMessage(results);
			} else {
				SendTelegramMessage("Could not start build anywhere.");
			}
			return;
		}

		public void TelegramGetCurrentAuction() {
			Auction auction;
			try {
				auction = ogamedService.GetCurrentAuction();
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
				SendTelegramMessage(outStr);
			} catch (Exception e) {
				SendTelegramMessage($"Error on GetCurrentAuction {e.Message}");
				return;
			}
		}

		public void TelegramSubscribeToNextAuction() {
			var auction = ogamedService.GetCurrentAuction();
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
				SendTelegramMessage($"Next auction notified in {timeStr}");
				timers.Add("TelegramAuctionSubscription", new Timer(_ => {
					var nextAuction = ogamedService.GetCurrentAuction();
					string auctionStr =
						$"Auction in progress! \n" +
						$"{nextAuction.ToString()}";
					SendTelegramMessage(auctionStr);
				}, null, timerTimeMs, Timeout.Infinite));
			} else {
				SendTelegramMessage("An auction is in progress! Hurry and use <code>/getcurrentauction</code>");
			}
		}

		public void TelegramBidAuctionMinimum() {
			var auction = ogamedService.GetCurrentAuction();
			if (auction.HasFinished) {
				SendTelegramMessage("No auction in progress!");
			} else {
				// check if auction is currently ours
				if (userData.userInfo.PlayerID == auction.HighestBidderUserID) {
					SendTelegramMessage("Auction is already ours!\n" +
						$"Item: \"{auction.CurrentItem}\"\n" +
						$"Ending in: {auction.GetTimeString()}");
				} else {
					long minBidRequired = auction.MinimumBid - auction.AlreadyBid;

					Celestial celestial = null;

					log(LogType.Info, LogSender.Tbot, "Finding input for Minimum Bid into Auction...");
					foreach (var item in auction.Resources) {
						var planetIdStr = item.Key;
						var planetResources = item.Value;

						long auctionPoints = (long) Math.Round(
								(planetResources.input.Metal / auction.ResourceMultiplier.Metal) +
								(planetResources.input.Crystal / auction.ResourceMultiplier.Crystal) +
								(planetResources.input.Deuterium / auction.ResourceMultiplier.Deuterium)
						);
						if (auctionPoints > minBidRequired) {
							SendTelegramMessage($"Found celestial! \"{planetResources.Name}\". ID: {planetIdStr}");
							celestial = new Celestial();
							celestial.ID = Int32.Parse(planetIdStr);
							celestial.Name = planetResources.Name;
							celestial.Resources = new Resources();
							celestial.Resources.Metal = planetResources.input.Metal;
							celestial.Resources.Crystal = planetResources.input.Crystal;
							celestial.Resources.Deuterium = planetResources.input.Deuterium;

							log(LogType.Info, LogSender.Tbot, $"Planet \"{planetResources.Name}\" points {auctionPoints} > {minBidRequired}. Proceeding! :)");
							break;
						} else
							log(LogType.Info, LogSender.Tbot, $"Planet \"{planetResources.Name}\" points {auctionPoints} < {minBidRequired}. Discarding");
					}

					if (celestial == null) {
						SendTelegramMessage(
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
							SendTelegramMessage("Cannot bid. Try again");
							log(LogType.Info, LogSender.Tbot, $"Planet \"{celestial.Name}\" minimum bidding failed. Remaining {minBidRequired}. Doing nothing");
						} else {
							SendTelegramMessage(
								$"Bidding Auction with M:{res.Metal} C:{res.Crystal} D:{res.Deuterium}\n" +
								$"From {celestial.Name} ID:{celestial.Name}");
							TelegramBidAuction(celestial, res);
						}
					}
				}
			}
		}

		public void TelegramBidAuction(Celestial celestial, Resources res) {
			log(LogType.Info, LogSender.Tbot, $"Bidding auction with {celestial.Name} {res.ToString()}");
			var result = ogamedService.DoAuction(celestial, res);
			if (result.Item1) {
				SendTelegramMessage($"Auction done with Resources of Planet {celestial.Name} ID:{celestial.ID} \n" +
												$"M:{res.Metal} C:{res.Crystal} D:{res.Deuterium}");
			} else {
				SendTelegramMessage($"BidAuction failed. \"{result.Item2}\"");
			}
		}



		public void TelegramCollect() {
			timers.Add("TelegramCollect", new Timer(AutoRepatriate, null, 3000, Timeout.Infinite));

			return;
		}

		public void TelegramCancelGhostSleep() {
			if (!timers.TryGetValue("GhostSleepTimer", out Timer value)) {
				SendTelegramMessage("No GhostSleep configured or too late to cancel (fleets already sent), Do a <code>/wakeup</code>");
				return;
			}

			timers.TryGetValue("GhostSleepTimer", out Timer value2);
			value2.Dispose();
			timers.Remove("GhostSleepTimer");
			telegramUserData.Mission = Missions.None;
			InitializeFeatures();
			SendTelegramMessage("Ghostsleep canceled!");

			return;
		}

		public void TelegramJumpGate(Celestial origin, Coordinate destination, string mode) {
			if (origin.Coordinate.Type == Celestials.Planet) {
				SendTelegramMessage($"Current Celestial is not a moon.");
				return;
			}

			Celestial moondest = userData.celestials.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) destination.Galaxy)
				.Where(c => c.Coordinate.System == (int) destination.System)
				.Where(c => c.Coordinate.Position == (int) destination.Position)
				.Where(c => c.Coordinate.Type == Celestials.Moon)
				.SingleOrDefault() ?? new() { ID = 0 };

			if (moondest.ID == 0) {
				SendTelegramMessage($"{destination.ToString()} -> Moon not found!");
				return;
			}

			if (moondest.Coordinate.ToString().Equals(origin.Coordinate.ToString())) {
				SendTelegramMessage($"Origin and destination are the same! did you /celestial?");
				return;
			}

			origin = UpdatePlanet(origin, UpdateTypes.Resources);
			origin = UpdatePlanet(origin, UpdateTypes.Ships);

			if (origin.Ships.GetMovableShips().IsEmpty()) {
				SendTelegramMessage($"No ships on {origin.Coordinate}, did you /celestial?");
				return;
			}

			var payload = origin.Resources;
			Ships ships = origin.Ships;
			if (mode.Equals("auto")) {
				long idealSmallCargo = Helpers.CalcShipNumberForPayload(payload, Buildables.SmallCargo, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);

				if (idealSmallCargo <= origin.Ships.GetAmount(Buildables.SmallCargo)) {
					ships.SetAmount(Buildables.SmallCargo, origin.Ships.GetAmount(Buildables.SmallCargo) - (long) idealSmallCargo);
				} else {
					long idealLargeCargo = Helpers.CalcShipNumberForPayload(payload, Buildables.LargeCargo, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);
					if (idealLargeCargo <= origin.Ships.GetAmount(Buildables.LargeCargo)) {
						ships.SetAmount(Buildables.LargeCargo, origin.Ships.GetAmount(Buildables.LargeCargo) - (long) idealLargeCargo);
					} else {
						ships.SetAmount(Buildables.SmallCargo, 0);
						ships.SetAmount(Buildables.LargeCargo, 0);
					}
				}
			}
			bool result = ogamedService.JumpGate(origin, moondest, ships);
			if (result) {
				SendTelegramMessage($"JumGate Done!");
			} else {
				SendTelegramMessage($"JumGate Failed!");
			}
		}

		public void TelegramPhalanx(Celestial origin, Coordinate target) {
			string outputMessage = "";
			List<Fleet> phalanxed = ogamedService.Phalanx(origin, target, out outputMessage);
			if (phalanxed.Count == 0) {
				SendTelegramMessage($"No fleet phalanxed. Client returned {outputMessage}");
			}
			else {
				SendTelegramMessage($"Phalanxed {phalanxed.Count} fleets");
				foreach (Fleet fleetPhalanxed in phalanxed) {
					string currFleetStr =
						$"Mission {fleetPhalanxed.Mission.ToString()}\n" +
						$"Origin {fleetPhalanxed.Origin.ToString()}\n" +
						$"Destination {fleetPhalanxed.Destination.ToString()}\n" +
						$"Ships: {fleetPhalanxed.Ships.ToString()}";
					SendTelegramMessage($"Phalanxed: {currFleetStr}");
				}
			}
		}

		public void TelegramDeploy(Celestial celestial, Coordinate destination, decimal speed) {
			celestial = UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = UpdatePlanet(celestial, UpdateTypes.Ships);

			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				log(LogType.Warning, LogSender.FleetScheduler, $"[Deploy] From {celestial.Coordinate.ToString()}: No ships!");
				SendTelegramMessage($"No ships on {celestial.Coordinate}, did you /celestial?");
				return;
			}
			var payload = celestial.Resources;
			if (celestial.Resources.Deuterium == 0) {
				log(LogType.Warning, LogSender.FleetScheduler, $"[Deploy] From {celestial.Coordinate.ToString()}: there is no fuel!");
				SendTelegramMessage($"Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel.");
				return;
			}

			FleetPrediction fleetPrediction = Helpers.CalcFleetPrediction(celestial.Coordinate, destination, celestial.Ships, Missions.Deploy, speed, userData.researches, userData.serverData, userData.userInfo.Class);
			int fleetId = SendFleet(celestial, celestial.Ships, destination, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

			if (fleetId != (int) SendFleetCode.GenericError ||
				fleetId != (int) SendFleetCode.AfterSleepTime ||
				fleetId != (int) SendFleetCode.NotEnoughSlots) {
				log(LogType.Warning, LogSender.FleetScheduler, $"Fleet {fleetId} deployed from {celestial.Coordinate.Type} to {destination.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				SendTelegramMessage($"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {destination.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				return;
			}

			return;
		}

		public bool TelegramSwitch(decimal speed, Celestial attacked = null, bool fromTelegram = false) {
			Celestial celestial;

			if (attacked == null) {
				celestial = TelegramGetCurrentCelestial();
			} else {
				celestial = attacked; //for autofleetsave func when under attack, last option if no other destination found.
			}

			if (celestial.Coordinate.Type == Celestials.Planet) {
				bool hasMoon = userData.celestials.Count(c => c.HasCoords(new Coordinate(celestial.Coordinate.Galaxy, celestial.Coordinate.System, celestial.Coordinate.Position, Celestials.Moon))) == 1;
				if (!hasMoon) {
					if (fromTelegram)
						SendTelegramMessage($"This planet does not have a Moon! Switch impossible.");
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

			celestial = UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = UpdatePlanet(celestial, UpdateTypes.Ships);

			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				log(LogType.Warning, LogSender.FleetScheduler, $"[Switch] Skipping fleetsave from {celestial.Coordinate.ToString()}: No ships!");
				if (fromTelegram)
					SendTelegramMessage($"No ships on {celestial.Coordinate}, did you /celestial?");
				return false;
			}

			var payload = celestial.Resources;
			if (celestial.Resources.Deuterium == 0) {
				log(LogType.Warning, LogSender.FleetScheduler, $"[Switch] Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel!");
				if (fromTelegram)
					SendTelegramMessage($"Skipping fleetsave from {celestial.Coordinate.ToString()}: there is no fuel.");
				return false;
			}

			FleetPrediction fleetPrediction = Helpers.CalcFleetPrediction(celestial.Coordinate, dest, celestial.Ships, Missions.Deploy, speed, userData.researches, userData.serverData, userData.userInfo.Class);
			int fleetId = SendFleet(celestial, celestial.Ships, dest, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

			if (fleetId != (int) SendFleetCode.GenericError ||
				fleetId != (int) SendFleetCode.AfterSleepTime ||
				fleetId != (int) SendFleetCode.NotEnoughSlots) {
				log(LogType.Warning, LogSender.FleetScheduler, $"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {dest.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				if (fromTelegram)
					SendTelegramMessage($"Fleet {fleetId} switched from {celestial.Coordinate.Type} to {dest.Type}\nPredicted time: {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");
				return true;
			}

			return false;
		}

		public async void TelegramSetCurrentCelestial(Coordinate coord, string celestialType, Feature updateType = Feature.Null, bool editsettings = false) {
			userData.celestials = UpdateCelestials();

			//check if no error in submitted celestial (belongs to the current player)
			telegramUserData.CurrentCelestial = userData.celestials
				.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) coord.Galaxy)
				.Where(c => c.Coordinate.System == (int) coord.System)
				.Where(c => c.Coordinate.Position == (int) coord.Position)
				.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>(celestialType))
				.SingleOrDefault() ?? new() { ID = 0 };

			if (telegramUserData.CurrentCelestial.ID == 0) {
				SendTelegramMessage("Error! Wrong information. Verify coordinate.\n");
				return;
			}
			if (editsettings) {
				await EditSettings(telegramUserData.CurrentCelestial, updateType);
				SendTelegramMessage($"JSON settings updated to: {telegramUserData.CurrentCelestial.Coordinate.ToString()}\nWait few seconds for Bot to reload before sending commands.");
			} else {
				SendTelegramMessage($"Main celestial successfuly updated to {telegramUserData.CurrentCelestial.Coordinate.ToString()}");
			}
			return;
		}

		public Celestial TelegramGetCurrentCelestial() {
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
					SendTelegramMessage("Error! Could not parse Celestial from JSON settings. Need <code>/editsettings</code>");
					return new Celestial();
				}

				telegramUserData.CurrentCelestial = celestial;
			}

			return telegramUserData.CurrentCelestial;
		}

		public void TelegramGetInfo(Celestial celestial) {

			celestial = UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = UpdatePlanet(celestial, UpdateTypes.Ships);
			string result = "";
			string resources = $"{celestial.Resources.Metal.ToString("#,#", CultureInfo.InvariantCulture)} Metal\n" +
								$"{celestial.Resources.Crystal.ToString("#,#", CultureInfo.InvariantCulture)} Crystal\n" +
								$"{celestial.Resources.Deuterium.ToString("#,#", CultureInfo.InvariantCulture)} Deuterium\n\n";
			string ships = celestial.Ships.GetMovableShips().ToString();

			if (celestial.Resources.TotalResources == 0)
				result += "No Resources." ?? resources;
			if (celestial.Ships.GetMovableShips().IsEmpty())
				result += "No ships." ?? ships;

			SendTelegramMessage($"{celestial.Coordinate.ToString()}\n\n" +
				"Resources:\n" +
				$"{resources}" +
				"Ships:\n" +
				$"{ships}");

			return;
		}

		public void SpyCrash(Celestial fromCelestial, Coordinate target = null) {
			decimal speed = Speeds.HundredPercent;
			fromCelestial = UpdatePlanet(fromCelestial, UpdateTypes.Ships);
			fromCelestial = UpdatePlanet(fromCelestial, UpdateTypes.Resources);
			var payload = fromCelestial.Resources;
			Random random = new Random();

			if (fromCelestial.Ships.EspionageProbe == 0 || payload.Deuterium < 1) {
				log(LogType.Info, LogSender.FleetScheduler, $"No probes or no Fuel on {fromCelestial.Coordinate.ToString()}!");
				SendTelegramMessage($"No probes or no Fuel on {fromCelestial.Coordinate.ToString()}!");
				return;
			}
			// spycrash auto part
			if (target == null) {
				List<Coordinate> spycrash = new();
				int playerid = userData.userInfo.PlayerID;
				int sys = 0;
				for (sys = fromCelestial.Coordinate.System - 2; sys <= fromCelestial.Coordinate.System + 2; sys++) {
					sys = Helpers.ClampSystem(sys);
					GalaxyInfo galaxyInfo = ogamedService.GetGalaxyInfo(fromCelestial.Coordinate.Galaxy, sys);
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
					SendTelegramMessage($"No planet to spycrash on could be found over system -2 -> +2");
					return;
				} else {
					target = spycrash[random.Next(spycrash.Count())];
				}
			}
			var attackingShips = new Ships().Add(Buildables.EspionageProbe, 1);

			int fleetId = SendFleet(fromCelestial, attackingShips, target, Missions.Attack, speed);

			if (fleetId != (int) SendFleetCode.GenericError ||
				fleetId != (int) SendFleetCode.AfterSleepTime ||
				fleetId != (int) SendFleetCode.NotEnoughSlots) {
				log(LogType.Info, LogSender.FleetScheduler, $"EspionageProbe sent to crash on {target.ToString()}");

				SendTelegramMessage($"EspionageProbe sent to crash on {target.ToString()}");
			}
			return;
		}

		public void TelegramCollectDeut(long MinAmount = 0) {
			userData.fleets = UpdateFleets();
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
				SendTelegramMessage("Error! Could not parse auto repatriate Celestial from JSON settings. Need <code>/editsettings</code>");
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

				var tempCelestial = UpdatePlanet(celestial, UpdateTypes.Fast);
				userData.fleets = UpdateFleets();

				tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Resources);
				tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Ships);

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

				long idealShips = Helpers.CalcShipNumberForPayload(payload, preferredShip, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);

				Ships ships = new();
				if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
					if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
						ships.Add(preferredShip, idealShips);
					} else {
						ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
					}
					payload = Helpers.CalcMaxTransportableResources(ships, payload, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo);

					if ((long) payload.TotalResources >= (long) MinAmount) {
						var fleetId = SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
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
				SendTelegramMessage($"{TotalDeut} Deuterium sent.");
			} else {
				SendTelegramMessage("No resources sent");
			}
		}
		public void AutoFleetSave(Celestial celestial, bool isSleepTimeFleetSave = false, long minDuration = 0, bool forceUnsafe = false, bool WaitFleetsReturn = false, Missions TelegramMission = Missions.None, bool fromTelegram = false, bool saveall = false) {
			DateTime departureTime = GetDateTime();
			duration = minDuration;

			if (WaitFleetsReturn) {

				userData.fleets = UpdateFleets();
				long interval;
				try {
					interval = (userData.fleets.OrderBy(f => f.BackIn).Last().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
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

					interval += Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime TimeToGhost = departureTime.AddMilliseconds(interval);
					NextWakeUpTime = TimeToGhost.AddMilliseconds(minDuration * 1000);

					if (saveall)
						timers.Add("GhostSleepTimer", new Timer(GhostandSleepAfterFleetsReturnAll, null, interval, Timeout.Infinite));
					else
						timers.Add("GhostSleepTimer", new Timer(GhostandSleepAfterFleetsReturn, null, interval, Timeout.Infinite));

					log(LogType.Info, LogSender.SleepMode, $"Fleets active, Next check at {TimeToGhost.ToString()}");
					SendTelegramMessage($"Waiting for fleets return, delaying ghosting at {TimeToGhost.ToString()}");

					return;
				} else if (interval == 0 && (!timers.TryGetValue("GhostSleepTimer", out Timer value2))) {

					log(LogType.Info, LogSender.SleepMode, $"No fleets active, Ghosting now.");
					NextWakeUpTime = departureTime.AddMilliseconds(minDuration * 1000);
					if (saveall)
						GhostandSleepAfterFleetsReturnAll(null);
					else
						GhostandSleepAfterFleetsReturn(null);

					return;
				} else if (timers.TryGetValue("GhostSleepTimer", out Timer value3)) {
					SendTelegramMessage($"GhostSleep already planned, try /cancelghostsleep");
					return;
				}
			}

			celestial = UpdatePlanet(celestial, UpdateTypes.Ships);
			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				log(LogType.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fleet to save!");
				if (fromTelegram)
					SendTelegramMessage($"{celestial.ToString()}: there is no fleet!");
				return;
			}

			celestial = UpdatePlanet(celestial, UpdateTypes.Resources);
			Celestial destination = new() { ID = 0 };
			if (!forceUnsafe)
				forceUnsafe = (bool) settings.SleepMode.AutoFleetSave.ForceUnsafe; //not used anymore


			if (celestial.Resources.Deuterium == 0) {
				log(LogType.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fuel!");
				if (fromTelegram)
					SendTelegramMessage($"{celestial.ToString()}: there is no fuel!");
				return;
			}

			long maxDeuterium = celestial.Resources.Deuterium;

			if (isSleepTimeFleetSave) {
				if (DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp)) {
					if (departureTime >= wakeUp)
						wakeUp = wakeUp.AddDays(1);
					minDuration = (long) wakeUp.Subtract(departureTime).TotalSeconds;
				} else {
					log(LogType.Warning, LogSender.FleetScheduler, $"Could not plan fleetsave from {celestial.ToString()}: unable to parse comeback time");
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
				log(LogType.Warning, LogSender.FleetScheduler, "Error: Could not parse 'DefaultMission' from settings, value set to Harvest.");
				mission = Missions.Harvest;
			}

			if (TelegramMission != Missions.None)
				mission = TelegramMission;

			List<FleetHypotesis> fleetHypotesis = GetFleetSaveDestination(userData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
			if (fleetHypotesis.Count() > 0) {
				foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
					log(LogType.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
					if (CheckFuel(fleet, celestial)) {
						fleetId = SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, userData.userInfo.Class, true);

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
				SendTelegramMessage($"No debris field found for {mission}, try to /spycrash.");
				return;
			} else if (fromTelegram && !AlreadySent && fleetHypotesis.Count() >= 0) {
				SendTelegramMessage($"Available fuel: {celestial.Resources.Deuterium}\nNo destination found for {mission}, try to reduce ghost time.");
				return;
			}

			//Doing Deploy
			if (!AlreadySent) {
				log(LogType.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} possible, checking next mission..");
				if (mission == Missions.Harvest) { mission = Missions.Deploy; } else { mission = Missions.Harvest; };
				mission = Missions.Deploy;
				fleetHypotesis = GetFleetSaveDestination(userData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						log(LogType.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, userData.userInfo.Class, true);

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
				log(LogType.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Colonize destination...");
				mission = Missions.Colonize;
				fleetHypotesis = GetFleetSaveDestination(userData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						log(LogType.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, userData.userInfo.Class, true);

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
				log(LogType.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} found, checking Spy destination...");
				mission = Missions.Spy;
				fleetHypotesis = GetFleetSaveDestination(userData.celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				if (fleetHypotesis.Count > 0) {
					foreach (FleetHypotesis fleet in fleetHypotesis.OrderBy(pf => pf.Fuel).ThenBy(pf => pf.Duration <= minDuration)) {
						log(LogType.Warning, LogSender.FleetScheduler, $"checking {mission} fleet to: {fleet.Destination}");
						if (CheckFuel(fleet, celestial)) {
							fleetId = SendFleet(fleet.Origin, fleet.Ships, fleet.Destination, fleet.Mission, fleet.Speed, payload, userData.userInfo.Class, true);

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
				log(LogType.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.ToString()} no {mission} possible (missing fuel?), checking for switch if has Moon");
				//var validSpeeds = userData.userInfo.Class == CharacterClass.General ? Speeds.GetGeneralSpeedsList() : Speeds.GetNonGeneralSpeedsList();
				//Random randomSpeed = new Random();
				//decimal speed = validSpeeds[randomSpeed.Next(validSpeeds.Count)];
				decimal speed = 10;
				AlreadySent = TelegramSwitch(speed, celestial);
			}

			if (!AlreadySent) {
				log(LogType.Warning, LogSender.FleetScheduler, $"Fleetsave from {celestial.Coordinate.ToString()} no suitable destination found, you gonna get hit!");
				SendTelegramMessage($"Fleetsave from {celestial.Coordinate.ToString()} No destination found!, you gonna get hit!");
				return;
			}


			if ((bool) settings.SleepMode.AutoFleetSave.Recall && AlreadySent) {
				if (fleetId != (int) SendFleetCode.GenericError ||
					fleetId != (int) SendFleetCode.AfterSleepTime ||
					fleetId != (int) SendFleetCode.NotEnoughSlots) {
					Fleet fleet = userData.fleets.Single(fleet => fleet.ID == fleetId);
					DateTime time = GetDateTime();
					var interval = ((minDuration / 2) * 1000) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					if (interval <= 0)
						interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					timers.Add($"RecallTimer-{fleetId.ToString()}", new Timer(RetireFleet, fleet, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.FleetScheduler, $"The fleet will be recalled at {newTime.ToString()}");
					if (fromTelegram)
						SendTelegramMessage($"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}, recalled at {newTime.ToString()}");
				}
			} else {
				if (fleetId != (int) SendFleetCode.GenericError ||
					fleetId != (int) SendFleetCode.AfterSleepTime ||
					fleetId != (int) SendFleetCode.NotEnoughSlots) {
					Fleet fleet = userData.fleets.Single(fleet => fleet.ID == fleetId);
					DateTime returntime = (DateTime) fleet.BackTime;
					log(LogType.Info, LogSender.FleetScheduler, $"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, arrive at {possibleFleet.Duration} fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}");
					if (fromTelegram)
						SendTelegramMessage($"Fleet {fleetId} send to {possibleFleet.Mission} on {possibleFleet.Destination.ToString()}, arrive at {possibleFleet.Duration.ToString()}, returned at {returntime.ToString()} fuel consumed: {possibleFleet.Fuel.ToString("#,#", CultureInfo.InvariantCulture)}");
				}
			}

		}

		private bool CheckFuel(FleetHypotesis fleetHypotesis, Celestial celestial) {
			if (celestial.Resources.Deuterium < fleetHypotesis.Fuel) {
				log(LogType.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: not enough fuel!");
				return false;
			}
			if (Helpers.CalcFleetFuelCapacity(fleetHypotesis.Ships, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier) < fleetHypotesis.Fuel) {
				log(LogType.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: ships don't have enough fuel capacity!");
				return false;
			}
			return true;
		}

		private List<FleetHypotesis> GetFleetSaveDestination(List<Celestial> source, Celestial origin, DateTime departureDate, long minFlightTime, Missions mission, long maxFuel, bool forceUnsafe = false) {
			var validSpeeds = userData.userInfo.Class == CharacterClass.General ? Speeds.GetGeneralSpeedsList() : Speeds.GetNonGeneralSpeedsList();
			List<FleetHypotesis> possibleFleets = new();
			List<Coordinate> possibleDestinations = new();
			GalaxyInfo galaxyInfo = new();
			origin = UpdatePlanet(origin, UpdateTypes.Resources);
			origin = UpdatePlanet(origin, UpdateTypes.Ships);

			switch (mission) {
				case Missions.Spy:
					if (origin.Ships.EspionageProbe == 0) {
						log(LogType.Info, LogSender.FleetScheduler, $"No espionageprobe available, skipping to next mission...");
						break;
					}
					Coordinate destination = new(origin.Coordinate.Galaxy, origin.Coordinate.System, 16, Celestials.Planet);
					foreach (var currentSpeed in validSpeeds) {
						FleetPrediction fleetPrediction = Helpers.CalcFleetPrediction(origin.Coordinate, destination, origin.Ships.GetMovableShips(), mission, currentSpeed, userData.researches, userData.serverData, userData.userInfo.Class);

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
						log(LogType.Info, LogSender.FleetScheduler, $"No colony ship available, skipping to next mission...");
						break;
					}
					galaxyInfo = ogamedService.GetGalaxyInfo(origin.Coordinate);
					int pos = 1;
					foreach (var planet in galaxyInfo.Planets) {
						if (planet == null)
							possibleDestinations.Add(new(origin.Coordinate.Galaxy, origin.Coordinate.System, pos));
						pos = +1;
					}

					if (possibleDestinations.Count() > 0) {
						foreach (var possibleDestination in possibleDestinations) {
							foreach (var currentSpeed in validSpeeds) {
								FleetPrediction fleetPrediction = Helpers.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, userData.researches, userData.serverData, userData.userInfo.Class);

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
						log(LogType.Info, LogSender.FleetScheduler, $"No recycler available, skipping to next mission...");
						break;
					}
					int playerid = userData.userInfo.PlayerID;
					int sys = 0;
					for (sys = origin.Coordinate.System - 5; sys <= origin.Coordinate.System + 5; sys++) {
						sys = Helpers.ClampSystem(sys);
						galaxyInfo = ogamedService.GetGalaxyInfo(origin.Coordinate.Galaxy, sys);
						foreach (var planet in galaxyInfo.Planets) {
							if (planet != null && planet.Debris != null && planet.Debris.Resources.TotalResources > 0) {
								possibleDestinations.Add(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris));
							}
						}
					}


					if (possibleDestinations.Count() > 0) {
						foreach (var possibleDestination in possibleDestinations) {
							foreach (var currentSpeed in validSpeeds) {
								FleetPrediction fleetPrediction = Helpers.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, userData.researches, userData.serverData, userData.userInfo.Class);

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
							FleetPrediction fleetPrediction = Helpers.CalcFleetPrediction(origin.Coordinate, possibleDestination, origin.Ships.GetMovableShips(), mission, currentSpeed, userData.researches, userData.serverData, userData.userInfo.Class);

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


		public void GhostandSleepAfterFleetsReturnAll(object state) {
			if (timers.TryGetValue("GhostSleepTimer", out Timer value))
				value.Dispose();
			timers.Remove("GhostSleepTimer");


			var celestialsToFleetsave = UpdateCelestials();
			celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
			if (celestialsToFleetsave.Count == 0)
				celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Planet).ToList();

			foreach (Celestial celestial in celestialsToFleetsave)
				AutoFleetSave(celestial, false, duration, false, false, telegramUserData.Mission, true);

			SleepNow(NextWakeUpTime);
		}

		public void GhostandSleepAfterFleetsReturn(object state) {
			if (timers.TryGetValue("GhostSleepTimer", out Timer value))
				value.Dispose();
			timers.Remove("GhostSleepTimer");

			AutoFleetSave(telegramUserData.CurrentCelestialToSave, false, duration, false, false, telegramUserData.Mission, true);

			SleepNow(NextWakeUpTime);
		}

		public void SleepNow(DateTime WakeUpTime) {
			long interval;

			DateTime time = GetDateTime();
			interval = (long) WakeUpTime.Subtract(time).TotalMilliseconds;
			timers.Add("TelegramSleepModeTimer", new Timer(WakeUpNow, null, interval, Timeout.Infinite));
			SendTelegramMessage($"Going to sleep, Waking Up at {WakeUpTime.ToString()}");
			log(LogType.Info, LogSender.SleepMode, $"Going to sleep..., Waking Up at {WakeUpTime.ToString()}");

			userData.isSleeping = true;
		}


		private void HandleSleepMode(object state) {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value)) {
				return;
			}

			try {
				WaitFeature();

				DateTime time = GetDateTime();

				if (!(bool) settings.SleepMode.Active) {
					log(LogType.Warning, LogSender.SleepMode, "Sleep mode is disabled");
					WakeUp(null);
				} else if (!DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep)) {
					log(LogType.Warning, LogSender.SleepMode, "Unable to parse GoToSleep time. Sleep mode will be disabled");
					WakeUp(null);
				} else if (!DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp)) {
					log(LogType.Warning, LogSender.SleepMode, "Unable to parse WakeUp time. Sleep mode will be disabled");
					WakeUp(null);
				} else if (goToSleep == wakeUp) {
					log(LogType.Warning, LogSender.SleepMode, "GoToSleep time and WakeUp time must be different. Sleep mode will be disabled");
					WakeUp(null);
				} else {
					long interval;

					if (time >= goToSleep) {
						if (time >= wakeUp) {
							if (goToSleep >= wakeUp) {
								// YES YES YES
								// ASLEEP
								// WAKE UP NEXT DAY
								interval = (long) wakeUp.AddDays(1).Subtract(time).TotalMilliseconds + (long) Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								GoToSleep(newTime);
							} else {
								// YES YES NO
								// AWAKE
								// GO TO SLEEP NEXT DAY
								interval = (long) goToSleep.AddDays(1).Subtract(time).TotalMilliseconds + (long) Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								WakeUp(newTime);
							}
						} else {
							if (goToSleep >= wakeUp) {
								// YES NO YES
								// THIS SHOULDNT HAPPEN
								interval = Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								if (interval <= 0)
									interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								log(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
							} else {
								// YES NO NO
								// ASLEEP
								// WAKE UP SAME DAY
								interval = (long) wakeUp.Subtract(time).TotalMilliseconds + (long) Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
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
								interval = (long) goToSleep.Subtract(time).TotalMilliseconds + (long) Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								WakeUp(newTime);
							} else {
								// NO YES NO
								// THIS SHOULDNT HAPPEN
								interval = Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								if (interval <= 0)
									interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								log(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
							}
						} else {
							if (goToSleep >= wakeUp) {
								// NO NO YES
								// ASLEEP
								// WAKE UP SAME DAY
								interval = (long) wakeUp.Subtract(time).TotalMilliseconds + (long) Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								GoToSleep(newTime);
							} else {
								// NO NO NO
								// AWAKE
								// GO TO SLEEP SAME DAY
								interval = (long) goToSleep.Subtract(time).TotalMilliseconds + (long) Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								WakeUp(newTime);
							}
						}
					}
				}
			} catch (Exception e) {
				log(LogType.Warning, LogSender.SleepMode, $"An error has occurred while handling sleep mode: {e.Message}");
				log(LogType.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = GetDateTime();
				long interval = Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				CheckCelestials();
			} finally {
				releaseFeature();
			}
		}

		private void GoToSleep(object state) {
			try {
				userData.fleets = UpdateFleets();
				bool delayed = false;
				if ((bool) settings.SleepMode.PreventIfThereAreFleets && userData.fleets.Count() > 0) {
					if (DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp) && DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep)) {
						DateTime time = GetDateTime();
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
							log(LogType.Info, LogSender.SleepMode, "There are fleets that would come back during sleep time. Delaying sleep mode.");
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
								interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							DateTime newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
							delayed = true;
							log(LogType.Info, LogSender.SleepMode, $"Fleets active, Next check at {newTime.ToString()}");
							if ((bool) settings.SleepMode.TelegramMessenger.Active) {
								SendTelegramMessage($"Fleets active, Next check at {newTime.ToString()}");
							}
						}
					} else {
						log(LogType.Warning, LogSender.SleepMode, "Unable to parse WakeUp or GoToSleep time.");
					}
				}
				if (!delayed) {
					log(LogType.Info, LogSender.SleepMode, "Going to sleep...");
					log(LogType.Info, LogSender.SleepMode, $"Waking Up at {state.ToString()}");

					if ((bool) settings.SleepMode.AutoFleetSave.Active) {
						var celestialsToFleetsave = UpdatePlanets(UpdateTypes.Ships);
						if ((bool) settings.SleepMode.AutoFleetSave.OnlyMoons)
							celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
						foreach (Celestial celestial in celestialsToFleetsave.OrderByDescending(c => c.Ships.GetFleetPoints())) {
							try {
								AutoFleetSave(celestial, true);
							} catch (Exception e) {
								LoggerService.Logger.WriteLog(LogType.Warning, LogSender.SleepMode, $"An error has occurred while fleetsaving: {e.Message}");
							}
						}
					}

					if ((bool) settings.SleepMode.TelegramMessenger.Active && state != null) {
						SendTelegramMessage($"[{userData.userInfo.PlayerName}{userData.serverData.Name}] Going to sleep, Waking Up at {state.ToString()}");
					}
					userData.isSleeping = true;
				}
				InitializeFeatures();
			} catch (Exception e) {
				log(LogType.Warning, LogSender.SleepMode, $"An error has occurred while going to sleep: {e.Message}");
				log(LogType.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = GetDateTime();
				long interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				CheckCelestials();
			}
		}


		public bool TelegramIsUnderAttack() {
			bool result = ogamedService.IsUnderAttack();

			return result;
		}


		public void WakeUpNow(object state) {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value))
				value.Dispose();
			timers.Remove("TelegramSleepModeTimer");
			SendTelegramMessage($"Bot woke up!");

			log(LogType.Info, LogSender.SleepMode, "Bot woke up!");

			userData.isSleeping = false;
			InitializeFeatures();
		}

		private void WakeUp(object state) {
			try {
				log(LogType.Info, LogSender.SleepMode, "Waking Up...");
				if ((bool) settings.SleepMode.TelegramMessenger.Active && state != null) {
					SendTelegramMessage($"Waking up");
					SendTelegramMessage($"Going to sleep at {state.ToString()}");
				}
				userData.isSleeping = false;
				InitializeFeatures();

			} catch (Exception e) {
				log(LogType.Warning, LogSender.SleepMode, $"An error has occurred while waking up: {e.Message}");
				log(LogType.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = GetDateTime();
				long interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				CheckCelestials();
			}
		}

		private void FakeActivity() {
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
				celestial = UpdatePlanet(celestial, UpdateTypes.Defences);
			}
			randomCelestial = userData.celestials.Shuffle().FirstOrDefault() ?? new() { ID = 0 };
			if (randomCelestial.ID != 0) {
				randomCelestial = UpdatePlanet(randomCelestial, UpdateTypes.Defences);
			}

			return;
		}

		private async void Defender(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Defender].WaitAsync();
				log(LogType.Info, LogSender.Defender, "Checking attacks...");

				if (userData.isSleeping) {
					log(LogType.Info, LogSender.Defender, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Defender].Release();
					return;
				}

				FakeActivity();
				userData.fleets = UpdateFleets();
				bool isUnderAttack = ogamedService.IsUnderAttack();
				DateTime time = GetDateTime();
				if (isUnderAttack) {
					if ((bool) settings.Defender.Alarm.Active)
						await Task.Factory.StartNew(() => Helpers.PlayAlarm());
					// UpdateTitle(false, true);
					log(LogType.Warning, LogSender.Defender, "ENEMY ACTIVITY!!!");
					userData.attacks = ogamedService.GetAttacks();
					foreach (AttackerFleet attack in userData.attacks) {
						HandleAttack(attack);
					}
				} else {
					log(LogType.Info, LogSender.Defender, "Your empire is safe");
				}
				long interval = Helpers.CalcRandomInterval((int) settings.Defender.CheckIntervalMin, (int) settings.Defender.CheckIntervalMax);
				if (interval <= 0)
					interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				log(LogType.Info, LogSender.Defender, $"Next check at {newTime.ToString()}");
				CheckCelestials();
			} catch (Exception e) {
				log(LogType.Warning, LogSender.Defender, $"An error has occurred while checking for attacks: {e.Message}");
				log(LogType.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
				DateTime time = GetDateTime();
				long interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				log(LogType.Info, LogSender.Defender, $"Next check at {newTime.ToString()}");
				CheckCelestials();
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
					log(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.Brain.Active && (bool) settings.Brain.BuyOfferOfTheDay.Active) {
					log(LogType.Info, LogSender.Brain, "Buying offer of the day...");
					if (userData.isSleeping) {
						log(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
						xaSem[Feature.Brain].Release();
						return;
					}
					var result = ogamedService.BuyOfferOfTheDay();
					if (result)
						log(LogType.Info, LogSender.Brain, "Offer of the day succesfully bought.");
					else
						log(LogType.Info, LogSender.Brain, "Offer of the day already bought.");
				} else {
					log(LogType.Info, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"BuyOfferOfTheDay Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogType.Info, LogSender.Brain, $"Stopping feature.");
					} else {
						var time = GetDateTime();
						var interval = Helpers.CalcRandomInterval((int) settings.Brain.BuyOfferOfTheDay.CheckIntervalMin, (int) settings.Brain.BuyOfferOfTheDay.CheckIntervalMax);
						if (interval <= 0)
							interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("OfferOfTheDayTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.Brain, $"Next BuyOfferOfTheDay check at {newTime.ToString()}");
						CheckCelestials();
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
				log(LogType.Info, LogSender.Brain, "Running autoresearch...");

				if (userData.isSleeping) {
					log(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.Brain.Active && (bool) settings.Brain.AutoResearch.Active || timers.TryGetValue("AutoResearchTimer", out Timer value)) {
					userData.researches = ogamedService.GetResearches();
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
						log(LogType.Warning, LogSender.Brain, "Unable to parse Brain.AutoResearch.Target. Falling back to planet with biggest Research Lab");
						userData.celestials = UpdatePlanets(UpdateTypes.Facilities);
						celestial = userData.celestials
							.Where(c => c.Coordinate.Type == Celestials.Planet)
							.OrderByDescending(c => c.Facilities.ResearchLab)
							.First() as Planet;
					}

					celestial = UpdatePlanet(celestial, UpdateTypes.Facilities) as Planet;
					if (celestial.Facilities.ResearchLab == 0) {
						log(LogType.Info, LogSender.Brain, "Skipping AutoResearch: Research Lab is missing on target planet.");
						return;
					}
					celestial = UpdatePlanet(celestial, UpdateTypes.Constructions) as Planet;
					if (celestial.Constructions.ResearchID != 0) {
						delayResearch = (long) celestial.Constructions.ResearchCountdown * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						log(LogType.Info, LogSender.Brain, "Skipping AutoResearch: there is already a research in progress.");
						return;
					}
					if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab) {
						log(LogType.Info, LogSender.Brain, "Skipping AutoResearch: the Research Lab is upgrading.");
						return;
					}
					userData.slots = UpdateSlots();
					celestial = UpdatePlanet(celestial, UpdateTypes.Facilities) as Planet;
					celestial = UpdatePlanet(celestial, UpdateTypes.Resources) as Planet;
					celestial = UpdatePlanet(celestial, UpdateTypes.ResourcesProduction) as Planet;

					Buildables research;

					if ((bool) settings.Brain.AutoResearch.PrioritizeAstrophysics || (bool) settings.Brain.AutoResearch.PrioritizePlasmaTechnology || (bool) settings.Brain.AutoResearch.PrioritizeEnergyTechnology || (bool) settings.Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork) {
						List<Celestial> planets = new();
						foreach (var p in userData.celestials) {
							if (p.Coordinate.Type == Celestials.Planet) {
								var newPlanet = UpdatePlanet(p, UpdateTypes.Facilities);
								newPlanet = UpdatePlanet(p, UpdateTypes.Buildings);
								planets.Add(newPlanet);
							}
						}
						var plasmaDOIR = Helpers.CalcNextPlasmaTechDOIR(planets.Where(c => c is Planet).Cast<Planet>().ToList<Planet>(), userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
						log(LogType.Debug, LogSender.Brain, $"Next Plasma tech DOIR: {Math.Round(plasmaDOIR, 2).ToString()}");
						var astroDOIR = Helpers.CalcNextAstroDOIR(planets.Where(c => c is Planet).Cast<Planet>().ToList<Planet>(), userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
						log(LogType.Debug, LogSender.Brain, $"Next Astro DOIR: {Math.Round(astroDOIR, 2).ToString()}");

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
						} else if ((bool) settings.Brain.AutoResearch.PrioritizeEnergyTechnology && Helpers.ShouldResearchEnergyTech(planets.Where(c => c.Coordinate.Type == Celestials.Planet).Cast<Planet>().ToList<Planet>(), userData.researches, (int) settings.Brain.AutoResearch.MaxEnergyTechnology, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull)) {
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
							research = Helpers.GetNextResearchToBuild(celestial as Planet, userData.researches, (bool) settings.Brain.AutoMine.PrioritizeRobotsAndNanites, userData.slots, (int) settings.Brain.AutoResearch.MaxEnergyTechnology, (int) settings.Brain.AutoResearch.MaxLaserTechnology, (int) settings.Brain.AutoResearch.MaxIonTechnology, (int) settings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) settings.Brain.AutoResearch.MaxPlasmaTechnology, (int) settings.Brain.AutoResearch.MaxCombustionDrive, (int) settings.Brain.AutoResearch.MaxImpulseDrive, (int) settings.Brain.AutoResearch.MaxHyperspaceDrive, (int) settings.Brain.AutoResearch.MaxEspionageTechnology, (int) settings.Brain.AutoResearch.MaxComputerTechnology, (int) settings.Brain.AutoResearch.MaxAstrophysics, (int) settings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) settings.Brain.AutoResearch.MaxWeaponsTechnology, (int) settings.Brain.AutoResearch.MaxShieldingTechnology, (int) settings.Brain.AutoResearch.MaxArmourTechnology, (bool) settings.Brain.AutoResearch.OptimizeForStart, (bool) settings.Brain.AutoResearch.EnsureExpoSlots, userData.userInfo.Class, userData.staff.Geologist, userData.staff.Admiral);
						}
					} else {
						research = Helpers.GetNextResearchToBuild(celestial as Planet, userData.researches, (bool) settings.Brain.AutoMine.PrioritizeRobotsAndNanites, userData.slots, (int) settings.Brain.AutoResearch.MaxEnergyTechnology, (int) settings.Brain.AutoResearch.MaxLaserTechnology, (int) settings.Brain.AutoResearch.MaxIonTechnology, (int) settings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) settings.Brain.AutoResearch.MaxPlasmaTechnology, (int) settings.Brain.AutoResearch.MaxCombustionDrive, (int) settings.Brain.AutoResearch.MaxImpulseDrive, (int) settings.Brain.AutoResearch.MaxHyperspaceDrive, (int) settings.Brain.AutoResearch.MaxEspionageTechnology, (int) settings.Brain.AutoResearch.MaxComputerTechnology, (int) settings.Brain.AutoResearch.MaxAstrophysics, (int) settings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) settings.Brain.AutoResearch.MaxWeaponsTechnology, (int) settings.Brain.AutoResearch.MaxShieldingTechnology, (int) settings.Brain.AutoResearch.MaxArmourTechnology, (bool) settings.Brain.AutoResearch.OptimizeForStart, (bool) settings.Brain.AutoResearch.EnsureExpoSlots, userData.userInfo.Class, userData.staff.Geologist, userData.staff.Admiral);
					}

					if (
						(bool) settings.Brain.AutoResearch.PrioritizeIntergalacticResearchNetwork &&
						research != Buildables.Null &&
						research != Buildables.IntergalacticResearchNetwork &&
						celestial.Facilities.ResearchLab >= 10 &&
						userData.researches.ComputerTechnology >= 8 &&
						userData.researches.HyperspaceTechnology >= 8 &&
						(int) settings.Brain.AutoResearch.MaxIntergalacticResearchNetwork >= Helpers.GetNextLevel(userData.researches, Buildables.IntergalacticResearchNetwork) &&
						userData.celestials.Any(c => c.Facilities != null)
					) {
						var cumulativeLabLevel = Helpers.CalcCumulativeLabLevel(userData.celestials, userData.researches);
						var researchTime = Helpers.CalcProductionTime(research, Helpers.GetNextLevel(userData.researches, research), userData.serverData.SpeedResearch, celestial.Facilities, cumulativeLabLevel, userData.userInfo.Class == CharacterClass.Discoverer, userData.staff.Technocrat);
						var irnTime = Helpers.CalcProductionTime(Buildables.IntergalacticResearchNetwork, Helpers.GetNextLevel(userData.researches, Buildables.IntergalacticResearchNetwork), userData.serverData.SpeedResearch, celestial.Facilities, cumulativeLabLevel, userData.userInfo.Class == CharacterClass.Discoverer, userData.staff.Technocrat);
						if (irnTime < researchTime) {
							research = Buildables.IntergalacticResearchNetwork;
						}
					}

					int level = Helpers.GetNextLevel(userData.researches, research);
					if (research != Buildables.Null) {
						celestial = UpdatePlanet(celestial, UpdateTypes.Resources) as Planet;
						Resources cost = Helpers.CalcPrice(research, level);
						if (celestial.Resources.IsEnoughFor(cost)) {
							var result = ogamedService.BuildCancelable(celestial, research);
							if (result)
								log(LogType.Info, LogSender.Brain, $"Research {research.ToString()} level {level.ToString()} started on {celestial.ToString()}");
							else
								log(LogType.Warning, LogSender.Brain, $"Research {research.ToString()} level {level.ToString()} could not be started on {celestial.ToString()}");
						} else {
							log(LogType.Info, LogSender.Brain, $"Not enough resources to build: {research.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {cost.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
							if ((bool) settings.Brain.AutoResearch.Transports.Active) {
								userData.fleets = UpdateFleets();
								if (!Helpers.IsThereTransportTowardsCelestial(celestial, userData.fleets)) {
									Celestial origin = userData.celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoResearch.Transports.Origin.Galaxy)
										.Where(c => c.Coordinate.System == (int) settings.Brain.AutoResearch.Transports.Origin.System)
										.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoResearch.Transports.Origin.Position)
										.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoResearch.Transports.Origin.Type))
										.SingleOrDefault() ?? new() { ID = 0 };
									fleetId = HandleMinerTransport(origin, celestial, cost);
									if (fleetId == (int) SendFleetCode.AfterSleepTime) {
										stop = true;
									}
									if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
										delay = true;
									}
								} else {
									log(LogType.Info, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
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
					log(LogType.Info, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"AutoResearch Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogType.Info, LogSender.Brain, $"Stopping feature.");
					} else if (delay) {
						log(LogType.Info, LogSender.Brain, $"Delaying...");
						var time = GetDateTime();
						userData.fleets = UpdateFleets();
						long interval;
						try {
							interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = Helpers.CalcRandomInterval((int) settings.AutoResearch.CheckIntervalMin, (int) settings.AutoResearch.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoResearchTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					} else if (delayResearch > 0) {
						var time = GetDateTime();
						var newTime = time.AddMilliseconds(delayResearch);
						timers.GetValueOrDefault("AutoResearchTimer").Change(delayResearch, Timeout.Infinite);
						log(LogType.Info, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					} else {
						long interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoResearch.CheckIntervalMin, (int) settings.Brain.AutoResearch.CheckIntervalMax);
						Planet celestial = userData.celestials
							.Unique()
							.SingleOrDefault(c => c.HasCoords(new(
								(int) settings.Brain.AutoResearch.Target.Galaxy,
								(int) settings.Brain.AutoResearch.Target.System,
								(int) settings.Brain.AutoResearch.Target.Position,
								Celestials.Planet
								)
							)) as Planet ?? new Planet() { ID = 0 };
						var time = GetDateTime();
						if (celestial.ID != 0) {
							userData.fleets = UpdateFleets();
							celestial = UpdatePlanet(celestial, UpdateTypes.Constructions) as Planet;
							var incomingFleets = Helpers.GetIncomingFleets(celestial, userData.fleets);
							if (celestial.Constructions.ResearchCountdown != 0)
								interval = (long) ((long) celestial.Constructions.ResearchCountdown * (long) 1000) + (long) Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							else if (fleetId > (int) SendFleetCode.GenericError) {
								var fleet = userData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
								interval = (fleet.ArriveIn * 1000) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							} else if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab)
								interval = (long) ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							else if (incomingFleets.Count() > 0) {
								var fleet = incomingFleets
									.OrderBy(f => (f.Mission == Missions.Transport || f.Mission == Missions.Deploy) ? f.ArriveIn : f.BackIn)
									.First();
								interval = (((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							} else {
								interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoResearch.CheckIntervalMin, (int) settings.Brain.AutoResearch.CheckIntervalMax);
							}
						}
						if (interval <= 0)
							interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoResearchTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
					}
					CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private async void AutoFarm(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.AutoFarm].WaitAsync();

				log(LogType.Info, LogSender.AutoFarm, "Running autofarm...");

				if (userData.isSleeping) {
					log(LogType.Info, LogSender.AutoFarm, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.AutoFarm.Active) {
					// If not enough slots are free, the farmer cannot run.
					userData.slots = UpdateSlots();

					int freeSlots = userData.slots.Free;
					int slotsToLeaveFree = (int) settings.AutoFarm.SlotsToLeaveFree;

					if (freeSlots > slotsToLeaveFree) {
						try {
							// Prune all reports older than KeepReportFor and all reports of state AttackSent: information no longer actual.
							var newTime = GetDateTime();
							var removeReports = userData.farmTargets.Where(t => t.State == FarmState.AttackSent || (t.Report != null && DateTime.Compare(t.Report.Date.AddMinutes((double) settings.AutoFarm.KeepReportFor), GetDateTime()) < 0)).ToList();
							foreach (var remove in removeReports) {
								var updateReport = remove;
								updateReport.State = FarmState.ProbesPending;
								updateReport.Report = null;
								userData.farmTargets.Remove(remove);
								userData.farmTargets.Add(updateReport);
							}

							// Keep local record of userData.celestials, to be updated by autofarmer itself, to reduce ogamed calls.
							var localCelestials = UpdateCelestials();
							Dictionary<int, long> celestialProbes = new Dictionary<int, long>();
							foreach (var celestial in localCelestials) {
								Celestial tempCelestial = UpdatePlanet(celestial, UpdateTypes.Fast);
								tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Ships);
								celestialProbes.Add(tempCelestial.ID, tempCelestial.Ships.EspionageProbe);
							}

							// Keep track of number of targets probed.
							int numProbed = 0;

							/// Galaxy scanning + target probing.
							log(LogType.Info, LogSender.AutoFarm, "Detecting farm targets...");
							foreach (var range in settings.AutoFarm.ScanRange) {
								if (SettingsService.IsSettingSet(settings.AutoFarm, "TargetsProbedBeforeAttack") && ((int)settings.AutoFarm.TargetsProbedBeforeAttack != 0) && numProbed >= (int) settings.AutoFarm.TargetsProbedBeforeAttack)
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
											log(LogType.Info, LogSender.AutoFarm, $"Skipping system {system.ToString()}: system in exclude list.");
											excludeSystem = true;
											break;
										}
									}
									if (excludeSystem == true)
										continue;

									var galaxyInfo = ogamedService.GetGalaxyInfo(galaxy, system);
									var planets = galaxyInfo.Planets.Where(p => p != null && p.Inactive && !p.Administrator && !p.Banned && !p.Vacation);
									List<Celestial> scannedTargets = planets.Cast<Celestial>().ToList();

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
											log(LogType.Info, LogSender.AutoFarm, "Maximum number of targets to probe reached, proceeding to attack.");
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
												log(LogType.Info, LogSender.AutoFarm, $"Skipping {planet.ToString()}: celestial in exclude list.");
												excludePlanet = true;
												break;
											}
										}
										if (excludePlanet == true)
											continue;

										// Check if planet with coordinates exists already in userData.farmTargets list.
										var exists = userData.farmTargets.Where(t => t != null && t.Celestial.HasCoords(planet.Coordinate));
										if (exists.Count() > 1) {
											// BUG: Same coordinates should never appear multiple times in userData.farmTargets. The list should only contain unique coordinates.
											log(LogType.Warning, LogSender.AutoFarm, "BUG: Same coordinates appeared multiple times within userData.farmTargets!");
											return;
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
										List<Celestial> tempCelestials = (settings.AutoFarm.Origin.Length > 0) ? Helpers.ParseCelestialsList(settings.AutoFarm.Origin, userData.celestials) : userData.celestials;
										List<Celestial> closestCelestials = tempCelestials
											.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
											.OrderBy(c => Helpers.CalcDistance(c.Coordinate, target.Celestial.Coordinate, userData.serverData)).ToList();

										foreach (var closest in closestCelestials) {
											int neededProbes = (int) settings.AutoFarm.NumProbes;
											if (target.State == FarmState.ProbesRequired)
												neededProbes *= 3;
											if (target.State == FarmState.FailedProbesRequired)
												neededProbes *= 9;

											// If local record indicate not enough espionage probes are available, update record to make sure this is correct.
											if (celestialProbes[closest.ID] < neededProbes) {
												var tempCelestial = UpdatePlanet(closest, UpdateTypes.Ships);
												celestialProbes.Remove(closest.ID);
												celestialProbes.Add(closest.ID, tempCelestial.Ships.EspionageProbe);
											}

											// Check if probes are available: If not, wait for them.
											if (celestialProbes[closest.ID] < neededProbes) {
												// Wait for probes to come back to current celestial. If no on-route, continue to next iteration.
												userData.fleets = UpdateFleets();
												var espionageMissions = Helpers.GetMissionsInProgress(closest.Coordinate, Missions.Spy, userData.fleets);
												if (espionageMissions.Any()) {
													var returningProbes = espionageMissions.Sum(f => f.Ships.EspionageProbe);
													if (celestialProbes[closest.ID] + returningProbes >= neededProbes) {
														var returningFleets = espionageMissions.OrderBy(f => f.BackIn).ToArray();
														long probesCount = 0;
														for (int i = 0; i <= returningFleets.Length; i++) {
															probesCount += returningFleets[i].Ships.EspionageProbe;
															if (probesCount >= neededProbes) {
																int interval = (int) ((1000 * returningFleets[i].BackIn) + Helpers.CalcRandomInterval(IntervalType.LessThanASecond));
																log(LogType.Info, LogSender.AutoFarm, $"Waiting for probes to return...");
																await Task.Delay(interval);
																freeSlots++;
																break;
															}
														}
													}
												} else {
													log(LogType.Warning, LogSender.AutoFarm, $"Cannot spy {target.Celestial.Coordinate.ToString()} from {closest.Coordinate.ToString()}, insufficient probes ({celestialProbes[closest.ID]}/{neededProbes}).");
													continue;
												}
											}

											if (freeSlots <= slotsToLeaveFree) {
												userData.slots = UpdateSlots();
												freeSlots = userData.slots.Free;
											}

											userData.fleets = UpdateFleets();
											while (freeSlots <= slotsToLeaveFree) {
												// No slots available, wait for first fleet of any mission type to return.
												userData.fleets = UpdateFleets();
												if (userData.fleets.Any()) {
													int interval = (int) ((1000 * userData.fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + Helpers.CalcRandomInterval(IntervalType.LessThanASecond));
													log(LogType.Info, LogSender.AutoFarm, $"Out of fleet slots. Waiting for fleet to return...");
													await Task.Delay(interval);
													userData.slots = UpdateSlots();
													freeSlots = userData.slots.Free;
												} else {
													log(LogType.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
													return;
												}
											}

											if (Helpers.GetMissionsInProgress(closest.Coordinate, Missions.Spy, userData.fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate))) {
												log(LogType.Warning, LogSender.AutoFarm, $"Probes already on route towards {target.ToString()}.");
												break;
											}
											if (Helpers.GetMissionsInProgress(closest.Coordinate, Missions.Attack, userData.fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate) && f.ReturnFlight == false)) {
												log(LogType.Warning, LogSender.AutoFarm, $"Attack already on route towards {target.ToString()}.");
												break;
											}

											// If local record indicate not enough espionage probes are available, update record to make sure this is correct.
											if (celestialProbes[closest.ID] < neededProbes) {
												var tempCelestial = UpdatePlanet(closest, UpdateTypes.Ships);
												celestialProbes.Remove(closest.ID);
												celestialProbes.Add(closest.ID, tempCelestial.Ships.EspionageProbe);
											}

											if (celestialProbes[closest.ID] >= neededProbes) {
												Ships ships = new();
												ships.Add(Buildables.EspionageProbe, neededProbes);

												log(LogType.Info, LogSender.AutoFarm, $"Spying {target.ToString()} from {closest.ToString()} with {neededProbes} probes.");

												userData.slots = UpdateSlots();
												var fleetId = SendFleet(closest, ships, target.Celestial.Coordinate, Missions.Spy, Speeds.HundredPercent);
												if (fleetId > (int) SendFleetCode.GenericError) {
													freeSlots--;
													numProbed++;
													celestialProbes[closest.ID] -= neededProbes;

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
												log(LogType.Warning, LogSender.AutoFarm, $"Insufficient probes ({celestialProbes[closest.ID]}/{neededProbes}).");
												if (SettingsService.IsSettingSet(settings.AutoFarm, "BuildProbes") && settings.AutoFarm.BuildProbes == true) {
													var buildProbes = neededProbes - celestialProbes[closest.ID];
													var cost = Helpers.CalcPrice(Buildables.EspionageProbe, (int) buildProbes);
													var tempCelestial = UpdatePlanet(closest, UpdateTypes.Resources);
													if (tempCelestial.Resources.IsEnoughFor(cost)) {
														log(LogType.Info, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {buildProbes}x{Buildables.EspionageProbe.ToString()}");
													} else {
														var buildableProbes = Helpers.CalcMaxBuildableNumber(Buildables.EspionageProbe, tempCelestial.Resources);
														log(LogType.Warning, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Not enough resources to build {buildProbes}x{Buildables.EspionageProbe.ToString()}. {buildableProbes} will be built instead.");
														buildProbes = buildableProbes;
													}

													var result = ogamedService.BuildShips(tempCelestial, Buildables.EspionageProbe, buildProbes);
													if (result) {
														tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
														int interval = (int) (Helpers.CalcProductionTime(Buildables.EspionageProbe, (int) buildProbes, userData.serverData, tempCelestial.Facilities) + Helpers.CalcRandomInterval(IntervalType.AFewSeconds)) * 1000;
														log(LogType.Info, LogSender.AutoFarm, "Production succesfully started. Waiting for build order to finish...");
														await Task.Delay(interval);
													} else {
														log(LogType.Warning, LogSender.AutoFarm, "Unable to start ship production.");
													}
												}
												break;
											}
										}
									}
								}
							}
						} catch (Exception e) {
							log(LogType.Debug, LogSender.AutoFarm, $"Exception: {e.Message}");
							log(LogType.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
							log(LogType.Warning, LogSender.AutoFarm, "Unable to parse scan range");
						}

						// Wait for all espionage fleets to return.
						userData.fleets = UpdateFleets();
						Fleet firstReturning = Helpers.GetFirstReturningEspionage(userData.fleets);
						if (firstReturning != null) {
							int interval = (int) ((1000 * firstReturning.BackIn) + Helpers.CalcRandomInterval(IntervalType.AFewSeconds));
							log(LogType.Info, LogSender.AutoFarm, $"Waiting for probes to return...");
							await Task.Delay(interval);
						}

						log(LogType.Info, LogSender.AutoFarm, "Processing espionage reports of found inactives...");

						/// Process reports.
						AutoFarmProcessReports();

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
							log(LogType.Info, LogSender.AutoFarm, "Attacking suitable farm targets...");
						} else {
							log(LogType.Info, LogSender.AutoFarm, "No suitable targets found.");
							return;
						}

						Buildables cargoShip = Buildables.LargeCargo;
						if (!Enum.TryParse<Buildables>((string) settings.AutoFarm.CargoType, true, out cargoShip)) {
							log(LogType.Warning, LogSender.AutoFarm, "Unable to parse cargoShip. Falling back to default LargeCargo");
							cargoShip = Buildables.LargeCargo;
						}
						if (cargoShip == Buildables.Null) {
							log(LogType.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip is Null");
							return;
						}
						if (cargoShip == Buildables.EspionageProbe && userData.serverData.ProbeCargo == 0) {
							log(LogType.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip set to EspionageProbe, but this universe does not have probe cargo.");
							return;
						}

						userData.researches = UpdateResearches();
						userData.celestials = UpdateCelestials();
						int attackTargetsCount = 0;
						decimal lootFuelRatio = SettingsService.IsSettingSet(settings.AutoFarm, "MinLootFuelRatio") ? (decimal) settings.AutoFarm.MinLootFuelRatio : (decimal) 0.0001;
						decimal speed = 0;
						foreach (FarmTarget target in attackTargets) {
							attackTargetsCount++;
							log(LogType.Info, LogSender.AutoFarm, $"Attacking target {attackTargetsCount}/{attackTargets.Count()} at {target.Celestial.Coordinate.ToString()} for {target.Report.Loot(userData.userInfo.Class).TransportableResources}.");
							var loot = target.Report.Loot(userData.userInfo.Class);
							var numCargo = Helpers.CalcShipNumberForPayload(loot, cargoShip, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);
							if (SettingsService.IsSettingSet(settings.AutoFarm, "CargoSurplusPercentage") && (double) settings.AutoFarm.CargoSurplusPercentage > 0) {
								numCargo = (long) Math.Round(numCargo + (numCargo / 100 * (double) settings.AutoFarm.CargoSurplusPercentage), 0);
							}
							var attackingShips = new Ships().Add(cargoShip, numCargo);

							List<Celestial> tempCelestials = (settings.AutoFarm.Origin.Length > 0) ? Helpers.ParseCelestialsList(settings.AutoFarm.Origin, userData.celestials) : userData.celestials;
							List<Celestial> closestCelestials = tempCelestials
								.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
								.OrderBy(c => Helpers.CalcDistance(c.Coordinate, target.Celestial.Coordinate, userData.serverData))
								.ToList();

							Celestial fromCelestial = null;
							foreach (var c in closestCelestials) {
								var tempCelestial = UpdatePlanet(c, UpdateTypes.Ships);
								tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Resources);
								if (tempCelestial.Ships != null && tempCelestial.Ships.GetAmount(cargoShip) >= (numCargo + settings.AutoFarm.MinCargosToKeep)) {
									// TODO Future: If fleet composition is changed, update ships passed to CalcFlightTime.
									speed = 0;
									if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(settings.AutoFarm, "MinLootFuelRatio") && settings.AutoFarm.MinLootFuelRatio != 0) {
										long maxFlightTime = SettingsService.IsSettingSet(settings.AutoFarm, "MaxFlightTime") ? (long) settings.AutoFarm.MaxFlightTime : 86400;
										var optimalSpeed = Helpers.CalcOptimalFarmSpeed(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(userData.userInfo.Class), lootFuelRatio, maxFlightTime, userData.researches, userData.serverData, userData.userInfo.Class);
										if (optimalSpeed == 0) {
											log(LogType.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

										} else {
											log(LogType.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
											speed = optimalSpeed;
										}
									}
									if (speed == 0) {
										if (SettingsService.IsSettingSet(settings.AutoFarm, "FleetSpeed") && settings.AutoFarm.FleetSpeed > 0) {
											speed = (int) settings.AutoFarm.FleetSpeed / 10;
											if (!Helpers.GetValidSpeedsForClass(userData.userInfo.Class).Any(s => s == speed)) {
												log(LogType.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
												speed = Speeds.HundredPercent;
											}
										} else {
											speed = Speeds.HundredPercent;
										}
									}
									FleetPrediction prediction = Helpers.CalcFleetPrediction(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, userData.researches, userData.serverData, userData.userInfo.Class);

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
								log(LogType.Info, LogSender.AutoFarm, $"No origin celestial available near destination {target.Celestial.ToString()} with enough cargo ships.");
								// TODO Future: If prefered cargo ship is not available or not sufficient capacity, combine with other cargo type.
								foreach (var closest in closestCelestials) {
									Celestial tempCelestial = closest;
									tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Ships);
									tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Resources);
									// TODO Future: If fleet composition is changed, update ships passed to CalcFlightTime.
									speed = 0;
									if (SettingsService.IsSettingSet(settings.AutoFarm, "FleetSpeed") && settings.AutoFarm.FleetSpeed > 0) {
										speed = (int) settings.AutoFarm.FleetSpeed / 10;
										if (!Helpers.GetValidSpeedsForClass(userData.userInfo.Class).Any(s => s == speed)) {
											log(LogType.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
											speed = Speeds.HundredPercent;
										}
									} else {
										speed = 0;
										if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(settings.AutoFarm, "MinLootFuelRatio") && settings.AutoFarm.MinLootFuelRatio != 0) {
											long maxFlightTime = SettingsService.IsSettingSet(settings.AutoFarm, "MaxFlightTime") ? (long) settings.AutoFarm.MaxFlightTime : 86400;
											var optimalSpeed = Helpers.CalcOptimalFarmSpeed(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(userData.userInfo.Class), lootFuelRatio, maxFlightTime, userData.researches, userData.serverData, userData.userInfo.Class);
											if (optimalSpeed == 0) {
												log(LogType.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

											} else {
												log(LogType.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
												speed = optimalSpeed;
											}
										}
										if (speed == 0) {
											if (SettingsService.IsSettingSet(settings.AutoFarm, "FleetSpeed") && settings.AutoFarm.FleetSpeed > 0) {
												speed = (int) settings.AutoFarm.FleetSpeed / 10;
												if (!Helpers.GetValidSpeedsForClass(userData.userInfo.Class).Any(s => s == speed)) {
													log(LogType.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
													speed = Speeds.HundredPercent;
												}
											} else {
												speed = Speeds.HundredPercent;
											}
										}
									}
									FleetPrediction prediction = Helpers.CalcFleetPrediction(tempCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, Missions.Attack, speed, userData.researches, userData.serverData, userData.userInfo.Class);

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
											var cost = Helpers.CalcPrice(cargoShip, (int) neededCargos);
											if (tempCelestial.Resources.IsEnoughFor(cost)) {
												log(LogType.Info, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {neededCargos}x{cargoShip.ToString()}");
											} else {
												var buildableCargos = Helpers.CalcMaxBuildableNumber(cargoShip, tempCelestial.Resources);
												log(LogType.Warning, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{cargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
												neededCargos = buildableCargos;
											}

											var result = ogamedService.BuildShips(tempCelestial, cargoShip, neededCargos);
											if (result) {
												tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Facilities);
												int interval = (int) (Helpers.CalcProductionTime(cargoShip, (int) neededCargos, userData.serverData, tempCelestial.Facilities) + Helpers.CalcRandomInterval(IntervalType.AFewSeconds)) * 1000;
												log(LogType.Info, LogSender.AutoFarm, "Production succesfully started. Waiting for build order to finish...");
												await Task.Delay(interval);
											} else {
												log(LogType.Warning, LogSender.AutoFarm, "Unable to start ship production.");
											}
										}

										if (tempCelestial.Ships.GetAmount(cargoShip) - (long) settings.AutoFarm.MinCargosToKeep < (long) settings.AutoFarm.MinCargosToSend) {
											log(LogType.Info, LogSender.AutoFarm, $"Insufficient {cargoShip.ToString()} on {tempCelestial.Coordinate}, require {numCargo + (long) settings.AutoFarm.MinCargosToKeep} {cargoShip.ToString()}.");
											continue;
										}

										numCargo = tempCelestial.Ships.GetAmount(cargoShip) - (long) settings.AutoFarm.MinCargosToKeep;
										fromCelestial = tempCelestial;
										break;
									}
								}
							}

							if (fromCelestial == null) {
								log(LogType.Info, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. No suitable origin celestial available near the destination.");
								continue;
							}

							// Only execute update slots if our local copy indicates we have run out.
							if (freeSlots <= slotsToLeaveFree) {
								userData.slots = UpdateSlots();
								freeSlots = userData.slots.Free;
							}

							while (freeSlots <= slotsToLeaveFree) {
								userData.fleets = UpdateFleets();
								// No slots free, wait for first fleet to come back.
								if (userData.fleets.Any()) {
									int interval = (int) ((1000 * userData.fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + Helpers.CalcRandomInterval(IntervalType.AFewSeconds));
									if (SettingsService.IsSettingSet(settings.AutoFarm, "MaxWaitTime") && (int) settings.AutoFarm.MaxWaitTime != 0 && interval > (int) settings.AutoFarm.MaxWaitTime) {
										log(LogType.Info, LogSender.AutoFarm, $"Out of fleet slots. Time to wait greater than set {(int) settings.AutoFarm.MaxWaitTime} seconds. Stopping autofarm.");
										return;
									} else {
										log(LogType.Info, LogSender.AutoFarm, "Out of fleet slots. Waiting for first fleet to return...");
										await Task.Delay(interval);
										userData.slots = UpdateSlots();
										freeSlots = userData.slots.Free;
									}
								} else {
									log(LogType.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
									return;
								}
							}

							if (userData.slots.Free > slotsToLeaveFree) {
								log(LogType.Info, LogSender.AutoFarm, $"Attacking {target.ToString()} from {fromCelestial} with {numCargo} {cargoShip.ToString()}.");
								Ships ships = new();

								speed = 0;
								if (/*cargoShip == Buildables.EspionageProbe &&*/ SettingsService.IsSettingSet(settings.AutoFarm, "MinLootFuelRatio") && settings.AutoFarm.MinLootFuelRatio != 0) {
									long maxFlightTime = SettingsService.IsSettingSet(settings.AutoFarm, "MaxFlightTime") ? (long) settings.AutoFarm.MaxFlightTime : 86400;
									var optimalSpeed = Helpers.CalcOptimalFarmSpeed(fromCelestial.Coordinate, target.Celestial.Coordinate, attackingShips, target.Report.Loot(userData.userInfo.Class), lootFuelRatio, maxFlightTime, userData.researches, userData.serverData, userData.userInfo.Class);
									if (optimalSpeed == 0) {
										log(LogType.Debug, LogSender.AutoFarm, $"Unable to calculate a valid optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");

									} else {
										log(LogType.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
										speed = optimalSpeed;
									}
								}
								if (speed == 0) {
									if (SettingsService.IsSettingSet(settings.AutoFarm, "FleetSpeed") && settings.AutoFarm.FleetSpeed > 0) {
										speed = (int) settings.AutoFarm.FleetSpeed / 10;
										if (!Helpers.GetValidSpeedsForClass(userData.userInfo.Class).Any(s => s == speed)) {
											log(LogType.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
											speed = Speeds.HundredPercent;
										}
									} else {
										speed = Speeds.HundredPercent;
									}
								}

								var fleetId = SendFleet(fromCelestial, attackingShips, target.Celestial.Coordinate, Missions.Attack, speed);

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
								log(LogType.Info, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. {userData.slots.Free} free, {slotsToLeaveFree} required.");
								return;
							}
						}
					} else {
						log(LogType.Warning, LogSender.FleetScheduler, "Unable to start auto farm, no slots available");
						return;
					}
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.AutoFarm, $"AutoFarm Exception: {e.Message}");
				log(LogType.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
			} finally {
				log(LogType.Info, LogSender.AutoFarm, $"Attacked targets: {userData.farmTargets.Where(t => t.State == FarmState.AttackSent).Count()}");
				if (!userData.isSleeping) {
					if (stop) {
						log(LogType.Info, LogSender.AutoFarm, $"Stopping feature.");
					} else {
						var time = GetDateTime();
						var interval = Helpers.CalcRandomInterval((int) settings.AutoFarm.CheckIntervalMin, (int) settings.AutoFarm.CheckIntervalMax);
						if (interval <= 0)
							interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoFarmTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.AutoFarm, $"Next autofarm check at {newTime.ToString()}");
						CheckCelestials();
					}

					xaSem[Feature.AutoFarm].Release();
				}
			}
		}

		/// <summary>
		/// Checks all received espionage reports and updates userData.farmTargets to reflect latest data retrieved from reports.
		/// </summary>
		private void AutoFarmProcessReports() {
			// TODO Future: Read espionage reports in separate thread (concurently with probing itself).
			// TODO Future: Check if probes were destroyed, blacklist target if so to avoid additional kills.
			List<EspionageReportSummary> summaryReports = ogamedService.GetEspionageReports();
			foreach (var summary in summaryReports) {
				if (summary.Type == EspionageReportType.Action)
					continue;

				try {
					var report = ogamedService.GetEspionageReport(summary.ID);
					if (DateTime.Compare(report.Date.AddMinutes((double) settings.AutoFarm.KeepReportFor), GetDateTime()) < 0) {
						ogamedService.DeleteReport(report.ID);
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
							ogamedService.DeleteReport(report.ID);
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

								log(LogType.Info, LogSender.AutoFarm, $"Need more probes on {report.Coordinate}. Loot: {report.Loot(userData.userInfo.Class)}");
							} else if (report.IsDefenceless()) {
								newFarmTarget.State = FarmState.AttackPending;
								log(LogType.Info, LogSender.AutoFarm, $"Attack pending on {report.Coordinate}. Loot: {report.Loot(userData.userInfo.Class)}");
							} else {
								newFarmTarget.State = FarmState.NotSuitable;
								log(LogType.Info, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - defences present.");
							}
						} else {
							newFarmTarget.State = FarmState.NotSuitable;
							log(LogType.Info, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - insufficient loot ({report.Loot(userData.userInfo.Class)})");
						}

						userData.farmTargets.Remove(target);
						userData.farmTargets.Add(newFarmTarget);
					} else {
						log(LogType.Info, LogSender.AutoFarm, $"Target {report.Coordinate} not scanned by TBot, ignoring...");
					}
				} catch (Exception e) {
					log(LogType.Error, LogSender.AutoFarm, $"AutoFarmProcessReports Exception: {e.Message}");
					log(LogType.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
					continue;
				}
			}

			ogamedService.DeleteAllEspionageReports();
		}

		private async void AutoMine(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogType.Info, LogSender.Brain, "Running automine...");

				if (userData.isSleeping) {
					log(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
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

					List<Celestial> celestialsToExclude = Helpers.ParseCelestialsList(settings.Brain.AutoMine.Exclude, userData.celestials);
					List<Celestial> celestialsToMine = new();
					if (state == null) {
						foreach (Celestial celestial in userData.celestials.Where(p => p is Planet)) {
							var cel = UpdatePlanet(celestial, UpdateTypes.Buildings);
							var nextMine = Helpers.GetNextMineToBuild(cel as Planet, userData.researches, userData.serverData.Speed, 100, 100, 100, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull, true, int.MaxValue);
							var lv = Helpers.GetNextLevel(cel, nextMine);
							var DOIR = Helpers.CalcNextDaysOfInvestmentReturn(cel as Planet, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
							log(LogType.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Mine: {nextMine.ToString()} lv {lv.ToString()}; DOIR: {DOIR.ToString()}.");
							if (DOIR < userData.nextDOIR || userData.nextDOIR == 0) {
								userData.nextDOIR = DOIR;
							}
							celestialsToMine.Add(cel);
						}
						celestialsToMine = celestialsToMine.OrderBy(cel => Helpers.CalcNextDaysOfInvestmentReturn(cel as Planet, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull)).ToList();
						celestialsToMine.AddRange(userData.celestials.Where(c => c is Moon));
					} else {
						celestialsToMine.Add(state as Celestial);
					}

					foreach (Celestial celestial in (bool) settings.Brain.AutoMine.RandomOrder ? celestialsToMine.Shuffle().ToList() : celestialsToMine) {
						if (celestialsToExclude.Has(celestial)) {
							log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}

						AutoMineCelestial(celestial, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
					}
				} else {
					log(LogType.Info, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"AutoMine Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private void AutoMineCelestial(Celestial celestial, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			int fleetId = (int) SendFleetCode.GenericError;
			Buildables buildable = Buildables.Null;
			int level = 0;
			bool started = false;
			bool stop = false;
			bool delay = false;
			long delayBuilding = 0;
			bool delayProduction = false;
			try {
				log(LogType.Info, LogSender.Brain, $"Running AutoMine on {celestial.ToString()}");
				celestial = UpdatePlanet(celestial, UpdateTypes.Fast);
				celestial = UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
				celestial = UpdatePlanet(celestial, UpdateTypes.ResourceSettings);
				celestial = UpdatePlanet(celestial, UpdateTypes.Buildings);
				celestial = UpdatePlanet(celestial, UpdateTypes.Facilities);
				celestial = UpdatePlanet(celestial, UpdateTypes.Constructions);
				celestial = UpdatePlanet(celestial, UpdateTypes.Productions);
				celestial = UpdatePlanet(celestial, UpdateTypes.Ships);
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
					celestial.Ships.Crawler < Helpers.CalcMaxCrawlers(celestial as Planet, userData.userInfo.Class, userData.staff.Geologist) &&
					Helpers.CalcOptimalCrawlers(celestial as Planet, userData.userInfo.Class, userData.staff, userData.researches, userData.serverData) > celestial.Ships.Crawler
				) {
					buildable = Buildables.Crawler;
					level = Helpers.CalcOptimalCrawlers(celestial as Planet, userData.userInfo.Class, userData.staff, userData.researches, userData.serverData);
				} else {
					if (celestial.Fields.Free == 0) {
						log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: not enough fields available.");
						return;
					}
					if (celestial.Constructions.BuildingID != 0) {
						log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a building in production.");
						if (
							celestial is Planet && (
								celestial.Constructions.BuildingID == (int) Buildables.MetalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.CrystalMine ||
								celestial.Constructions.BuildingID == (int) Buildables.DeuteriumSynthesizer
							)
						) {
							var buildingBeingBuilt = (Buildables) celestial.Constructions.BuildingID;

							var levelBeingBuilt = Helpers.GetNextLevel(celestial, buildingBeingBuilt);
							var DOIR = Helpers.CalcDaysOfInvestmentReturn(celestial as Planet, buildingBeingBuilt, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
							if (DOIR > userData.lastDOIR) {
								userData.lastDOIR = DOIR;
							}
						}
						delayBuilding = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						return;
					}

					if (celestial is Planet) {

						buildable = Helpers.GetNextBuildingToBuild(celestial as Planet, userData.researches, maxBuildings, maxFacilities, userData.userInfo.Class, userData.staff, userData.serverData, autoMinerSettings);
						level = Helpers.GetNextLevel(celestial as Planet, buildable, userData.userInfo.Class == CharacterClass.Collector, userData.staff.Engineer, userData.staff.IsFull);
					} else {
						buildable = Helpers.GetNextLunarFacilityToBuild(celestial as Moon, userData.researches, maxLunarFacilities);
						level = Helpers.GetNextLevel(celestial as Moon, buildable, userData.userInfo.Class == CharacterClass.Collector, userData.staff.Engineer, userData.staff.IsFull);
					}
				}

				if (buildable != Buildables.Null && level > 0) {
					log(LogType.Info, LogSender.Brain, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
					if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
						float DOIR = Helpers.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
						log(LogType.Debug, LogSender.Brain, $"Days of investment return: {Math.Round(DOIR, 2).ToString()} days.");
					}

					Resources xCostBuildable = Helpers.CalcPrice(buildable, level);
					if (celestial is Moon)
						xCostBuildable.Deuterium += (long) autoMinerSettings.DeutToLeaveOnMoons;

					if (buildable == Buildables.Terraformer) {
						if (xCostBuildable.Energy > celestial.ResourcesProduction.Energy.CurrentProduction) {
							log(LogType.Info, LogSender.Brain, $"Not enough energy to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							buildable = Buildables.SolarSatellite;
							level = Helpers.CalcNeededSolarSatellites(celestial as Planet, xCostBuildable.Energy - celestial.ResourcesProduction.Energy.CurrentProduction, userData.userInfo.Class == CharacterClass.Collector, userData.staff.Engineer, userData.staff.IsFull);
							xCostBuildable = Helpers.CalcPrice(buildable, level);
						}
					}

					if (celestial.Resources.IsEnoughFor(xCostBuildable)) {
						bool result = false;
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							if (!celestial.HasProduction()) {
								log(LogType.Info, LogSender.Brain, $"Building {level.ToString()} x {buildable.ToString()} on {celestial.ToString()}");
								result = ogamedService.BuildShips(celestial, buildable, level);
							} else {
								log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: There is already a production ongoing.");
								delayProduction = true;
							}
						} else {
							log(LogType.Info, LogSender.Brain, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							result = ogamedService.BuildConstruction(celestial, buildable);
						}

						if (result) {
							if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
								float DOIR = Helpers.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
								if (DOIR > userData.lastDOIR) {
									userData.lastDOIR = DOIR;
								}
							}
							if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
								celestial = UpdatePlanet(celestial, UpdateTypes.Productions);
								try {
									if (celestial.Productions.First().ID == (int) buildable) {
										started = true;
										log(LogType.Info, LogSender.Brain, $"{celestial.Productions.First().Nbr.ToString()}x {buildable.ToString()} succesfully started.");
									} else {
										celestial = UpdatePlanet(celestial, UpdateTypes.Resources);
										if (celestial.Resources.Energy >= 0) {
											started = true;
											log(LogType.Info, LogSender.Brain, $"{level.ToString()}x {buildable.ToString()} succesfully built");
										} else {
											log(LogType.Warning, LogSender.Brain, $"Unable to start {level.ToString()}x {buildable.ToString()} construction: an unknown error has occurred");
										}
									}
								} catch {
									started = true;
									log(LogType.Info, LogSender.Brain, $"Unable to determine if the production has started.");
								}
							} else {
								celestial = UpdatePlanet(celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.BuildingID == (int) buildable) {
									started = true;
									log(LogType.Info, LogSender.Brain, "Building succesfully started.");
								} else {
									celestial = UpdatePlanet(celestial, UpdateTypes.Buildings);
									celestial = UpdatePlanet(celestial, UpdateTypes.Facilities);
									if (celestial.GetLevel(buildable) != level)
										log(LogType.Warning, LogSender.Brain, "Unable to start building construction: an unknown error has occurred");
									else {
										started = true;
										log(LogType.Info, LogSender.Brain, "Building succesfully started.");
									}
								}
							}
						} else if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler)
							log(LogType.Warning, LogSender.Brain, "Unable to start building construction: a network error has occurred");
					} else {
						if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
							float DOIR = Helpers.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
							if (DOIR < userData.nextDOIR || userData.nextDOIR == 0) {
								userData.nextDOIR = DOIR;
							}
						}
						if (buildable == Buildables.SolarSatellite || buildable == Buildables.Crawler) {
							log(LogType.Info, LogSender.Brain, $"Not enough resources to build: {level.ToString()}x {buildable.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");

						} else {
							log(LogType.Info, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.TransportableResources} - Available: {celestial.Resources.TransportableResources}");
						}
						if ((bool) settings.Brain.AutoMine.Transports.Active) {
							userData.fleets = UpdateFleets();
							if (!Helpers.IsThereTransportTowardsCelestial(celestial, userData.fleets)) {
								Celestial origin = userData.celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
										.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
										.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
										.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
										.SingleOrDefault() ?? new() { ID = 0 };
								fleetId = HandleMinerTransport(origin, celestial, xCostBuildable, buildable, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);

								if (fleetId == (int) SendFleetCode.AfterSleepTime) {
									stop = true;
									return;
								}
								if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
									delay = true;
									return;
								}
							} else {
								log(LogType.Info, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
							}
						}
					}
				} else {
					log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build.");
					if (celestial.Coordinate.Type == Celestials.Planet) {
						var nextDOIR = Helpers.CalcNextDaysOfInvestmentReturn(celestial as Planet, userData.researches, userData.serverData.Speed, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull);
						if (
							(celestial as Planet).HasFacilities(maxFacilities) && (
								(celestial as Planet).HasMines(maxBuildings) ||
								nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn
							)
						) {
							if (nextDOIR > autoMinerSettings.MaxDaysOfInvestmentReturn) {
								var nextMine = Helpers.GetNextMineToBuild(celestial as Planet, userData.researches, userData.serverData.Speed, 100, 100, 100, 1, userData.userInfo.Class, userData.staff.Geologist, userData.staff.IsFull, autoMinerSettings.OptimizeForStart, float.MaxValue);
								var nexMineLevel = Helpers.GetNextLevel(celestial, nextMine);
								if (nextDOIR < userData.nextDOIR || userData.nextDOIR == 0) {
									userData.nextDOIR = nextDOIR;
								}
								log(LogType.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine.MaxDaysOfInvestmentReturn to at least {Math.Round(nextDOIR, 2, MidpointRounding.ToPositiveInfinity).ToString()}.");
								log(LogType.Debug, LogSender.Brain, $"Next mine to build: {nextMine.ToString()} lv {nexMineLevel.ToString()}.");

							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								log(LogType.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine mines max levels");
							}
							if ((celestial as Planet).HasMines(maxBuildings)) {
								log(LogType.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine facilities max levels");
							}
							stop = true;
						}
					} else if (celestial.Coordinate.Type == Celestials.Moon) {
						if ((celestial as Moon).HasLunarFacilities(maxLunarFacilities)) {
							log(LogType.Debug, LogSender.Brain, $"To continue building you should rise Brain.AutoMine lunar facilities max levels");
						}
						stop = true;
					}
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"AutoMineCelestial Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = GetDateTime();
				string autoMineTimer = $"AutoMineTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					log(LogType.Info, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}.");
					if (timers.TryGetValue($"AutoMineTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					celestial = UpdatePlanet(celestial, UpdateTypes.Productions);
					celestial = UpdatePlanet(celestial, UpdateTypes.Facilities);
					log(LogType.Info, LogSender.Brain, $"Delaying...");
					time = GetDateTime();
					long interval;
					try {
						interval = Helpers.CalcProductionTime((Buildables) celestial.Productions.First().ID, celestial.Productions.First().Nbr, userData.serverData, celestial.Facilities) * 1000 + Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
					} catch {
						interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					log(LogType.Info, LogSender.Brain, $"Delaying...");
					time = GetDateTime();
					userData.fleets = UpdateFleets();
					long interval;
					try {
						interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (started) {
					long interval = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval > System.Int32.MaxValue ? System.Int32.MaxValue : interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (userData.lastDOIR >= userData.nextDOIR) {
						userData.nextDOIR = 0;
					}
				} else if (delayBuilding > 0) {
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(delayBuilding);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, delayBuilding > System.Int32.MaxValue ? System.Int32.MaxValue : delayBuilding, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else {
					long interval = CalcAutoMineTimer(celestial, buildable, level, started, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);

					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						userData.fleets = UpdateFleets();
						var transportfleet = userData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);

					} else {
						interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
					}

					if (interval == long.MaxValue || interval == long.MinValue)
						interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
					if (userData.lastDOIR >= userData.nextDOIR) {
						userData.nextDOIR = 0;
					}
					//log(LogType.Debug, LogSender.Brain, $"Last DOIR: {Math.Round(userData.lastDOIR, 2)}");
					//log(LogType.Debug, LogSender.Brain, $"Next DOIR: {Math.Round(userData.nextDOIR, 2)}");

				}
			}
		}

		private async void LifeformAutoResearch(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogType.Info, LogSender.Brain, "Running Lifeform autoresearch...");

				if (userData.isSleeping) {
					log(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
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
							var cel = UpdatePlanet(celestial, UpdateTypes.LFBuildings);
							cel = UpdatePlanet(celestial, UpdateTypes.LFTechs);
							cel = UpdatePlanet(celestial, UpdateTypes.Resources);

							if (cel.LFtype == LFTypes.None) {
								log(LogType.Info, LogSender.Brain, $"Skipping {cel.ToString()}: No Lifeform active on this planet.");
								continue;
							}
							var nextLFTechToBuild = Helpers.GetNextLFTechToBuild(cel, maxResearchLevel);
							if (nextLFTechToBuild != LFTechno.None) {
								var level = Helpers.GetNextLevel(cel, nextLFTechToBuild);
								Resources nextLFTechCost = ogamedService.GetPrice(nextLFTechToBuild, level);
								var isLessCostLFTechToBuild = Helpers.GetLessExpensiveLFTechToBuild(ogamedService, cel, nextLFTechCost, maxResearchLevel);
								if (isLessCostLFTechToBuild != LFTechno.None) {
									level = Helpers.GetNextLevel(cel, isLessCostLFTechToBuild);
									nextLFTechToBuild = isLessCostLFTechToBuild;
								}

								log(LogType.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Lifeform Research: {nextLFTechToBuild.ToString()} lv {level.ToString()}.");
								celestialsToMine.Add(celestial);
							} else {
								log(LogType.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: No Next Lifeform technology to build found. All research reached settings MaxResearchLevel ?");
							}

						}
					} else {
						celestialsToMine.Add(state as Celestial);
					}
					foreach (Celestial celestial in celestialsToMine) {
						LifeformAutoResearchCelestial(celestial);
					}
				} else {
					log(LogType.Info, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"Lifeform AutoMine Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private void LifeformAutoResearchCelestial(Celestial celestial) {
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
				log(LogType.Info, LogSender.Brain, $"Running Lifeform AutoResearch on {celestial.ToString()}");
				celestial = UpdatePlanet(celestial, UpdateTypes.Fast);
				celestial = UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = UpdatePlanet(celestial, UpdateTypes.LFTechs);
				celestial = UpdatePlanet(celestial, UpdateTypes.Constructions);

				if (celestial.Constructions.LFResearchID != 0) {
					log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a Lifeform research in production.");
					delayProduction = true;
					delayTime = (long) celestial.Constructions.LFResearchCountdown * (long) 1000 + (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
					return;
				}
				int maxResearchLevel = (int) settings.Brain.LifeformAutoResearch.MaxResearchLevel;
				if (celestial is Planet) {
					buildable = Helpers.GetNextLFTechToBuild(celestial, maxResearchLevel);

					if (buildable != LFTechno.None) {
						level = Helpers.GetNextLevel(celestial, buildable);
						Resources nextLFTechCost = ogamedService.GetPrice(buildable, level);
						var isLessCostLFTechToBuild = Helpers.GetLessExpensiveLFTechToBuild(ogamedService, celestial, nextLFTechCost, maxResearchLevel);
						if (isLessCostLFTechToBuild != LFTechno.None) {
							level = Helpers.GetNextLevel(celestial, isLessCostLFTechToBuild);
							buildable = isLessCostLFTechToBuild;
						}
						log(LogType.Info, LogSender.Brain, $"Best Lifeform Research for {celestial.ToString()}: {buildable.ToString()}");

						Resources xCostBuildable = ogamedService.GetPrice(buildable, level);

						if (celestial.Resources.IsEnoughFor(xCostBuildable)) {
							bool result = false;
							log(LogType.Info, LogSender.Brain, $"Lifeform Research {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							result = ogamedService.BuildCancelable(celestial, (LFTechno) buildable);

							if (result) {
								celestial = UpdatePlanet(celestial, UpdateTypes.Constructions);
								if (celestial.Constructions.LFResearchID == (int) buildable) {
									started = true;
									log(LogType.Info, LogSender.Brain, "Lifeform Research succesfully started.");
								} else {
									celestial = UpdatePlanet(celestial, UpdateTypes.LFTechs);
									if (celestial.GetLevel(buildable) != level)
										log(LogType.Warning, LogSender.Brain, "Unable to start Lifeform Research construction: an unknown error has occurred");
									else {
										started = true;
										log(LogType.Info, LogSender.Brain, "Lifeform Research succesfully started.");
									}
								}

							} else {
								log(LogType.Warning, LogSender.Brain, "Unable to start Lifeform Research: a network error has occurred");
							}
						} else {
							log(LogType.Info, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.LFBuildingCostResources.ToString()} - Available: {celestial.Resources.LFBuildingCostResources.ToString()}");

							if ((bool) settings.Brain.LifeformAutoResearch.Transports.Active) {
								userData.fleets = UpdateFleets();
								if (!Helpers.IsThereTransportTowardsCelestial(celestial, userData.fleets)) {
									Celestial origin = userData.celestials
											.Unique()
											.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
											.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
											.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
											.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
											.SingleOrDefault() ?? new() { ID = 0 };
									fleetId = HandleMinerTransport(origin, celestial, xCostBuildable);
									if (fleetId == (int) SendFleetCode.AfterSleepTime) {
										stop = true;
										return;
									}
									if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
										delay = true;
										return;
									}
								} else {
									log(LogType.Info, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
								}
							}
						}
					} else {
						log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build. All research reached settings MaxResearchLevel ?");
						stop = true;
					}
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"LifeformAutoResearch Celestial Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = GetDateTime();
				string autoMineTimer = $"LifeformAutoResearchTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					log(LogType.Info, LogSender.Brain, $"Stopping Lifeform AutoResearch check for {celestial.ToString()}.");
					if (timers.TryGetValue($"LifeformAutoResearchTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					log(LogType.Info, LogSender.Brain, $"Delaying...");
					time = GetDateTime();
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(delayTime);
					timers.Add(autoMineTimer, new Timer(LifeformAutoResearch, celestial, delayTime, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform Research check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					log(LogType.Info, LogSender.Brain, $"Delaying...");
					time = GetDateTime();
					userData.fleets = UpdateFleets();
					try {
						interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = Helpers.CalcRandomInterval((int) settings.Brain.LifeformAutoResearch.CheckIntervalMin, (int) settings.Brain.LifeformAutoResearch.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoResearch, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (started) {
					interval = ((long) celestial.Constructions.LFResearchCountdown * (long) 1000) + (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
					if (interval == long.MaxValue || interval == long.MinValue)
						interval = Helpers.CalcRandomInterval((int) settings.Brain.LifeformAutoResearch.CheckIntervalMin, (int) settings.Brain.LifeformAutoResearch.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoResearch, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");
				} else {
					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						userData.fleets = UpdateFleets();
						var transportfleet = userData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					} else {
						interval = Helpers.CalcRandomInterval((int) settings.Brain.LifeformAutoMine.CheckIntervalMin, (int) settings.Brain.LifeformAutoMine.CheckIntervalMax);
					}

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoResearch, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform AutoResearch check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}

		private async void LifeformAutoMine(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogType.Info, LogSender.Brain, "Running Lifeform automine...");

				if (userData.isSleeping) {
					log(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
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
							var cel = UpdatePlanet(celestial, UpdateTypes.Buildings);

							if ((int) settings.Brain.LifeformAutoMine.StartFromCrystalMineLvl > (int) cel.Buildings.CrystalMine) {
								log(LogType.Debug, LogSender.Brain, $"Celestial {cel.ToString()} did not reach required CrystalMine level. Skipping..");
								continue;
							}
							int maxTechFactory = (int) settings.Brain.LifeformAutoMine.MaxBaseTechBuilding;
							int maxPopuFactory = (int) settings.Brain.LifeformAutoMine.MaxBaseFoodBuilding;
							int maxFoodFactory = (int) settings.Brain.LifeformAutoMine.MaxBasePopulationBuilding;

							cel = UpdatePlanet(celestial, UpdateTypes.LFBuildings);
							cel = UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
							var nextLFBuilding = Helpers.GetNextLFBuildingToBuild(ogamedService, cel, maxPopuFactory, maxFoodFactory, maxTechFactory);
							if (nextLFBuilding != LFBuildables.None) {
								var lv = Helpers.GetNextLevel(celestial, nextLFBuilding);
								log(LogType.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: Next Mine: {nextLFBuilding.ToString()} lv {lv.ToString()}.");

								celestialsToMine.Add(celestial);
							} else {
								log(LogType.Debug, LogSender.Brain, $"Celestial {cel.ToString()}: No Next Lifeform building to build found.");
							}
						}
					} else {
						celestialsToMine.Add(state as Celestial);
					}

					foreach (Celestial celestial in celestialsToMine) {
						LifeformAutoMineCelestial(celestial);
					}
				} else {
					log(LogType.Info, LogSender.Brain, "Skipping: feature disabled");
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"Lifeform AutoMine Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private void LifeformAutoMineCelestial(Celestial celestial) {
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

				log(LogType.Info, LogSender.Brain, $"Running Lifeform AutoMine on {celestial.ToString()}");
				celestial = UpdatePlanet(celestial, UpdateTypes.Resources);
				celestial = UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
				celestial = UpdatePlanet(celestial, UpdateTypes.LFBuildings);
				celestial = UpdatePlanet(celestial, UpdateTypes.Buildings);
				celestial = UpdatePlanet(celestial, UpdateTypes.Constructions);

				if (celestial.Constructions.LFBuildingID != 0 || celestial.Constructions.BuildingID == (int) Buildables.RoboticsFactory || celestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
					log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a building (LF, robotic or nanite) in production.");
					delayProduction = true;
					if (celestial.Constructions.LFBuildingID != 0) {
						delayTime = (long) celestial.Constructions.LFBuildingCountdown * (long) 1000 + (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
					} else {
						delayTime = (long) celestial.Constructions.BuildingCountdown * (long) 1000 + (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
					}
				}
				if (delayTime == 0) {
					if (celestial is Planet) {
						buildable = Helpers.GetNextLFBuildingToBuild(ogamedService, celestial, maxPopuFactory, maxFoodFactory, maxTechFactory);

						if (buildable != LFBuildables.None) {
							level = Helpers.GetNextLevel(celestial, buildable);
							log(LogType.Info, LogSender.Brain, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
							Resources xCostBuildable = ogamedService.GetPrice(buildable, level);

							if (celestial.Resources.IsBuildable(xCostBuildable)) {
								bool result = false;
								log(LogType.Info, LogSender.Brain, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
								result = ogamedService.BuildCancelable(celestial, buildable);

								if (result) {
									celestial = UpdatePlanet(celestial, UpdateTypes.Constructions);
									if (celestial.Constructions.LFBuildingID == (int) buildable) {
										started = true;
										log(LogType.Info, LogSender.Brain, "Building succesfully started.");
									} else {
										celestial = UpdatePlanet(celestial, UpdateTypes.LFBuildings);
										if (celestial.GetLevel(buildable) != level)
											log(LogType.Warning, LogSender.Brain, "Unable to start building construction: an unknown error has occurred");
										else {
											started = true;
											log(LogType.Info, LogSender.Brain, "Building succesfully started.");
										}
									}

								} else {
									log(LogType.Warning, LogSender.Brain, "Unable to start building construction: a network error has occurred");
								}
							} else {
								log(LogType.Info, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}. Needed: {xCostBuildable.LFBuildingCostResources.ToString()} - Available: {celestial.Resources.LFBuildingCostResources.ToString()}");

								if ((bool) settings.Brain.LifeformAutoMine.Transports.Active) {
									userData.fleets = UpdateFleets();
									if (!Helpers.IsThereTransportTowardsCelestial(celestial, userData.fleets)) {
										Celestial origin = userData.celestials
												.Unique()
												.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
												.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
												.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
												.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
												.SingleOrDefault() ?? new() { ID = 0 };
										fleetId = HandleMinerTransport(origin, celestial, xCostBuildable);
										if (fleetId == (int) SendFleetCode.AfterSleepTime) {
											stop = true;
											return;
										}
										if (fleetId == (int) SendFleetCode.NotEnoughSlots) {
											delay = true;
											return;
										}
									} else {
										log(LogType.Info, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
									}
								}
							}
						} else {
							log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build. Check max Lifeform base building max level in settings file?");
							stop = true;
						}
					}
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"LifeformAutoMine Celestial Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = GetDateTime();
				string autoMineTimer = $"LifeformAutoMineTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					log(LogType.Info, LogSender.Brain, $"Stopping Lifeform AutoMine check for {celestial.ToString()}.");
					if (timers.TryGetValue($"LifeformAutoMineTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else if (delayProduction) {
					log(LogType.Info, LogSender.Brain, $"Delaying...");
					time = GetDateTime();
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(delayTime);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, delayTime, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				} else if (delay) {
					log(LogType.Info, LogSender.Brain, $"Delaying...");
					time = GetDateTime();
					userData.fleets = UpdateFleets();
					try {
						interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					} catch {
						interval = Helpers.CalcRandomInterval((int) settings.Brain.LifeformAutoMine.CheckIntervalMin, (int) settings.Brain.LifeformAutoMine.CheckIntervalMax);
					}
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (started) {
					interval = ((long) celestial.Constructions.LFBuildingCountdown * (long) 1000) + (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
					if (interval == long.MaxValue || interval == long.MinValue)
						interval = Helpers.CalcRandomInterval((int) settings.Brain.LifeformAutoMine.CheckIntervalMin, (int) settings.Brain.LifeformAutoMine.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else if (delayTime > 0) {
					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(delayTime);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, delayTime, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");

				} else {
					if (fleetId != 0 && fleetId != -1 && fleetId != -2) {
						userData.fleets = UpdateFleets();
						var transportfleet = userData.fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
						interval = (transportfleet.ArriveIn * 1000) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					} else {
						interval = Helpers.CalcRandomInterval((int) settings.Brain.LifeformAutoMine.CheckIntervalMin, (int) settings.Brain.LifeformAutoMine.CheckIntervalMax);
					}

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(LifeformAutoMine, celestial, interval, Timeout.Infinite));
					log(LogType.Info, LogSender.Brain, $"Next Lifeform AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}

		private long CalcAutoMineTimer(Celestial celestial, Buildables buildable, int level, bool started, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			long interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
			try {
				if (celestial.Fields.Free == 0) {
					interval = long.MaxValue;
					log(LogType.Info, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}: not enough fields available.");
				}

				celestial = UpdatePlanet(celestial, UpdateTypes.Constructions);
				if (started) {
					if (buildable == Buildables.SolarSatellite) {
						celestial = UpdatePlanet(celestial, UpdateTypes.Productions);
						celestial = UpdatePlanet(celestial, UpdateTypes.Facilities);
						interval = Helpers.CalcProductionTime(buildable, level, userData.serverData, celestial.Facilities) * 1000;
					} else if (buildable == Buildables.Crawler) {
						interval = (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
					} else {
						if (celestial.HasConstruction())
							interval = ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
						else
							interval = 0;
					}
				} else if (celestial.HasConstruction()) {
					interval = ((long) celestial.Constructions.BuildingCountdown * (long) 1000) + (long) Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
				} else {
					celestial = UpdatePlanet(celestial, UpdateTypes.Buildings);
					celestial = UpdatePlanet(celestial, UpdateTypes.Facilities);

					if (buildable != Buildables.Null) {
						var price = Helpers.CalcPrice(buildable, level);
						var productionTime = long.MaxValue;
						var transportTime = long.MaxValue;
						var returningExpoTime = long.MaxValue;
						var transportOriginTime = long.MaxValue;
						var returningExpoOriginTime = long.MaxValue;

						celestial = UpdatePlanet(celestial, UpdateTypes.ResourcesProduction);
						DateTime now = GetDateTime();
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
								//log(LogType.Debug, LogSender.Brain, $"The required resources will be produced by {now.AddMilliseconds(productionTime).ToString()}");
							}
						}

						userData.fleets = UpdateFleets();
						var incomingFleets = Helpers.GetIncomingFleetsWithResources(celestial, userData.fleets);
						if (incomingFleets.Any()) {
							var fleet = incomingFleets.First();
							transportTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
							//log(LogType.Debug, LogSender.Brain, $"Next fleet with resources arriving by {now.AddMilliseconds(transportTime).ToString()}");
						}

						var returningExpo = Helpers.GetFirstReturningExpedition(celestial.Coordinate, userData.fleets);
						if (returningExpo != null) {
							returningExpoTime = (long) (returningExpo.BackIn * 1000) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
							//log(LogType.Debug, LogSender.Brain, $"Next expedition returning by {now.AddMilliseconds(returningExpoTime).ToString()}");
						}

						if ((bool) settings.Brain.AutoMine.Transports.Active) {
							Celestial origin = userData.celestials
									.Unique()
									.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
									.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
									.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
									.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
									.SingleOrDefault() ?? new() { ID = 0 };
							var returningExpoOrigin = Helpers.GetFirstReturningExpedition(origin.Coordinate, userData.fleets);
							if (returningExpoOrigin != null) {
								returningExpoOriginTime = (long) (returningExpoOrigin.BackIn * 1000) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								//log(LogType.Debug, LogSender.Brain, $"Next expedition returning in transport origin celestial by {now.AddMilliseconds(returningExpoOriginTime).ToString()}");
							}

							var incomingOriginFleets = Helpers.GetIncomingFleetsWithResources(origin, userData.fleets);
							if (incomingOriginFleets.Any()) {
								var fleet = incomingOriginFleets.First();
								transportOriginTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
								//log(LogType.Debug, LogSender.Brain, $"Next fleet with resources arriving in transport origin celestial by {DateTime.Now.AddMilliseconds(transportOriginTime).ToString()}");
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
				log(LogType.Error, LogSender.Brain, $"AutoMineCelestial Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
				return interval + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
			}
			if (interval < 0)
				interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
			if (interval == long.MaxValue)
				return interval;
			return interval + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
		}

		private int HandleMinerTransport(Celestial origin, Celestial destination, Resources resources, Buildables buildable = Buildables.Null, Buildings maxBuildings = null, Facilities maxFacilities = null, Facilities maxLunarFacilities = null, AutoMinerSettings autoMinerSettings = null) {
			try {
				if (origin.ID == destination.ID) {
					log(LogType.Warning, LogSender.Brain, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					log(LogType.Warning, LogSender.Brain, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = resources.Difference(destination.Resources);
					Resources resToLeave = new(0, 0, 0);
					if ((long) settings.Brain.AutoMine.Transports.DeutToLeave > 0)
						resToLeave.Deuterium = (long) settings.Brain.AutoMine.Transports.DeutToLeave;

					origin = UpdatePlanet(origin, UpdateTypes.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = UpdatePlanet(origin, UpdateTypes.Ships);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoMine.Transports.CargoType, true, out preferredShip)) {
							log(LogType.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.SmallCargo;
						}

						long idealShips = Helpers.CalcShipNumberForPayload(missingResources, preferredShip, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);
						Ships ships = new();
						Ships tempShips = new();
						tempShips.Add(preferredShip, 1);
						var flightPrediction = Helpers.CalcFleetPrediction(origin.Coordinate, destination.Coordinate, tempShips, Missions.Transport, Speeds.HundredPercent, userData.researches, userData.serverData, userData.userInfo.Class);
						long flightTime = flightPrediction.Time;
						idealShips = Helpers.CalcShipNumberForPayload(missingResources, preferredShip, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);
						var availableShips = origin.Ships.GetAmount(preferredShip);
						if (buildable != Buildables.Null) {
							int level = Helpers.GetNextLevel(destination, buildable);
							long buildTime = Helpers.CalcProductionTime(buildable, level, userData.serverData, destination.Facilities);
							if (maxBuildings != null && maxFacilities != null && maxLunarFacilities != null && autoMinerSettings != null) {
								var tempCelestial = destination;
								while (flightTime * 2 >= buildTime && idealShips <= availableShips) {
									tempCelestial.SetLevel(buildable, level);
									if (buildable != Buildables.SolarSatellite && buildable != Buildables.Crawler && buildable != Buildables.SpaceDock) {
										tempCelestial.Fields.Built += 1;
									}
									var nextBuildable = Buildables.Null;
									if (tempCelestial.Coordinate.Type == Celestials.Planet) {
										tempCelestial.Resources.Energy += Helpers.GetProductionEnergyDelta(buildable, level, userData.researches.EnergyTechnology, 1, userData.userInfo.Class, userData.staff.Engineer, userData.staff.IsFull);
										tempCelestial.ResourcesProduction.Energy.Available += Helpers.GetProductionEnergyDelta(buildable, level, userData.researches.EnergyTechnology, 1, userData.userInfo.Class, userData.staff.Engineer, userData.staff.IsFull);
										tempCelestial.Resources.Energy -= Helpers.GetRequiredEnergyDelta(buildable, level);
										tempCelestial.ResourcesProduction.Energy.Available -= Helpers.GetRequiredEnergyDelta(buildable, level);
										nextBuildable = Helpers.GetNextBuildingToBuild(tempCelestial as Planet, userData.researches, maxBuildings, maxFacilities, userData.userInfo.Class, userData.staff, userData.serverData, autoMinerSettings, 1);
									} else {
										nextBuildable = Helpers.GetNextLunarFacilityToBuild(tempCelestial as Moon, userData.researches, maxLunarFacilities);
									}
									if ( (nextBuildable != Buildables.Null) && (buildable != Buildables.SolarSatellite) ) {
										var nextLevel = Helpers.GetNextLevel(tempCelestial, nextBuildable);
										var newMissingRes = missingResources.Sum(Helpers.CalcPrice(nextBuildable, nextLevel));

										if (origin.Resources.IsEnoughFor(newMissingRes, resToLeave)) {
											var newIdealShips = Helpers.CalcShipNumberForPayload(newMissingRes, preferredShip, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);
											if (newIdealShips <= origin.Ships.GetAmount(preferredShip)) {
												idealShips = newIdealShips;
												missingResources = newMissingRes;
												buildTime += Helpers.CalcProductionTime(nextBuildable, nextLevel, userData.serverData, tempCelestial.Facilities);
												log(LogType.Info, LogSender.Brain, $"Sending resources for {nextBuildable.ToString()} level {nextLevel} too");
												level = nextLevel;
												buildable = nextBuildable;
											}
											else {
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
							idealShips = Helpers.CalcShipNumberForPayload(missingResources, preferredShip, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);
						}

						if (idealShips <= origin.Ships.GetAmount(preferredShip)) {
							ships.Add(preferredShip, idealShips);

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = UpdatePlanet(destination, UpdateTypes.ResourceSettings);
								destination = UpdatePlanet(destination, UpdateTypes.Buildings);
								destination = UpdatePlanet(destination, UpdateTypes.ResourcesProduction);

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
									log(LogType.Info, LogSender.Brain, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
									return SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, userData.userInfo.Class);
								} else {
									log(LogType.Info, LogSender.Brain, "Skipping transport: it is quicker to wait for production.");
									return 0;
								}
							} else {
								log(LogType.Info, LogSender.Brain, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
								return SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, userData.userInfo.Class);
							}
						} else {
							log(LogType.Info, LogSender.Brain, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						log(LogType.Info, LogSender.Brain, $"Skipping transport: not enough resources in origin. Needed: {missingResources.TransportableResources} - Available: {origin.Resources.TransportableResources}");
						return 0;
					}
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"HandleMinerTransport Exception: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}

		private async void AutoBuildCargo(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await xaSem[Feature.Brain].WaitAsync();
				log(LogType.Info, LogSender.Brain, "Running autocargo...");

				if (userData.isSleeping) {
					log(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.Brain.Active && (bool) settings.Brain.AutoCargo.Active) {
					userData.fleets = UpdateFleets();
					List<Celestial> newCelestials = userData.celestials.ToList();
					List<Celestial> celestialsToExclude = Helpers.ParseCelestialsList(settings.Brain.AutoCargo.Exclude, userData.celestials);

					foreach (Celestial celestial in (bool) settings.Brain.AutoCargo.RandomOrder ? userData.celestials.Shuffle().ToList() : userData.celestials) {
						if (celestialsToExclude.Has(celestial)) {
							log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}

						var tempCelestial = UpdatePlanet(celestial, UpdateTypes.Fast);

						userData.fleets = UpdateFleets();
						if ((bool) settings.Brain.AutoCargo.SkipIfIncomingTransport && Helpers.IsThereTransportTowardsCelestial(tempCelestial, userData.fleets)) {
							log(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
							continue;
						}

						tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Productions);
						if (tempCelestial.HasProduction()) {
							log(LogType.Warning, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is already a production ongoing.");
							foreach (Production production in tempCelestial.Productions) {
								Buildables productionType = (Buildables) production.ID;
								log(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are already in production.");
							}
							continue;
						}
						tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Constructions);
						if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
							Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
							log(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");

						}

						tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Ships);
						tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Resources);
						var capacity = Helpers.CalcFleetCapacity(tempCelestial.Ships, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);
						if (tempCelestial.Coordinate.Type == Celestials.Moon && (bool) settings.Brain.AutoCargo.ExcludeMoons) {
							log(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
							continue;
						}
						long neededCargos;
						Buildables preferredCargoShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoCargo.CargoType, true, out preferredCargoShip)) {
							log(LogType.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredCargoShip = Buildables.SmallCargo;
						}
						if (capacity <= tempCelestial.Resources.TotalResources && (bool) settings.Brain.AutoCargo.LimitToCapacity) {
							long difference = tempCelestial.Resources.TotalResources - capacity;
							int oneShipCapacity = Helpers.CalcShipCapacity(preferredCargoShip, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.CargoHyperspaceTechMultiplier);
							neededCargos = (long) Math.Round((float) difference / (float) oneShipCapacity, MidpointRounding.ToPositiveInfinity);
							log(LogType.Info, LogSender.Brain, $"{difference.ToString("N0")} more capacity is needed, {neededCargos} more {preferredCargoShip.ToString()} are needed.");
						} else {
							neededCargos = (long) settings.Brain.AutoCargo.MaxCargosToKeep - tempCelestial.Ships.GetAmount(preferredCargoShip);
						}
						if (neededCargos > 0) {
							if (neededCargos > (long) settings.Brain.AutoCargo.MaxCargosToBuild)
								neededCargos = (long) settings.Brain.AutoCargo.MaxCargosToBuild;

							if (tempCelestial.Ships.GetAmount(preferredCargoShip) + neededCargos > (long) settings.Brain.AutoCargo.MaxCargosToKeep)
								neededCargos = (long) settings.Brain.AutoCargo.MaxCargosToKeep - tempCelestial.Ships.GetAmount(preferredCargoShip);

							var cost = Helpers.CalcPrice(preferredCargoShip, (int) neededCargos);
							if (tempCelestial.Resources.IsEnoughFor(cost))
								log(LogType.Info, LogSender.Brain, $"{tempCelestial.ToString()}: Building {neededCargos}x{preferredCargoShip.ToString()}");
							else {
								var buildableCargos = Helpers.CalcMaxBuildableNumber(preferredCargoShip, tempCelestial.Resources);
								log(LogType.Info, LogSender.Brain, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{preferredCargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
								neededCargos = buildableCargos;
							}

							if (neededCargos > 0) {
								var result = ogamedService.BuildShips(tempCelestial, preferredCargoShip, neededCargos);
								if (result)
									log(LogType.Info, LogSender.Brain, "Production succesfully started.");
								else
									log(LogType.Warning, LogSender.Brain, "Unable to start ship production.");
							}

							tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Productions);
							foreach (Production production in tempCelestial.Productions) {
								Buildables productionType = (Buildables) production.ID;
								log(LogType.Info, LogSender.Brain, $"{tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are in production.");
							}
						} else {
							log(LogType.Info, LogSender.Brain, $"{tempCelestial.ToString()}: No ships will be built.");
						}

						newCelestials.Remove(celestial);
						newCelestials.Add(tempCelestial);
					}
					userData.celestials = newCelestials;
				} else {
					log(LogType.Info, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.Brain, $"Unable to complete autocargo: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogType.Info, LogSender.Brain, $"Stopping feature.");
					} else {
						var time = GetDateTime();
						var interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoCargo.CheckIntervalMin, (int) settings.Brain.AutoCargo.CheckIntervalMax);
						if (interval <= 0)
							interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("CapacityTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.Brain, $"Next capacity check at {newTime.ToString()}");
						CheckCelestials();
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
				log(LogType.Info, LogSender.Brain, "Repatriating resources...");

				if (userData.isSleeping) {
					log(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if (((bool) settings.Brain.Active && (bool) settings.Brain.AutoRepatriate.Active) || (timers.TryGetValue("TelegramCollect", out Timer value))) {
					//log(LogType.Info, LogSender.Telegram, $"Telegram collect initated..");
					if (settings.Brain.AutoRepatriate.Target) {
						userData.fleets = UpdateFleets();
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
						List<Celestial> celestialsToExclude = Helpers.ParseCelestialsList(settings.Brain.AutoRepatriate.Exclude, userData.celestials);

						foreach (Celestial celestial in (bool) settings.Brain.AutoRepatriate.RandomOrder ? userData.celestials.Shuffle().ToList() : userData.celestials.OrderBy(c => Helpers.CalcDistance(c.Coordinate, destinationCoordinate, userData.serverData)).ToList()) {
							if (celestialsToExclude.Has(celestial)) {
								log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
								continue;
							}
							if (celestial.Coordinate.IsSame(destinationCoordinate)) {
								log(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial is the target.");
								continue;
							}

							var tempCelestial = UpdatePlanet(celestial, UpdateTypes.Fast);

							userData.fleets = UpdateFleets();
							if ((bool) settings.Brain.AutoRepatriate.SkipIfIncomingTransport && Helpers.IsThereTransportTowardsCelestial(celestial, userData.fleets) && (!timers.TryGetValue("TelegramCollect", out Timer value2))) {
								log(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
								continue;
							}
							if (celestial.Coordinate.Type == Celestials.Moon && (bool) settings.Brain.AutoRepatriate.ExcludeMoons) {
								log(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
								continue;
							}

							tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Resources);
							tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Ships);

							Buildables preferredShip = Buildables.SmallCargo;
							if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
								log(LogType.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
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
								log(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: resources under set limit");
								continue;
							}

							long idealShips = Helpers.CalcShipNumberForPayload(payload, preferredShip, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier);

							Ships ships = new();
							if (tempCelestial.Ships.GetAmount(preferredShip) != 0) {
								if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
									ships.Add(preferredShip, idealShips);
								} else {
									ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
								}
								payload = Helpers.CalcMaxTransportableResources(ships, payload, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo);

								if (payload.TotalResources > 0) {
									var fleetId = SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
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
								log(LogType.Warning, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there are no {preferredShip.ToString()}");
							}

							newCelestials.Remove(celestial);
							newCelestials.Add(tempCelestial);
						}
						userData.celestials = newCelestials;
						//send notif only if sent via telegram
						if (timers.TryGetValue("TelegramCollect", out Timer value1)) {
							if ((TotalMet > 0) || (TotalCri > 0) || (TotalDeut > 0)) {
								SendTelegramMessage($"Resources sent!:\n{TotalMet} Metal\n{TotalCri} Crystal\n{TotalDeut} Deuterium");
							} else {
								SendTelegramMessage("No resources sent");
							}
						}
					} else {
						log(LogType.Warning, LogSender.Brain, "Skipping autorepatriate: unable to parse custom destination");
					}
				} else {
					log(LogType.Info, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				log(LogType.Warning, LogSender.Brain, $"Unable to complete repatriate: {e.Message}");
				log(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!userData.isSleeping) {
					if (timers.TryGetValue("TelegramCollect", out Timer val)) {
						val.Dispose();
						timers.Remove("TelegramCollect");
					} else {
						if (stop) {
							log(LogType.Info, LogSender.Brain, $"Stopping feature.");
						} else if (delay) {
							log(LogType.Info, LogSender.Brain, $"Delaying...");
							userData.fleets = UpdateFleets();
							var time = GetDateTime();
							long interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
							log(LogType.Info, LogSender.Brain, $"Next repatriate check at {newTime.ToString()}");
						} else {
							var time = GetDateTime();
							var interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoRepatriate.CheckIntervalMin, (int) settings.Brain.AutoRepatriate.CheckIntervalMax);
							if (interval <= 0)
								interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							var newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
							log(LogType.Info, LogSender.Brain, $"Next repatriate check at {newTime.ToString()}");
						}
					}
					CheckCelestials();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private int SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Model.Resources payload = null, CharacterClass playerClass = CharacterClass.NoClass, bool force = false) {
			log(LogType.Info, LogSender.FleetScheduler, $"Sending fleet from {origin.Coordinate.ToString()} to {destination.ToString()}. Mission: {mission.ToString()}. Speed: {(speed * 10).ToString()}% Ships: {ships.ToString()}");

			if (playerClass == CharacterClass.NoClass)
				playerClass = userData.userInfo.Class;

			if (!ships.HasMovableFleet()) {
				log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: there are no ships to send");
				return (int) SendFleetCode.GenericError;
			}
			if (mission == Missions.Expedition && ships.IsOnlyProbes()) {
				log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: cannot send an expedition with no ships");
				return (int) SendFleetCode.GenericError;
			}
			if (origin.Coordinate.IsSame(destination)) {
				log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: origin and destination are the same");
				return (int) SendFleetCode.GenericError;
			}
			if (destination.Galaxy <= 0 || destination.Galaxy > userData.serverData.Galaxies || destination.Position <= 0 || destination.Position > 17) {
				log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
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
					log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
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

			if (!Helpers.GetValidSpeedsForClass(playerClass).Any(s => s == speed)) {
				log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: speed not available for your class");
				return (int) SendFleetCode.GenericError;
			}
			FleetPrediction fleetPrediction = Helpers.CalcFleetPrediction(origin.Coordinate, destination, ships, mission, speed, userData.researches, userData.serverData, userData.userInfo.Class);
			log(LogType.Debug, LogSender.FleetScheduler, $"Calculated flight time (one-way): {TimeSpan.FromSeconds(fleetPrediction.Time).ToString()}");

			var flightTime = mission switch {
				Missions.Deploy => fleetPrediction.Time,
				Missions.Expedition => (long) Math.Round((double) (2 * fleetPrediction.Time) + 3600, 0, MidpointRounding.ToPositiveInfinity),
				_ => (long) Math.Round((double) (2 * fleetPrediction.Time), 0, MidpointRounding.ToPositiveInfinity),
			};
			log(LogType.Debug, LogSender.FleetScheduler, $"Calculated flight time (full trip): {TimeSpan.FromSeconds(flightTime).ToString()}");
			log(LogType.Debug, LogSender.FleetScheduler, $"Calculated flight fuel: {fleetPrediction.Fuel.ToString()}");

			origin = UpdatePlanet(origin, UpdateTypes.Resources);
			if (origin.Resources.Deuterium < fleetPrediction.Fuel) {
				log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: not enough deuterium!");
				return (int) SendFleetCode.GenericError;
			}

			if (Helpers.CalcFleetFuelCapacity(ships, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier) != 0 && Helpers.CalcFleetFuelCapacity(ships, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier) < fleetPrediction.Fuel) {
				log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: ships don't have enough fuel capacity!");
				return (int) SendFleetCode.GenericError;
			}

			if (
				(bool) settings.SleepMode.Active &&
				DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep) &&
				DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp) &&
				!force
			) {
				DateTime time = GetDateTime();

				if (Helpers.ShouldSleep(time, goToSleep, wakeUp)) {
					log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: bed time has passed");
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
				log(LogType.Debug, LogSender.FleetScheduler, $"goToSleep : {goToSleep.ToString()}");
				log(LogType.Debug, LogSender.FleetScheduler, $"wakeUp : {wakeUp.ToString()}");

				DateTime returnTime = time.AddSeconds(flightTime);
				log(LogType.Debug, LogSender.FleetScheduler, $"returnTime : {returnTime.ToString()}");

				if (returnTime >= goToSleep && returnTime <= wakeUp) {
					log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: it would come back during sleep time");
					return (int) SendFleetCode.AfterSleepTime;
				}
			}
			userData.slots = UpdateSlots();
			int slotsToLeaveFree = (int) settings.General.SlotsToLeaveFree;
			if (userData.slots.Free == 0) {
				LoggerService.Logger.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet, no slots available");
				return (int) SendFleetCode.NotEnoughSlots;
			}
			else if (userData.slots.Free > slotsToLeaveFree || force) {
				if (payload == null)
					payload = new();
				try {
					Fleet fleet = ogamedService.SendFleet(origin, ships, destination, mission, speed, payload);
					log(LogType.Info, LogSender.FleetScheduler, "Fleet succesfully sent");
					userData.fleets = ogamedService.GetFleets();
					userData.slots = UpdateSlots();
					return fleet.ID;
				} catch (Exception e) {
					log(LogType.Error, LogSender.FleetScheduler, $"Unable to send fleet: an exception has occurred: {e.Message}");
					log(LogType.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
					return (int) SendFleetCode.GenericError;
				}
			} else {
				log(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet, no slots available");
				return (int) SendFleetCode.NotEnoughSlots;
			}
		}

		private async void CancelFleet(Fleet fleet) {
			//log(LogType.Info, LogSender.FleetScheduler, $"Recalling fleet id {fleet.ID} originally from {fleet.Origin.ToString()} to {fleet.Destination.ToString()} with mission: {fleet.Mission.ToString()}. Start time: {fleet.StartTime.ToString()} - Arrival time: {fleet.ArrivalTime.ToString()} - Ships: {fleet.Ships.ToString()}");
			userData.slots = UpdateSlots();
			try {
				ogamedService.CancelFleet(fleet);
				await Task.Delay((int) IntervalType.AFewSeconds);
				userData.fleets = UpdateFleets();
				Fleet recalledFleet = userData.fleets.SingleOrDefault(f => f.ID == fleet.ID) ?? new() { ID = (int) SendFleetCode.GenericError };
				if (recalledFleet.ID == (int) SendFleetCode.GenericError) {
					log(LogType.Error, LogSender.FleetScheduler, "Unable to recall fleet: an unknon error has occurred, already recalled ?.");
				} else {
					log(LogType.Info, LogSender.FleetScheduler, $"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
					if ((bool) settings.Defender.TelegramMessenger.Active) {
						SendTelegramMessage($"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
					}
					return;
				}
			} catch (Exception e) {
				log(LogType.Error, LogSender.FleetScheduler, $"Unable to recall fleet: an exception has occurred: {e.Message}");
				log(LogType.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				return;
			} finally {
				if (timers.TryGetValue($"RecallTimer-{fleet.ID.ToString()}", out Timer value)) {
					value.Dispose();
					timers.Remove($"RecallTimer-{fleet.ID.ToString()}");
				}

			}
		}

		public void TelegramRetireFleet(int fleetId) {
			userData.fleets = UpdateFleets();
			Fleet ToRecallFleet = userData.fleets.SingleOrDefault(f => f.ID == fleetId) ?? new() { ID = (int) SendFleetCode.GenericError };
			if (ToRecallFleet.ID == (int) SendFleetCode.GenericError) {
				SendTelegramMessage($"Unable to recall fleet! Already recalled?");
				return;
			}
			RetireFleet(ToRecallFleet);
		}

		private void RetireFleet(object fleet) {
			CancelFleet((Fleet) fleet);
		}


		public void TelegramMesgAttacker(string message) {
			userData.attacks = ogamedService.GetAttacks();
			List<int> playerid = new List<int>();

			foreach (AttackerFleet attack in userData.attacks) {
				if (attack.AttackerID != 0 && !playerid.Any(s => s == attack.AttackerID)) {
					var result = ogamedService.SendMessage(attack.AttackerID, message);
					playerid.Add(attack.AttackerID);

					if (result)
						SendTelegramMessage($"Message succesfully sent to {attack.AttackerName}.");
					else
						SendTelegramMessage($"Unable to send message.");
				} else {
					SendTelegramMessage($"Unable send message, AttackerID error.");
				}
			}
		}

		private async void HandleAttack(AttackerFleet attack) {
			if (userData.celestials.Count() == 0) {
				DateTime time = GetDateTime();
				long interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				log(LogType.Warning, LogSender.Defender, "Unable to handle attack at the moment: bot is still getting account info.");
				log(LogType.Info, LogSender.Defender, $"Next check at {newTime.ToString()}");
				return;
			}

			Celestial attackedCelestial = userData.celestials.Unique().SingleOrDefault(planet => planet.HasCoords(attack.Destination));
			attackedCelestial = UpdatePlanet(attackedCelestial, UpdateTypes.Ships);

			try {
				if ((settings.Defender.WhiteList as long[]).Any()) {
					foreach (int playerID in (long[]) settings.Defender.WhiteList) {
						if (attack.AttackerID == playerID) {
							log(LogType.Info, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: attacker {attack.AttackerName} whitelisted.");
							return;
						}
					}
				}
			} catch {
				log(LogType.Warning, LogSender.Defender, "An error has occurred while checking Defender WhiteList");
			}

			try {
				if (attack.MissionType == Missions.MissileAttack) {
					if (
						!SettingsService.IsSettingSet(settings.Defender, "IgnoreMissiles") ||
						(SettingsService.IsSettingSet(settings.Defender, "IgnoreMissiles") && (bool) settings.Defender.IgnoreMissiles)
					) {
						log(LogType.Info, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: missiles attack.");
						return;
					}
				}
				if (attack.Ships != null && userData.researches.EspionageTechnology >= 8) {
					if (SettingsService.IsSettingSet(settings.Defender, "IgnoreProbes") && (bool) settings.Defender.IgnoreProbes && attack.IsOnlyProbes()) {
						if (attack.MissionType == Missions.Spy)
							log(LogType.Info, LogSender.Defender, "Attacker sent only Probes! Espionage action skipped.");
						else
							log(LogType.Info, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: only Espionage Probes.");

						return;
					}
					if (
						(bool) settings.Defender.IgnoreWeakAttack &&
						attack.Ships.GetFleetPoints() < (attackedCelestial.Ships.GetFleetPoints() / (int) settings.Defender.WeakAttackRatio)
					) {
						log(LogType.Info, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: weak attack.");
						return;
					}
				} else {
					log(LogType.Info, LogSender.Defender, "Unable to detect fleet composition.");
				}
			} catch {
				log(LogType.Warning, LogSender.Defender, "An error has occurred while checking attacker fleet composition");
			}

			if ((bool) settings.Defender.TelegramMessenger.Active) {
				SendTelegramMessage($"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} arriving at {attack.ArrivalTime.ToString()}");
				if (attack.Ships != null)
					await Task.Delay(1000);
				SendTelegramMessage($"The attack is composed by: {attack.Ships.ToString()}");
			}
			log(LogType.Warning, LogSender.Defender, $"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attackedCelestial.ToString()} arriving at {attack.ArrivalTime.ToString()}");
			if (attack.Ships != null)
				await Task.Delay(1000);
			log(LogType.Warning, LogSender.Defender, $"The attack is composed by: {attack.Ships.ToString()}");

			if ((bool) settings.Defender.SpyAttacker.Active) {
				userData.slots = UpdateSlots();
				if (attackedCelestial.Ships.EspionageProbe == 0) {
					log(LogType.Warning, LogSender.Defender, "Could not spy attacker: no probes available.");
				} else {
					try {
						Coordinate destination = attack.Origin;
						Ships ships = new() { EspionageProbe = (int) settings.Defender.SpyAttacker.Probes };
						int fleetId = SendFleet(attackedCelestial, ships, destination, Missions.Spy, Speeds.HundredPercent, new Resources(), userData.userInfo.Class);
						Fleet fleet = userData.fleets.Single(fleet => fleet.ID == fleetId);
						log(LogType.Info, LogSender.Defender, $"Spying attacker from {attackedCelestial.ToString()} to {destination.ToString()} with {settings.Defender.SpyAttacker.Probes} probes. Arrival at {fleet.ArrivalTime.ToString()}");
					} catch (Exception e) {
						log(LogType.Error, LogSender.Defender, $"Could not spy attacker: an exception has occurred: {e.Message}");
						log(LogType.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
					}
				}
			}

			if ((bool) settings.Defender.MessageAttacker.Active) {
				try {
					if (attack.AttackerID != 0) {
						Random random = new();
						string[] messages = settings.Defender.MessageAttacker.Messages;
						string message = messages.ToList().Shuffle().First();
						log(LogType.Info, LogSender.Defender, $"Sending message \"{message}\" to attacker {attack.AttackerName}");
						var result = ogamedService.SendMessage(attack.AttackerID, message);
						if (result)
							log(LogType.Info, LogSender.Defender, "Message succesfully sent.");
						else
							log(LogType.Warning, LogSender.Defender, "Unable send message.");
					} else {
						log(LogType.Warning, LogSender.Defender, "Unable send message.");
					}

				} catch (Exception e) {
					log(LogType.Error, LogSender.Defender, $"Could not message attacker: an exception has occurred: {e.Message}");
					log(LogType.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
				}
			}

			if ((bool) settings.Defender.Autofleet.Active) {
				var minFlightTime = attack.ArriveIn + (attack.ArriveIn / 100 * 30) + (Helpers.CalcRandomInterval(IntervalType.SomeSeconds) / 1000);
				AutoFleetSave(attackedCelestial, false, minFlightTime, true);
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
					log(LogType.Info, LogSender.Expeditions, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Expeditions].Release();
					return;
				}

				if ((bool) settings.Expeditions.Active && timers.TryGetValue("ExpeditionsTimer", out Timer value)) {
					userData.researches = UpdateResearches();
					if (userData.researches.Astrophysics == 0) {
						log(LogType.Info, LogSender.Expeditions, "Skipping: Astrophysics not yet researched!");
						time = GetDateTime();
						interval = Helpers.CalcRandomInterval(IntervalType.AboutHalfAnHour);
						newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
						return;
					}

					userData.slots = UpdateSlots();
					userData.fleets = UpdateFleets();
					userData.serverData = ogamedService.GetServerData();
					int expsToSend;
					if (SettingsService.IsSettingSet(settings.Expeditions, "WaitForAllExpeditions") && (bool) settings.Expeditions.WaitForAllExpeditions) {
						if (userData.slots.ExpInUse == 0)
							expsToSend = userData.slots.ExpTotal;
						else
							expsToSend = 0;
					} else {
						expsToSend = Math.Min(userData.slots.ExpFree, userData.slots.Free);
					}
					log(LogType.Debug, LogSender.Expeditions, $"Expedition slot free: {expsToSend}");
					if (SettingsService.IsSettingSet(settings.Expeditions, "WaitForMajorityOfExpeditions") && (bool) settings.Expeditions.WaitForMajorityOfExpeditions) {
						if ((double) expsToSend < Math.Round((double) userData.slots.ExpTotal / 2D, 0, MidpointRounding.ToZero) + 1D) {
							log(LogType.Debug, LogSender.Expeditions, $"Majority of expedition already in flight, Skipping...");
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
											customOrigin = UpdatePlanet(customOrigin, UpdateTypes.Ships);
											origins.Add(customOrigin);
										}
									} catch (Exception e) {
										log(LogType.Debug, LogSender.Expeditions, $"Exception: {e.Message}");
										log(LogType.Warning, LogSender.Expeditions, $"Stacktrace: {e.StackTrace}");
										log(LogType.Warning, LogSender.Expeditions, "Unable to parse custom origin");

										userData.celestials = UpdatePlanets(UpdateTypes.Ships);
										origins.Add(userData.celestials
											.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
											.OrderByDescending(planet => Helpers.CalcFleetCapacity(planet.Ships, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier))
											.First()
										);
									}
								} else {
									userData.celestials = UpdatePlanets(UpdateTypes.Ships);
									origins.Add(userData.celestials
										.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
										.OrderByDescending(planet => Helpers.CalcFleetCapacity(planet.Ships, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.ProbeCargo, userData.serverData.CargoHyperspaceTechMultiplier))
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
										log(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no ships available");
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
												log(LogType.Warning, LogSender.Expeditions, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
												continue;
											}
										} else {
											Buildables primaryShip = Buildables.LargeCargo;
											if (!Enum.TryParse<Buildables>(settings.Expeditions.PrimaryShip.ToString(), true, out primaryShip)) {
												log(LogType.Warning, LogSender.Expeditions, "Unable to parse PrimaryShip. Falling back to default LargeCargo");
												primaryShip = Buildables.LargeCargo;
											}
											if (primaryShip == Buildables.Null) {
												log(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: primary ship is Null");
												continue;
											}

											var availableShips = origin.Ships.GetMovableShips();
											if (SettingsService.IsSettingSet(settings.Expeditions, "PrimaryToKeep") && (int) settings.Expeditions.PrimaryToKeep > 0) {
												availableShips.SetAmount(primaryShip, availableShips.GetAmount(primaryShip) - (long) settings.Expeditions.PrimaryToKeep);
											}
											log(LogType.Warning, LogSender.Expeditions, $"Available {primaryShip.ToString()} in origin {origin.ToString()}: {availableShips.GetAmount(primaryShip)}");
											fleet = Helpers.CalcFullExpeditionShips(availableShips, primaryShip, expsToSendFromThisOrigin, userData.serverData, userData.researches, userData.userInfo.Class, userData.serverData.ProbeCargo);
											if (fleet.GetAmount(primaryShip) < (long) settings.Expeditions.MinPrimaryToSend) {
												fleet.SetAmount(primaryShip, (long) settings.Expeditions.MinPrimaryToSend);
												if (!availableShips.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
													log(LogType.Warning, LogSender.Expeditions, $"Unable to send expeditions: available {primaryShip.ToString()} in origin {origin.ToString()} under set min number of {(long) settings.Expeditions.MinPrimaryToSend}");
													continue;
												}
											}
											Buildables secondaryShip = Buildables.Null;
											if (!Enum.TryParse<Buildables>(settings.Expeditions.SecondaryShip, true, out secondaryShip)) {
												log(LogType.Warning, LogSender.Expeditions, "Unable to parse SecondaryShip. Falling back to default Null");
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
													log(LogType.Warning, LogSender.Expeditions, $"Unable to send expeditions: available {secondaryShip.ToString()} in origin {origin.ToString()} under set number of {(long) settings.Expeditions.MinSecondaryToSend}");
													continue;
												} else {
													fleet.Add(secondaryShip, secondaryToSend);
													if (!availableShips.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
														log(LogType.Warning, LogSender.Expeditions, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
														continue;
													}
												}
											}
										}

										log(LogType.Info, LogSender.Expeditions, $"{expsToSendFromThisOrigin.ToString()} expeditions with {fleet.ToString()} will be sent from {origin.ToString()}");
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
												destination.System = Helpers.WrapSystem(destination.System);
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
											userData.slots = UpdateSlots();
											Resources payload = new();
											if ((long) settings.Expeditions.FuelToCarry > 0) {
												payload.Deuterium = (long) settings.Expeditions.FuelToCarry;
											}
											if (userData.slots.ExpFree > 0) {
												var fleetId = SendFleet(origin, fleet, destination, Missions.Expedition, Speeds.HundredPercent, payload);

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
												log(LogType.Info, LogSender.Expeditions, "Unable to send expeditions: no expedition slots available.");
												break;
											}
										}
									}
								}
							} else {
								log(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no fleet slots available");
							}
						} else {
							log(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no expeditions slots available");
						}
					}

					userData.fleets = UpdateFleets();
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

					userData.slots = UpdateSlots();
					if ( (orderedFleets.Count() == 0) || (userData.slots.ExpFree > 0)) {
						interval = Helpers.CalcRandomInterval(IntervalType.AboutFiveMinutes);
					} else {
						interval = (int) ((1000 * orderedFleets.First().BackIn) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo));
					}
					time = GetDateTime();
					if (interval <= 0)
						interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
					log(LogType.Info, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
					CheckCelestials();
				}
			} catch (Exception e) {
				log(LogType.Warning, LogSender.Expeditions, $"HandleExpeditions exception: {e.Message}");
				log(LogType.Warning, LogSender.Expeditions, $"Stacktrace: {e.StackTrace}");
				long interval = (long) (Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo));
				var time = GetDateTime();
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
				log(LogType.Info, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogType.Info, LogSender.Expeditions, $"Stopping feature.");
					}
					if (delay) {
						log(LogType.Info, LogSender.Expeditions, $"Delaying...");
						var time = GetDateTime();
						userData.fleets = UpdateFleets();
						long interval;
						try {
							interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = Helpers.CalcRandomInterval((int) settings.Expeditions.CheckIntervalMin, (int) settings.Expeditions.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
					}
					CheckCelestials();
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
					log(LogType.Info, LogSender.Harvest, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Harvest].Release();
					return;
				}

				if ((bool) settings.AutoHarvest.Active) {
					log(LogType.Info, LogSender.Harvest, "Detecting harvest targets");

					List<Celestial> newCelestials = userData.celestials.ToList();
					var dic = new Dictionary<Coordinate, Celestial>();

					userData.fleets = UpdateFleets();

					foreach (Planet planet in userData.celestials.Where(c => c is Planet)) {
						Planet tempCelestial = UpdatePlanet(planet, UpdateTypes.Fast) as Planet;
						tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Ships) as Planet;
						Moon moon = new() {
							Ships = new()
						};

						bool hasMoon = userData.celestials.Count(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) == 1;
						if (hasMoon) {
							moon = userData.celestials.Unique().Single(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) as Moon;
							moon = UpdatePlanet(moon, UpdateTypes.Ships) as Moon;
						}

						if ((bool) settings.AutoHarvest.HarvestOwnDF) {
							Coordinate dest = new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris);
							if (dic.Keys.Any(d => d.IsSame(dest)))
								continue;
							if (userData.fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
								continue;
							tempCelestial = UpdatePlanet(tempCelestial, UpdateTypes.Debris) as Planet;
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
									log(LogType.Info, LogSender.Harvest, $"Skipping harvest in {dest.ToString()}: not enough recyclers.");
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
									destination.System = Helpers.WrapSystem(destination.System);

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
								ExpeditionDebris expoDebris = ogamedService.GetGalaxyInfo(dest).ExpeditionDebris;
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
										log(LogType.Info, LogSender.Harvest, $"Skipping harvest in {dest.ToString()}: not enough pathfinders.");
								}
							}
						}

						newCelestials.Remove(planet);
						newCelestials.Add(tempCelestial);
					}
					userData.celestials = newCelestials;

					if (dic.Count() == 0)
						log(LogType.Info, LogSender.Harvest, "Skipping harvest: there are no fields to harvest.");

					foreach (Coordinate destination in dic.Keys) {
						var fleetId = (int) SendFleetCode.GenericError;
						Celestial origin = dic[destination];
						if (destination.Position == 16) {
							ExpeditionDebris debris = ogamedService.GetGalaxyInfo(destination).ExpeditionDebris;
							long pathfindersToSend = Math.Min(Helpers.CalcShipNumberForPayload(debris.Resources, Buildables.Pathfinder, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.CargoHyperspaceTechMultiplier), origin.Ships.Pathfinder);
							log(LogType.Info, LogSender.Harvest, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {pathfindersToSend.ToString()} {Buildables.Pathfinder.ToString()}");
							fleetId = SendFleet(origin, new Ships { Pathfinder = pathfindersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
						} else {
							if (userData.celestials.Any(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet)))) {
								Debris debris = (userData.celestials.Where(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet))).First() as Planet).Debris;
								long recyclersToSend = Math.Min(Helpers.CalcShipNumberForPayload(debris.Resources, Buildables.Recycler, userData.researches.HyperspaceTechnology, userData.userInfo.Class, userData.serverData.CargoHyperspaceTechMultiplier), origin.Ships.Recycler);
								log(LogType.Info, LogSender.Harvest, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {recyclersToSend.ToString()} {Buildables.Recycler.ToString()}");
								fleetId = SendFleet(origin, new Ships { Recycler = recyclersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
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

					userData.fleets = UpdateFleets();
					long interval;
					if (userData.fleets.Any(f => f.Mission == Missions.Harvest)) {
						interval = (userData.fleets
							.Where(f => f.Mission == Missions.Harvest)
							.OrderBy(f => f.BackIn)
							.First()
							.BackIn ?? 0) * 1000;
					} else {
						interval = (int) Helpers.CalcRandomInterval((int) settings.AutoHarvest.CheckIntervalMin, (int) settings.AutoHarvest.CheckIntervalMax);
					}
					var time = GetDateTime();
					if (interval <= 0)
						interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
					log(LogType.Info, LogSender.Harvest, $"Next check at {newTime.ToString()}");
				}
			} catch (Exception e) {
				log(LogType.Warning, LogSender.Harvest, $"HandleHarvest exception: {e.Message}");
				log(LogType.Warning, LogSender.Harvest, $"Stacktrace: {e.StackTrace}");
				long interval = (int) Helpers.CalcRandomInterval((int) settings.AutoHarvest.CheckIntervalMin, (int) settings.AutoHarvest.CheckIntervalMax);
				var time = GetDateTime();
				if (interval <= 0)
					interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
				log(LogType.Info, LogSender.Harvest, $"Next check at {newTime.ToString()}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogType.Info, LogSender.Harvest, $"Stopping feature.");
					}
					if (delay) {
						log(LogType.Info, LogSender.Harvest, $"Delaying...");
						var time = GetDateTime();
						userData.fleets = UpdateFleets();
						long interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.Harvest, $"Next check at {newTime.ToString()}");
					}
					CheckCelestials();
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
					log(LogType.Info, LogSender.Colonize, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Colonize].Release();
					return;
				}

				if ((bool) settings.AutoColonize.Active) {
					long interval = Helpers.CalcRandomInterval((int) settings.AutoColonize.CheckIntervalMin, (int) settings.AutoColonize.CheckIntervalMax);
					log(LogType.Info, LogSender.Colonize, "Checking if a new planet is needed...");

					userData.researches = UpdateResearches();
					var maxPlanets = Helpers.CalcMaxPlanets(userData.researches);
					var currentPlanets = userData.celestials.Where(c => c.Coordinate.Type == Celestials.Planet).Count();
					var slotsToLeaveFree = (int) (settings.AutoColonize.SlotsToLeaveFree ?? 0);
					if (currentPlanets + slotsToLeaveFree < maxPlanets) {
						log(LogType.Info, LogSender.Colonize, "A new planet is needed.");

						userData.fleets = UpdateFleets();
						if (userData.fleets.Any(f => f.Mission == Missions.Colonize && !f.ReturnFlight)) {
							log(LogType.Info, LogSender.Colonize, "Colony Ship(s) already in flight.");
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
							UpdatePlanet(origin, UpdateTypes.Ships);

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
									GalaxyInfo galaxy = ogamedService.GetGalaxyInfo(t);
									if (galaxy.Planets.Any(p => p != null && p.HasCoords(t))) {
										continue;
									}
									filteredTargets.Add(t);
								}
								if (filteredTargets.Count() > 0) {
									filteredTargets = filteredTargets
										.OrderBy(t => Helpers.CalcDistance(origin.Coordinate, t, userData.serverData))
										.Take(maxPlanets - currentPlanets)
										.ToList();
									foreach (var target in filteredTargets) {
										Ships ships = new() { ColonyShip = 1 };
										var fleetId = SendFleet(origin, ships, target, Missions.Colonize, Speeds.HundredPercent);

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
									log(LogType.Info, LogSender.Colonize, "No valid coordinate in target list.");
								}
							} else {
								UpdatePlanet(origin, UpdateTypes.Productions);
								UpdatePlanet(origin, UpdateTypes.Facilities);
								if (origin.Productions.Any()) {
									log(LogType.Info, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed. {origin.Productions.Where(p => p.ID == (int) Buildables.ColonyShip).Sum(p => p.Nbr)} colony ship(s) already in production.");
									foreach (var prod in origin.Productions) {
										if (prod == origin.Productions.First()) {
											interval += (int) Helpers.CalcProductionTime((Buildables) prod.ID, prod.Nbr - 1, userData.serverData, origin.Facilities) * 1000;
										} else {
											interval += (int) Helpers.CalcProductionTime((Buildables) prod.ID, prod.Nbr, userData.serverData, origin.Facilities) * 1000;
										}
										if (prod.ID == (int) Buildables.ColonyShip) {
											break;
										}
									}
								} else {
									log(LogType.Info, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed.");
									UpdatePlanet(origin, UpdateTypes.Resources);
									var cost = Helpers.CalcPrice(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip);
									if (origin.Resources.IsEnoughFor(cost)) {
										UpdatePlanet(origin, UpdateTypes.Constructions);
										if (origin.HasConstruction() && (origin.Constructions.BuildingID == (int) Buildables.Shipyard || origin.Constructions.BuildingID == (int) Buildables.NaniteFactory)) {
											log(LogType.Info, LogSender.Colonize, $"Unable to build colony ship: {((Buildables) origin.Constructions.BuildingID).ToString()} is in construction");
											interval = (long) origin.Constructions.BuildingCountdown * (long) 1000;
										} else if (origin.HasProduction()) {
											log(LogType.Info, LogSender.Colonize, $"Unable to build colony ship: there is already something in production");
											interval = (long) Helpers.CalcProductionTime((Buildables) origin.Productions.First().ID, origin.Productions.First().Nbr - 1, userData.serverData, origin.Facilities) * 1000;
										} else if (origin.Facilities.Shipyard >= 4 && userData.researches.ImpulseDrive >= 3) {
											log(LogType.Info, LogSender.Colonize, $"Building {neededColonizers - origin.Ships.ColonyShip}....");
											ogamedService.BuildShips(origin, Buildables.ColonyShip, neededColonizers - origin.Ships.ColonyShip);
											interval = (int) Helpers.CalcProductionTime(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip, userData.serverData, origin.Facilities) * 1000;
										} else {
											log(LogType.Info, LogSender.Colonize, $"Requirements to build colony ship not met");
										}
									} else {
										log(LogType.Info, LogSender.Colonize, $"Not enough resources to build {neededColonizers} colony ship(s). Needed: {cost.TransportableResources} - Available: {origin.Resources.TransportableResources}");
									}
								}
							}
						}
					} else {
						log(LogType.Info, LogSender.Colonize, "No new planet is needed.");
					}

					DateTime time = GetDateTime();
					if (interval <= 0) {
						interval = Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					}

					DateTime newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("ColonizeTimer").Change(interval + Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite);
					log(LogType.Info, LogSender.Colonize, $"Next check at {newTime}");
				}
			} catch (Exception e) {
				log(LogType.Warning, LogSender.Colonize, $"HandleColonize exception: {e.Message}");
				log(LogType.Warning, LogSender.Colonize, $"Stacktrace: {e.StackTrace}");
				long interval = Helpers.CalcRandomInterval((int) settings.AutoColonize.CheckIntervalMin, (int) settings.AutoColonize.CheckIntervalMax);
				DateTime time = GetDateTime();
				if (interval <= 0)
					interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("ColonizeTimer").Change(interval, Timeout.Infinite);
				log(LogType.Info, LogSender.Colonize, $"Next check at {newTime}");
			} finally {
				if (!userData.isSleeping) {
					if (stop) {
						log(LogType.Info, LogSender.Colonize, $"Stopping feature.");
					}
					if (delay) {
						log(LogType.Info, LogSender.Colonize, $"Delaying...");
						var time = GetDateTime();
						userData.fleets = UpdateFleets();
						long interval;
						try {
							interval = (userData.fleets.OrderBy(f => f.BackIn).First().BackIn ?? 0) * 1000 + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						} catch {
							interval = Helpers.CalcRandomInterval((int) settings.AutoColonize.CheckIntervalMin, (int) settings.AutoColonize.CheckIntervalMax);
						}
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("ColonizeTimer").Change(interval, Timeout.Infinite);
						log(LogType.Info, LogSender.Colonize, $"Next check at {newTime}");
					}
					CheckCelestials();
					xaSem[Feature.Colonize].Release();
				}
			}
		}

		private async void ScheduleFleet(object scheduledFleet) {
			FleetSchedule _scheduledFleet = scheduledFleet as FleetSchedule;
			try {
				await xaSem[Feature.FleetScheduler].WaitAsync();
				userData.scheduledFleets.Add(_scheduledFleet);
				SendFleet(_scheduledFleet.Origin, _scheduledFleet.Ships, _scheduledFleet.Destination, _scheduledFleet.Mission, _scheduledFleet.Speed, _scheduledFleet.Payload, userData.userInfo.Class);
			} catch (Exception e) {
				log(LogType.Warning, LogSender.FleetScheduler, $"ScheduleFleet exception: {e.Message}");
				log(LogType.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
			} finally {
				userData.scheduledFleets = userData.scheduledFleets.OrderBy(f => f.Departure).ToList();
				if (userData.scheduledFleets.Count() > 0) {
					long nextTime = (long) userData.scheduledFleets.FirstOrDefault().Departure.Subtract(GetDateTime()).TotalMilliseconds;
					timers.GetValueOrDefault("FleetSchedulerTimer").Change(nextTime, Timeout.Infinite);
					log(LogType.Info, LogSender.FleetScheduler, $"Next scheduled fleet at {userData.scheduledFleets.First().ToString()}");
				}
				xaSem[Feature.FleetScheduler].Release();
			}
		}

		private async void HandleScheduledFleet(object scheduledFleet) {
			FleetSchedule _scheduledFleet = scheduledFleet as FleetSchedule;
			try {
				await xaSem[Feature.FleetScheduler].WaitAsync();
				SendFleet(_scheduledFleet.Origin, _scheduledFleet.Ships, _scheduledFleet.Destination, _scheduledFleet.Mission, _scheduledFleet.Speed, _scheduledFleet.Payload, userData.userInfo.Class);
			} catch (Exception e) {
				log(LogType.Warning, LogSender.FleetScheduler, $"HandleScheduledFleet exception: {e.Message}");
				log(LogType.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
			} finally {
				userData.scheduledFleets.Remove(_scheduledFleet);
				userData.scheduledFleets = userData.scheduledFleets.OrderBy(f => f.Departure).ToList();
				if (userData.scheduledFleets.Count() > 0) {
					long nextTime = (long) userData.scheduledFleets.FirstOrDefault().Departure.Subtract(GetDateTime()).TotalMilliseconds;
					timers.GetValueOrDefault("FleetSchedulerTimer").Change(nextTime, Timeout.Infinite);
					log(LogType.Info, LogSender.FleetScheduler, $"Next scheduled fleet at {userData.scheduledFleets.First().ToString()}");
				}
				xaSem[Feature.FleetScheduler].Release();
			}
		}
	}
}
