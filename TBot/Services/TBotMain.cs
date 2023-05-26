using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Newtonsoft.Json.Linq;
using Tbot.Common.Extensions;
using Tbot.Common.Settings;
using Tbot.Exceptions;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Workers;
using Tbot.Workers.Brain;
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
	public class TBotMain : IEquatable<TBotMain>, IAsyncDisposable, ITBotMain {
		private readonly IOgameService _ogameService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly ILoggerService<TBotMain> _logger;
		private readonly ICalculationService _helpersService;
		private readonly IWorkerFactory _workerFactory;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		private readonly SemaphoreSlim _settingsReloadSemaphore = new SemaphoreSlim(1, 1);

		private ITelegramMessenger telegramMessenger;

		private bool loggedIn = false;
		private Dictionary<string, Timer> timers = new();
		private ConcurrentDictionary<Feature, ITBotWorker> workers = new();
		private CancellationTokenSource cts = new();


		public UserData userData = new();
		public TelegramUserData telegramUserData = new();

		public DateTime startTime = DateTime.UtcNow;
		public SettingsFileWatcher settingsWatcher = null;

		public event EventHandler OnError;

		public dynamic InstanceSettings { get; private set; }
		private string InstanceSettingsPath { get; set; }
		public string InstanceAlias { get; private set; }
		public UserData UserData {
			set {
				userData = value;
			}
			get {
				return userData;
			}
		}
		public TelegramUserData TelegramUserData {
			set {
				telegramUserData = value;
			}
			get {
				return telegramUserData;
			}
		}
		public IOgameService OgamedInstance {
			get {
				return _ogameService;
			}
		}
		public ICalculationService HelperService {
			get {
				return _helpersService;
			}
		}
		public IFleetScheduler FleetScheduler {
			get {
				return _fleetScheduler;
			}
		}
		public long SleepDuration { get; set; }
		public DateTime NextWakeUpTime { get; set; }

		public TBotMain(
			IOgameService ogameService,
			IFleetScheduler fleetScheduler,
			ICalculationService helpersService,
			IWorkerFactory workerFactory,
			ILoggerService<TBotMain> logger) {

			_ogameService = ogameService;
			_logger = logger;
			_helpersService = helpersService;
			_fleetScheduler = fleetScheduler;
			_workerFactory = workerFactory;
			_tbotOgameBridge = new TBotOgamedBridge(this, _ogameService);

			// Avoid circular dependency
			_fleetScheduler.SetTBotInstance(this);
			_fleetScheduler.SetTBotOgameBridge(_tbotOgameBridge);

			_ogameService.OnError += _ogameService_OnError;
		}

		private void _ogameService_OnError(object sender, EventArgs e) {
			log(LogLevel.Information, LogSender.Tbot, "Ogamed encountered an error.");
			if (OnError != null) {
				OnError(this, EventArgs.Empty);
			}
		}

		private Credentials GetCredentialsFromSettings() {
			return new() {
				Universe = ((string) InstanceSettings.Credentials.Universe).FirstCharToUpper(),
				Username = (string) InstanceSettings.Credentials.Email,
				Password = (string) InstanceSettings.Credentials.Password,
				Language = ((string) InstanceSettings.Credentials.Language).ToLower(),
				IsLobbyPioneers = (bool) InstanceSettings.Credentials.LobbyPioneers,
				BasicAuthUsername = (string) InstanceSettings.Credentials.BasicAuth.Username,
				BasicAuthPassword = (string) InstanceSettings.Credentials.BasicAuth.Password
			};
		}

		private Device GetDeviceFromSettings() {
			return new() {
				Name = ((string) InstanceSettings.Credentials.DeviceConf.Name).ToUpper(),
				System = (string) InstanceSettings.Credentials.DeviceConf.System ?? "",
				Browser = (string) InstanceSettings.Credentials.DeviceConf.Browser ?? "",
				UserAgent = (string) InstanceSettings.Credentials.DeviceConf.UserAgent ?? "",
				Memory = (int) InstanceSettings.Credentials.DeviceConf.Memory,
				Concurrency = (int) InstanceSettings.Credentials.DeviceConf.Concurrency,
				Color = (int) InstanceSettings.Credentials.DeviceConf.Color,
				Width = (int) InstanceSettings.Credentials.DeviceConf.Width,
				Height = (int) InstanceSettings.Credentials.DeviceConf.Height,
				Timezone = (string) InstanceSettings.Credentials.DeviceConf.Timezone ?? "",
				Lang = (string) InstanceSettings.Credentials.DeviceConf.Lang ?? ""
			};
		}


		private async Task InitializeOgame() {
			string host = (string) InstanceSettings.General.Host ?? "localhost";
			string port = (string) InstanceSettings.General.Port ?? "8080";
			if (!_ogameService.IsPortAvailable(host, int.Parse(port))) {
				throw new Exception("Port " + port + " is not available");
			}

			string captchaKey = (string) InstanceSettings.General.CaptchaAPIKey ?? "";
			ProxySettings proxy = new();
			string cookiesPath = "cookies" + (string) InstanceSettings.Credentials.Email + ".txt";

			if ((bool) InstanceSettings.General.Proxy.Enabled && (string) InstanceSettings.General.Proxy.Address != "") {
				log(LogLevel.Information, LogSender.Tbot, "Initializing proxy");
				string proxyType = ((string) InstanceSettings.General.Proxy.Type).ToLower();
				string proxyAddress = (string) InstanceSettings.General.Proxy.Address;
				if (proxyType == "https") {
					proxyType = "http";
				}
				if (proxyType == "http" && proxyAddress.Contains(':') && !proxyAddress.StartsWith("http://")) {
					proxyAddress = "http://" + proxyAddress;
				}
				if (proxyType == "socks5" || proxyType == "http") {
					proxy.Enabled = (bool) InstanceSettings.General.Proxy.Enabled;
					proxy.Address = proxyAddress;
					proxy.Type = proxyType;
					proxy.Username = (string) InstanceSettings.General.Proxy.Username ?? "";
					proxy.Password = (string) InstanceSettings.General.Proxy.Password ?? "";
				} else {
					log(LogLevel.Warning, LogSender.Tbot, "Unable to initialize proxy: unsupported proxy type");
					log(LogLevel.Warning, LogSender.Tbot, "Press enter to continue");
				}
			}


			_ogameService.Initialize(GetCredentialsFromSettings(), GetDeviceFromSettings(), proxy, (string) host, int.Parse(port), (string) captchaKey);
			await _ogameService.SetUserAgent((string) InstanceSettings.Credentials.DeviceConf.UserAgent);
		}

		private async Task ResolveCaptcha() {

			var captchaChallenge = await _ogameService.GetCaptchaChallenge();
			if (captchaChallenge.Id.Length == 0) {
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
			userData.serverInfo = await _tbotOgameBridge.UpdateServerInfo();
			userData.serverData = await _tbotOgameBridge.UpdateServerData();
			userData.userInfo = await _tbotOgameBridge.UpdateUserInfo();
			userData.staff = await _tbotOgameBridge.UpdateStaff();

			var serverTime = await _tbotOgameBridge.GetDateTime();

			log(LogLevel.Information, LogSender.Tbot, $"Server time: {serverTime.ToString()}");
			log(LogLevel.Information, LogSender.Tbot, $"Player name: {userData.userInfo.PlayerName}");
			log(LogLevel.Information, LogSender.Tbot, $"Player class: {userData.userInfo.Class.ToString()}");
			log(LogLevel.Information, LogSender.Tbot, $"Player rank: {userData.userInfo.Rank}");
			log(LogLevel.Information, LogSender.Tbot, $"Player points: {userData.userInfo.Points}");
			log(LogLevel.Information, LogSender.Tbot, $"Player honour points: {userData.userInfo.HonourPoints}");
		}

		public async Task Init(string settingPath,
			string alias,
			ITelegramMessenger telegramHandler) {

			InstanceSettingsPath = settingPath;
			InstanceAlias = alias;
			InstanceSettings = await SettingsService.GetSettings(settingPath);

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
			} catch (System.Net.Http.HttpRequestException) {
				try {
					await ResolveCaptcha();
				} catch (Exception ex) {
					log(LogLevel.Error, LogSender.Tbot, $"Unable to login after captcha. (\"{ex.Message}\")");
					throw new UnableToLoginException("Unable to login", ex);
				}
			} catch (Exception e) {
				log(LogLevel.Error, LogSender.Tbot, $"Unable to login. (\"{e.Message}\")");
				throw new UnableToLoginException("Unable to login", e);
			}

			await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));

			loggedIn = true;
			log(LogLevel.Information, LogSender.Tbot, "Logged in!");

			await InitUserData();

			var isVacationMode = await _ogameService.IsVacationMode();

			if (isVacationMode) {
				log(LogLevel.Warning, LogSender.Tbot, "Account in vacation mode");
				loggedIn = false;
				await _ogameService.Logout();
				throw new AccountInVacationModeException("Account is in vacation mode");
			}

			if (telegramMessenger != null) {
				await telegramMessenger.AddTbotInstance(this, _tbotOgameBridge);
			}
			userData.lastDOIR = 0;
			userData.nextDOIR = 0;

			log(LogLevel.Information, LogSender.Tbot, "Initializing data...");
			userData.celestials = await _tbotOgameBridge.GetPlanets();
			userData.researches = await _tbotOgameBridge.UpdateResearches();
			userData.scheduledFleets = new();
			userData.farmTargets = new();

			if (userData.celestials.Count == 1) {
				await EditSettings(userData.celestials.First());
				InstanceSettings = await SettingsService.GetSettings(InstanceSettingsPath);
			}

			// SleepMode will know if features must be enabled or not
			InitializeSleepMode();

			// Up and running. Lets initialize notification for settings file
			settingsWatcher = new SettingsFileWatcher(OnSettingsChanged, InstanceSettingsPath);
		}

		public async ValueTask DisposeAsync() {
			log(LogLevel.Information, LogSender.Tbot, "Deinitializing instance...");

			if (settingsWatcher != null) {
				settingsWatcher.deinitWatch();
				settingsWatcher = null;
			}

			if (telegramMessenger != null) {
				log(LogLevel.Information, LogSender.Tbot, "Removing instance from telegram...");
				await telegramMessenger.RemoveTBotInstance(this);
			}

			// Deinitialize workers
			List<Task> deInitTasks = new();
			foreach (var worker in workers) {
				log(LogLevel.Information, LogSender.Tbot, $"Deinitializing worker {worker.Value.GetWorkerName()}");

				deInitTasks.Add(worker.Value.StopWorker());
			}
			await Task.WhenAll(deInitTasks);
			deInitTasks.Clear();

			log(LogLevel.Information, LogSender.Tbot, "Deinitializing timers...");
			foreach (var entry in timers) {
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

		public override string ToString() {
			if (loggedIn && (userData.userInfo != null) && (userData.serverData != null))
				return $"{userData.userInfo.PlayerName}@{userData.serverData.Name}";
			else
				return $"{InstanceAlias}";
		}

		public void log(LogLevel logLevel, LogSender sender, string format) {
			_logger.WriteLog(logLevel, sender, $"[{ToString()}] {format}");
		}

		private async void InitializeFeatures() {
			foreach(var feat in Features.AllFeatures) {
				await InitializeFeature(feat);
			}
		}

		public async Task InitializeFeature(Feature feat) {
			if (feat == Feature.SleepMode) {
				return;// always enabled
			}
			long dueTime = feat switch {
				Feature.Defender => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
				Feature.BrainAutobuildCargo => RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo),
				Feature.BrainAutoRepatriate => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.BrainAutoMine => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
				Feature.BrainLifeformAutoMine => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
				Feature.BrainLifeformAutoResearch => RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds),
				Feature.BrainOfferOfTheDay => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.BrainAutoResearch => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.AutoFarm => RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo),
				Feature.Expeditions => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.Harvest => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.Colonize => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				Feature.AutoDiscovery => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds),
				_ => RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds)
			};

			// Its ok to add an infinite period time, since each worker will change its period accordingly after first execution
			if (workers.TryGetValue(feat, out var worker)) {
				worker.RestartWorker(cts.Token, Timeout.InfiniteTimeSpan, TimeSpan.FromMilliseconds(dueTime));
			} else {
				ITBotWorker newWorker = _workerFactory.InitializeWorker(feat, this, _tbotOgameBridge);
				if (newWorker != null) {
					workers.TryAdd(feat, newWorker);
					await newWorker.StartWorker(cts.Token, TimeSpan.FromMilliseconds(dueTime));
				} else {
					log(LogLevel.Warning, LogSender.Tbot, $"Cannot start worker for {feat.ToString()}");
				}
			}
		}

		public async Task StopFeature(Feature feat) {
			if (workers.TryGetValue(feat, out var worker)) {
				await worker.StopWorker();
			}
		}

		public bool IsFeatureRunning(Feature feat) {
			if (workers.TryGetValue(feat, out var worker)) {
				return worker.IsWorkerRunning();
			} else if (feat == Feature.SleepMode) {
				return true;    // Always running
			} else {
				return false;
			}
		}

		public async Task<bool> EditSettings(Celestial celestial = null, Feature feature = Feature.Null, string recall = "", int cargo = 0) {
			await Task.Delay(500, cts.Token);
			var file = await SettingsService.GetSettingsFileContents(Path.GetFullPath(InstanceSettingsPath));
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
					jsonObj["Brain"]["Transports"]["Origin"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["Brain"]["Transports"]["Origin"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["Brain"]["Transports"]["Origin"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["Brain"]["Transports"]["Origin"]["Type"] = type;
				}

				if (feature == Feature.BrainAutoResearch || feature == Feature.Null) {
					jsonObj["Brain"]["AutoResearch"]["Target"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["Brain"]["AutoResearch"]["Target"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["Brain"]["AutoResearch"]["Target"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["Brain"]["AutoResearch"]["Target"]["Type"] = "Planet";

					jsonObj["Brain"]["Transports"]["Origin"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["Brain"]["Transports"]["Origin"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["Brain"]["Transports"]["Origin"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["Brain"]["Transports"]["Origin"]["Type"] = type;
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

				if (feature == Feature.AutoDiscovery || feature == Feature.Null) {
					jsonObj["AutoDiscovery"]["Origin"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["AutoDiscovery"]["Origin"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["AutoDiscovery"]["Origin"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["AutoDiscovery"]["Origin"]["Type"] = type;
				}

				if (feature == Feature.Colonize || feature == Feature.Null) {
					jsonObj["AutoColonize"]["Origin"]["Galaxy"] = (int) celestial.Coordinate.Galaxy;
					jsonObj["AutoColonize"]["Origin"]["System"] = (int) celestial.Coordinate.System;
					jsonObj["AutoColonize"]["Origin"]["Position"] = (int) celestial.Coordinate.Position;
					jsonObj["AutoColonize"]["Origin"]["Type"] = "Planet";
				}
			}

			string output = Newtonsoft.Json.JsonConvert.SerializeObject(jsonObj, Newtonsoft.Json.Formatting.Indented);
			await SettingsService.WriteSettings(Path.GetFullPath(InstanceSettingsPath), output);

			return true;
		}

		public async Task WaitFeature() {
			List<Task> tasks = new List<Task>();
			List<SemaphoreSlim> semaphores = new List<SemaphoreSlim>();
			foreach (var feat in Features.AllFeatures) {
				ITBotWorker worker = _workerFactory.GetWorker(feat);
				if (worker != null) {
					var semaphore = worker.GetSemaphore();
					if (!semaphores.Contains(semaphore)) {
						tasks.Add(semaphore.WaitAsync()); //Avoid waiting the same semaphore more than once (Brain)
						semaphores.Add(semaphore);
					}
				}
			}
			await Task.WhenAll(
				tasks.ToArray()
			);
		}

		public void releaseFeature() {
			foreach (var feat in Features.AllFeatures) {
				ITBotWorker worker = _workerFactory.GetWorker(feat);
				if (worker != null) {
					worker.ReleaseWorker();
				}
			}
		}
		private DateTime _lastReloadFinished = DateTime.MinValue;
		private async void OnSettingsChanged() {
			DateTime onSettingsLaunched = DateTime.Now;
			await _settingsReloadSemaphore.WaitAsync();
			try {
				if (_lastReloadFinished > onSettingsLaunched) {
					return;
				}
				log(LogLevel.Information, LogSender.Tbot, "Settings file change detected! Waiting workers to complete ongoing activities...");

				// Wait on feature to be shut down
				cts.Cancel();
				foreach (var worker in workers) {
					log(LogLevel.Information, LogSender.Tbot, $"Stopping feature {worker.Key.ToString()}...");
					await worker.Value.StopWorker();
					log(LogLevel.Information, LogSender.Tbot, $"Feature {worker.Key.ToString()} stopped for settings reload!");
				}

				log(LogLevel.Information, LogSender.Tbot, "Reloading Settings file");
				InstanceSettings = await SettingsService.GetSettings(InstanceSettingsPath);

				cts = new();
				// If wakeUp, then all features will be restored
				await HandleSleepModeAsync(null);
				_lastReloadFinished = DateTime.Now;
			}
			finally {
				_settingsReloadSemaphore.Release();
			}
			
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
			userData.fleets = (await FleetScheduler.UpdateFleets()).Where(f => !f.ReturnFlight).ToList();
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
				await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Constructions);
				if ((int) celestial.Constructions.BuildingID == (int) Buildables.NaniteFactory || (int) celestial.Constructions.BuildingID == (int) Buildables.Shipyard) {
					results += $"{celestial.Coordinate.ToString()}: Shipyard or Nanite in construction\n";
					continue;
				}
				await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
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
					foreach (KeyValuePair<string, AuctionResourcesValue> item in auction.Resources) {
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
			_fleetScheduler.Collect();

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

			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Resources);
			origin = await _tbotOgameBridge.UpdatePlanet(origin, UpdateTypes.Ships);

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
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);

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
			int fleetId = await _fleetScheduler.SendFleet(celestial, celestial.Ships, destination, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

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

			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);

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
			int fleetId = await _fleetScheduler.SendFleet(celestial, celestial.Ships, dest, Missions.Deploy, speed, payload, userData.userInfo.Class, true);

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
			userData.celestials = await _tbotOgameBridge.UpdateCelestials();

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
					.Where(c => c.Coordinate.Galaxy == (int) InstanceSettings.Brain.Transports.Origin.Galaxy)
					.Where(c => c.Coordinate.System == (int) InstanceSettings.Brain.Transports.Origin.System)
					.Where(c => c.Coordinate.Position == (int) InstanceSettings.Brain.Transports.Origin.Position)
					.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) InstanceSettings.Brain.Transports.Origin.Type))
					.SingleOrDefault() ?? new() { ID = 0 };

				if (celestial.ID == 0) {
					await SendTelegramMessage("Error! Could not parse Celestial from JSON InstanceSettings. Need <code>/editsettings</code>");
					return new Celestial();
				}

				telegramUserData.CurrentCelestial = celestial;
			}

			return telegramUserData.CurrentCelestial;
		}

		public async Task TelegramGetInfo(Celestial celestial) {

			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Resources);
			celestial = await _tbotOgameBridge.UpdatePlanet(celestial, UpdateTypes.Ships);
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

		public async Task TelegramCollectDeut(long MinAmount = 0) {
			await _fleetScheduler.CollectDeut(MinAmount);
		}

		public async Task SleepNow(DateTime WakeUpTime) {
			long interval;

			DateTime time = await _tbotOgameBridge.GetDateTime();
			interval = (long) WakeUpTime.Subtract(time).TotalMilliseconds;
			timers.Add("TelegramSleepModeTimer", new Timer(WakeUpNow, null, interval, Timeout.Infinite));
			await SendTelegramMessage($"Going to sleep, Waking Up at {WakeUpTime.ToString()}");
			log(LogLevel.Information, LogSender.SleepMode, $"Going to sleep..., Waking Up at {WakeUpTime.ToString()}");
			if (userData.isSleeping == false) {
				if (
					SettingsService.IsSettingSet(InstanceSettings, "SleepMode") &&
					SettingsService.IsSettingSet(InstanceSettings.SleepMode, "LogoutOnSleep") &&
					(bool) InstanceSettings.SleepMode.LogoutOnSleep
				) {
					loggedIn = false;
					await _ogameService.Logout();
					log(LogLevel.Information, LogSender.SleepMode, $"Logged out from ogamed.");
				}

				userData.isSleeping = true;
			}
		}
		private async void HandleSleepMode(object state) {
			await HandleSleepModeAsync(state);
		}
		private async Task HandleSleepModeAsync(object state) {
			if (timers.TryGetValue("TelegramSleepModeTimer", out Timer value)) {
				return;
			}

			try {

				DateTime time = await _tbotOgameBridge.GetDateTime();

				if (!(bool) InstanceSettings.SleepMode.Active) {
					log(LogLevel.Warning, LogSender.SleepMode, "Sleep mode is disabled");
					await WakeUpAsync(null);
				} else if (!DateTime.TryParse((string) InstanceSettings.SleepMode.GoToSleep, out DateTime goToSleep)) {
					log(LogLevel.Warning, LogSender.SleepMode, "Unable to parse GoToSleep time. Sleep mode will be disabled");
					await WakeUpAsync(null);
				} else if (!DateTime.TryParse((string) InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp)) {
					log(LogLevel.Warning, LogSender.SleepMode, "Unable to parse WakeUp time. Sleep mode will be disabled");
					await WakeUpAsync(null);
				} else if (goToSleep == wakeUp) {
					log(LogLevel.Warning, LogSender.SleepMode, "GoToSleep time and WakeUp time must be different. Sleep mode will be disabled");
					await WakeUpAsync(null);
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
								await GoToSleepAsync(newTime);
							} else {
								// YES YES NO
								// AWAKE
								// GO TO SLEEP NEXT DAY
								interval = (long) goToSleep.AddDays(1).Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await WakeUpAsync(newTime);
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
								await GoToSleepAsync(newTime);
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
								await WakeUpAsync(newTime);
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
								await GoToSleepAsync(newTime);
							} else {
								// NO NO NO
								// AWAKE
								// GO TO SLEEP SAME DAY
								interval = (long) goToSleep.Subtract(time).TotalMilliseconds + (long) RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
								if (interval <= 0)
									interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
								DateTime newTime = time.AddMilliseconds(interval);
								await WakeUpAsync(newTime);
							}
						}
					}
				}
			} catch (Exception e) {
				log(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while handling sleep mode: {e.Message}");
				log(LogLevel.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AMinuteOrTwo);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			} finally {
			}
		}

		private async void GoToSleep(object state) {
			await GoToSleepAsync(state);
		}
		private async Task GoToSleepAsync(object state) {
			try {
				userData.fleets = await _fleetScheduler.UpdateFleets();
				bool delayed = false;
				if ((bool) InstanceSettings.SleepMode.PreventIfThereAreFleets && userData.fleets.Count() > 0) {
					if (DateTime.TryParse((string) InstanceSettings.SleepMode.WakeUp, out DateTime wakeUp) && DateTime.TryParse((string) InstanceSettings.SleepMode.GoToSleep, out DateTime goToSleep)) {
						DateTime time = await _tbotOgameBridge.GetDateTime();
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
							if ((bool) InstanceSettings.SleepMode.TelegramMessenger.Active) {
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

					if ((bool) InstanceSettings.SleepMode.AutoFleetSave.Active) {
						var celestialsToFleetsave = await _tbotOgameBridge.UpdatePlanets(UpdateTypes.Ships);
						if ((bool) InstanceSettings.SleepMode.AutoFleetSave.OnlyMoons)
							celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
						foreach (Celestial celestial in celestialsToFleetsave.OrderByDescending(c => c.Ships.GetFleetPoints())) {
							try {
								await _fleetScheduler.AutoFleetSave(celestial, true);
							} catch (Exception e) {
								_logger.WriteLog(LogLevel.Warning, LogSender.SleepMode, $"An error has occurred while fleetsaving: {e.Message}");
							}
						}
					}

					if ((bool) InstanceSettings.SleepMode.TelegramMessenger.Active && state != null) {
						await SendTelegramMessage($"Going to sleep, Waking Up at {state.ToString()}");
					}
					if (userData.isSleeping == false) {
						if (
							SettingsService.IsSettingSet(InstanceSettings, "SleepMode") &&
							SettingsService.IsSettingSet(InstanceSettings.SleepMode, "LogoutOnSleep") &&
							(bool) InstanceSettings.SleepMode.LogoutOnSleep
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
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
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
					SettingsService.IsSettingSet(InstanceSettings, "SleepMode") &&
					SettingsService.IsSettingSet(InstanceSettings.SleepMode, "LogoutOnSleep") &&
					(bool) InstanceSettings.SleepMode.LogoutOnSleep
				) {
					await _ogameService.Login();
					loggedIn = true;
					log(LogLevel.Information, LogSender.SleepMode, "Ogamed logged in again!");
				}
			}
			InitializeFeatures();
		}

		private async void WakeUp(object state) {
			await WakeUpAsync(state);
		}

		private async Task WakeUpAsync(object state) {
			try {
				log(LogLevel.Information, LogSender.SleepMode, "Waking Up...");
				if ((bool) InstanceSettings.SleepMode.TelegramMessenger.Active && state != null) {
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
				DateTime time = await _tbotOgameBridge.GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				log(LogLevel.Information, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				await _tbotOgameBridge.CheckCelestials();
			}
		}

		public async Task TelegramRetireFleet(int fleetId) {
			userData.fleets = await _fleetScheduler.UpdateFleets();
			Fleet ToRecallFleet = userData.fleets.SingleOrDefault(f => f.ID == fleetId) ?? new() { ID = (int) SendFleetCode.GenericError };
			if (ToRecallFleet.ID == (int) SendFleetCode.GenericError) {
				await SendTelegramMessage($"Unable to recall fleet! Already recalled?");
				return;
			}
			_fleetScheduler.RetireFleet(ToRecallFleet);
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
	}
}
