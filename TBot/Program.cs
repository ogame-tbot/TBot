using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Tbot.Includes;
using Tbot.Model;
using Tbot.Services;

namespace Tbot {
	class Program {
		static volatile OgamedService ogamedService;
		static volatile TelegramMessenger telegramMessenger;
		static volatile Dictionary<string, Timer> timers;
		static volatile dynamic settings;
		static volatile Server serverInfo;
		static volatile ServerData serverData;
		static volatile UserInfo userInfo;
		static volatile List<Celestial> celestials;
		static volatile List<Fleet> fleets;
		static volatile List<AttackerFleet> attacks;
		static volatile Slots slots;
		static volatile Researches researches;
		static volatile ConcurrentDictionary<Feature, bool> features;
		static volatile List<FleetSchedule> scheduledFleets;
		static volatile List<FarmTarget> farmTargets;
		static volatile Staff staff;
		static volatile bool isSleeping;
		static Dictionary<Feature, Semaphore> xaSem = new();

		static void Main(string[] args) {
			Helpers.SetTitle();
			isSleeping = false;

			ReadSettings();
			FileSystemWatcher settingsWatcher = new(Path.GetFullPath(AppContext.BaseDirectory));
			settingsWatcher.Filter = "settings.json";
			settingsWatcher.NotifyFilter = NotifyFilters.LastWrite;
			settingsWatcher.Changed += new(OnSettingsChanged);
			settingsWatcher.EnableRaisingEvents = true;

			Credentials credentials = new() {
				Universe = (string) settings.Credentials.Universe,
				Username = (string) settings.Credentials.Email,
				Password = (string) settings.Credentials.Password,
				Language = (string) settings.Credentials.Language,
				IsLobbyPioneers = (bool) settings.Credentials.LobbyPioneers,
				BasicAuthUsername = (string) settings.Credentials.BasicAuth.Username,
				BasicAuthPassword = (string) settings.Credentials.BasicAuth.Password
			};

			try {
				string host = (string) settings.General.Host ?? "localhost";
				string port = (string) settings.General.Port ?? "8080";
				string captchaKey = (string) settings.General.CaptchaAPIKey ?? "";
				ProxySettings proxy = new();
				if ((bool) settings.General.Proxy.Enabled && (string) settings.General.Proxy.Address != "") {
					Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing proxy");
					if ((string) settings.General.Proxy.Type == "socks5" || (string) settings.General.Proxy.Type == "https") {
						proxy.Enabled = (bool) settings.General.Proxy.Enabled;
						proxy.Address = (string) settings.General.Proxy.Address;
						proxy.Type = (string) settings.General.Proxy.Type ?? "socks5";
						proxy.Username = (string) settings.General.Proxy.Username ?? "";
						proxy.Password = (string) settings.General.Proxy.Password ?? "";
					}
					else {
						Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "Unable to initialize proxy: unsupported proxy type");
					}
				}
				ogamedService = new OgamedService(credentials, (string) host, int.Parse(port), (string) captchaKey, proxy);
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Tbot, $"Unable to start ogamed: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			}

			try {
				ogamedService.SetUserAgent((string) settings.General.UserAgent);
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Tbot, $"Unable to set user agent: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			}
			Thread.Sleep(Helpers.CalcRandomInterval(IntervalType.LessThanASecond));


			var isLoggedIn = false;
			try {
				isLoggedIn = ogamedService.Login();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Tbot, $"Unable to login: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
			}
			Thread.Sleep(Helpers.CalcRandomInterval(IntervalType.AFewSeconds));

			if (!isLoggedIn) {
				Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to login.");
				Console.ReadLine();
			} else {
				serverInfo = UpdateServerInfo();
				serverData = UpdateServerData();
				userInfo = UpdateUserInfo();
				staff = UpdateStaff();

				Helpers.WriteLog(LogType.Info, LogSender.Tbot, $"Server time: {GetDateTime().ToString()}");

				Helpers.WriteLog(LogType.Info, LogSender.Tbot, $"Player name: {userInfo.PlayerName}");
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, $"Player class: {userInfo.Class.ToString()}");
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, $"Player rank: {userInfo.Rank}");
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, $"Player points: {userInfo.Points}");
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, $"Player honour points: {userInfo.HonourPoints}");

				if (!ogamedService.IsVacationMode()) {
					if ((bool) settings.TelegramMessenger.Active) {
						Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Activating Telegram Messenger");
						telegramMessenger = new TelegramMessenger((string) settings.TelegramMessenger.API, (string) settings.TelegramMessenger.ChatId);
						telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] TBot activated");
					}

					timers = new Dictionary<string, Timer>();

					xaSem[Feature.Defender] = new Semaphore(1, 1);
					xaSem[Feature.Brain] = new Semaphore(1, 1);
					xaSem[Feature.BrainAutobuildCargo] = new Semaphore(1, 1);
					xaSem[Feature.BrainAutoRepatriate] = new Semaphore(1, 1);
					xaSem[Feature.BrainAutoMine] = new Semaphore(1, 1);
					xaSem[Feature.BrainOfferOfTheDay] = new Semaphore(1, 1);
					xaSem[Feature.AutoFarm] = new Semaphore(1, 1);
					xaSem[Feature.Expeditions] = new Semaphore(1, 1);
					xaSem[Feature.Harvest] = new Semaphore(1, 1);
					xaSem[Feature.Colonize] = new Semaphore(1, 1);
					xaSem[Feature.FleetScheduler] = new Semaphore(1, 1);
					xaSem[Feature.SleepMode] = new Semaphore(1, 1);

					features = new();
					features.AddOrUpdate(Feature.Defender, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.Brain, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.BrainAutobuildCargo, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.BrainAutoRepatriate, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.BrainAutoMine, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.BrainOfferOfTheDay, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.BrainAutoResearch, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.AutoFarm, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.Expeditions, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.Colonize, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.Harvest, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.FleetScheduler, false, HandleStartStopFeatures);
					features.AddOrUpdate(Feature.SleepMode, false, HandleStartStopFeatures);

					Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing data...");
					celestials = GetPlanets();
					researches = UpdateResearches();
					scheduledFleets = new();
					farmTargets = new();
					UpdateTitle(false);

					Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing features...");
					InitializeSleepMode();
				} else {
					Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "Account in vacation mode");
				}

				Console.ReadLine();
			}
		}

		private static bool HandleStartStopFeatures(Feature feature, bool currentValue) {
			if (isSleeping && (bool) settings.SleepMode.Active)
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

		private static void InitializeFeatures() {
			// features.AddOrUpdate(Feature.SleepMode, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.Defender, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.Brain, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.BrainAutobuildCargo, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.BrainAutoRepatriate, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.BrainAutoMine, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.BrainOfferOfTheDay, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.BrainAutoResearch, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.AutoFarm, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.Expeditions, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.Harvest, false, HandleStartStopFeatures);
			features.AddOrUpdate(Feature.Colonize, false, HandleStartStopFeatures);
		}

		private static void ReadSettings() {
			settings = SettingsService.GetSettings();
		}

		private static void OnSettingsChanged(object sender, FileSystemEventArgs e) {
			if (e.ChangeType != WatcherChangeTypes.Changed) {
				return;
			}

			xaSem[Feature.Defender].WaitOne();
			xaSem[Feature.Brain].WaitOne();
			xaSem[Feature.Expeditions].WaitOne();
			xaSem[Feature.Harvest].WaitOne();
			xaSem[Feature.Colonize].WaitOne();
			xaSem[Feature.AutoFarm].WaitOne();
			xaSem[Feature.SleepMode].WaitOne();

			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Settings file changed");
			ReadSettings();

			xaSem[Feature.Defender].Release();
			xaSem[Feature.Brain].Release();
			xaSem[Feature.Expeditions].Release();
			xaSem[Feature.Harvest].Release();
			xaSem[Feature.Colonize].Release();
			xaSem[Feature.AutoFarm].Release();
			xaSem[Feature.SleepMode].Release();

			InitializeSleepMode();
			UpdateTitle();
		}

		private static DateTime GetDateTime() {
			try {
				DateTime dateTime = ogamedService.GetServerTime();
				if (dateTime.Kind == DateTimeKind.Utc)
					return dateTime.ToLocalTime();
				else
					return dateTime;
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"GetDateTime() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				var fallback = DateTime.Now;
				if (fallback.Kind == DateTimeKind.Utc)
					return fallback.ToLocalTime();
				else
					return fallback;
			}
		}

		private static Slots UpdateSlots() {
			try {
				return ogamedService.GetSlots();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateSlots() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private static List<Fleet> UpdateFleets() {
			try {
				return ogamedService.GetFleets();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateFleets() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private static List<GalaxyInfo> UpdateGalaxyInfos() {
			try {
				List<GalaxyInfo> galaxyInfos = new();
				Planet newPlanet = new();
				List<Celestial> newCelestials = celestials.ToList();
				foreach (Planet planet in celestials.Where(p => p is Planet)) {
					newPlanet = planet;
					var gi = ogamedService.GetGalaxyInfo(planet.Coordinate);
					if (gi.Planets.Any(p => p != null && p.ID == planet.ID)) {
						newPlanet.Debris = gi.Planets.Single(p => p != null && p.ID == planet.ID).Debris;
						galaxyInfos.Add(gi);
					}

					if (celestials.Any(p => p.HasCoords(newPlanet.Coordinate))) {
						Planet oldPlanet = celestials.Unique().SingleOrDefault(p => p.HasCoords(newPlanet.Coordinate)) as Planet;
						newCelestials.Remove(oldPlanet);
						newCelestials.Add(newPlanet);
					}
				}
				celestials = newCelestials;
				return galaxyInfos;
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateGalaxyInfos() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private static ServerData UpdateServerData() {
			try {
				return ogamedService.GetServerData();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateServerData() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private static Server UpdateServerInfo() {
			try {
				return ogamedService.GetServerInfo();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateServerInfo() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private static UserInfo UpdateUserInfo() {
			try {
				UserInfo user = ogamedService.GetUserInfo();
				user.Class = ogamedService.GetUserClass();
				return user;
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateUserInfo() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
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

		private static List<Celestial> UpdateCelestials() {
			try {
				return ogamedService.GetCelestials();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateCelestials() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return celestials ?? new();
			}
		}

		private static Researches UpdateResearches() {
			try {
				return ogamedService.GetResearches();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateResearches() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private static Staff UpdateStaff() {
			try {
				return ogamedService.GetStaff();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdateStaff() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return new();
			}
		}

		private static List<Celestial> GetPlanets() {
			List<Celestial> localPlanets = celestials ?? new();
			try {
				List<Celestial> ogamedPlanets = ogamedService.GetCelestials();
				if (localPlanets.Count == 0 || ogamedPlanets.Count != celestials.Count) {
					localPlanets = ogamedPlanets.ToList();
				}
				return localPlanets;
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"GetPlanets() Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return localPlanets;
			}
		}

		private static List<Celestial> UpdatePlanets(UpdateType updateType = UpdateType.Full) {
			// Helpers.WriteLog(LogType.Info, LogSender.Tbot, $"Updating celestials... Mode: {updateType.ToString()}");
			List<Celestial> localPlanets = GetPlanets();
			List<Celestial> newPlanets = new();
			try {
				foreach (Celestial planet in localPlanets) {
					newPlanets.Add(UpdatePlanet(planet, updateType));
				}
				return newPlanets;
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"UpdatePlanets({updateType.ToString()}) Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				return newPlanets;
			}
		}

		private static Celestial UpdatePlanet(Celestial planet, UpdateType updateType = UpdateType.Full) {
			// Helpers.WriteLog(LogType.Info, LogSender.Tbot, $"Updating {planet.ToString()}. Mode: {updateType.ToString()}");
			try {
				switch (updateType) {
					case UpdateType.Fast:
						planet = ogamedService.GetCelestial(planet);
						break;
					case UpdateType.Resources:
						planet.Resources = ogamedService.GetResources(planet);
						break;
					case UpdateType.Buildings:
						planet.Buildings = ogamedService.GetBuildings(planet);
						break;
					case UpdateType.Ships:
						planet.Ships = ogamedService.GetShips(planet);
						break;
					case UpdateType.Facilities:
						planet.Facilities = ogamedService.GetFacilities(planet);
						break;
					case UpdateType.Defences:
						planet.Defences = ogamedService.GetDefences(planet);
						break;
					case UpdateType.Productions:
						planet.Productions = ogamedService.GetProductions(planet);
						break;
					case UpdateType.Constructions:
						planet.Constructions = ogamedService.GetConstructions(planet);
						break;
					case UpdateType.ResourceSettings:
						if (planet is Planet) {
							planet.ResourceSettings = ogamedService.GetResourceSettings(planet as Planet);
						}
						break;
					case UpdateType.ResourcesProduction:
						if (planet is Planet) {
							planet.ResourcesProduction = ogamedService.GetResourcesProduction(planet as Planet);
						}
						break;
					case UpdateType.Techs:
						var techs = ogamedService.GetTechs(planet);
						planet.Defences = techs.defenses;
						planet.Facilities = techs.facilities;
						planet.Ships = techs.ships;
						planet.Buildings = techs.supplies;
						break;
					case UpdateType.Debris:
						if (planet is Moon)
							break;
						var galaxyInfo = ogamedService.GetGalaxyInfo(planet.Coordinate);
						var thisPlanetGalaxyInfo = galaxyInfo.Planets.Single(p => p != null && p.Coordinate.IsSame(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet)));
						planet.Debris = thisPlanetGalaxyInfo.Debris;
						break;
					case UpdateType.Full:
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
				Helpers.WriteLog(LogType.Debug, LogSender.Tbot, $"Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, $"Stacktrace: {e.StackTrace}");
				Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "An error has occurred. Skipping update");
			}
			return planet;
		}

		private static void UpdateTitle(bool force = true, bool underAttack = false) {
			CheckCelestials();
			if (force) {
				serverInfo = UpdateServerInfo();
				serverData = UpdateServerData();
				userInfo = UpdateUserInfo();
				staff = UpdateStaff();
				celestials = UpdateCelestials();
				researches = UpdateResearches();
			}
			string title = $"[{serverInfo.Name}.{serverInfo.Language}] {userInfo.PlayerName} - Rank: {userInfo.Rank} - http://{(string) settings.General.Host}:{(string) settings.General.Port}";
			if (
				(bool) settings.General.Proxy.Enabled &&
				(string) settings.General.Proxy.Address != "" &&
				((string) settings.General.Proxy.Type == "socks5" || (string) settings.General.Proxy.Type == "https")
			) {
				title += " (Proxy active)";
			}				
			if ((string) settings.General.CustomTitle != "") {
				title = $"{(string) settings.General.CustomTitle} - {title}";
			}				
			if (underAttack) {
				title = $"ENEMY ACTIVITY! - {title}";
			}

			Helpers.SetTitle(title);
		}

		private static void CheckCelestials() {
			try {
				if (!isSleeping && celestials.Count != UpdateCelestials().Count) {
					if (features.TryGetValue(Feature.BrainAutoMine, out bool value) && value)
						timers.Remove("AutoMineTimer");
					timers.Add("AutoMineTimer", new Timer(AutoMine, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
				}
				celestials = celestials.Unique().ToList();
			} catch { }
		}

		private static void InitializeDefender() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing defender...");
			StopDefender(false);
			timers.Add("DefenderTimer", new Timer(Defender, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		private static void StopDefender(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping defender...");
			if (timers.TryGetValue("DefenderTimer", out Timer value))
				value.Dispose();
			timers.Remove("DefenderTimer");
		}

		private static void InitializeBrainAutoCargo() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing autocargo...");
			StopBrainAutoCargo(false);
			timers.Add("CapacityTimer", new Timer(AutoBuildCargo, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Timeout.Infinite));
		}

		private static void StopBrainAutoCargo(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping autocargo...");
			if (timers.TryGetValue("CapacityTimer", out Timer value))
				value.Dispose();
			timers.Remove("CapacityTimer");
		}

		private static void InitializeBrainRepatriate() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing repatriate...");
			StopBrainRepatriate(false);
			timers.Add("RepatriateTimer", new Timer(AutoRepatriate, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private static void StopBrainRepatriate(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping repatriate...");
			if (timers.TryGetValue("RepatriateTimer", out Timer value))
				value.Dispose();
			timers.Remove("RepatriateTimer");
		}

		private static void InitializeBrainAutoMine() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing automine...");
			StopBrainAutoMine(false);
			timers.Add("AutoMineTimer", new Timer(AutoMine, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		private static void StopBrainAutoMine(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping automine...");
			if (timers.TryGetValue("AutoMineTimer", out Timer value))
				value.Dispose();
			timers.Remove("AutoMineTimer");
			foreach (var celestial in celestials) {
				if (timers.TryGetValue($"AutoMineTimer-{celestial.ID.ToString()}", out value))
					value.Dispose();
				timers.Remove($"AutoMineTimer-{celestial.ID.ToString()}");
			}
		}

		private static void InitializeBrainOfferOfTheDay() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing offer of the day...");
			StopBrainOfferOfTheDay(false);
			timers.Add("OfferOfTheDayTimer", new Timer(BuyOfferOfTheDay, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private static void StopBrainOfferOfTheDay(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping offer of the day...");
			if (timers.TryGetValue("OfferOfTheDayTimer", out Timer value))
				value.Dispose();
			timers.Remove("OfferOfTheDayTimer");
		}

		private static void InitializeBrainAutoResearch() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing autoresearch...");
			StopBrainAutoResearch(false);
			timers.Add("AutoResearchTimer", new Timer(AutoResearch, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite));
		}

		private static void StopBrainAutoResearch(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping autoresearch...");
			if (timers.TryGetValue("AutoResearchTimer", out Timer value))
				value.Dispose();
			timers.Remove("AutoResearchTimer");
		}

		private static void InitializeAutoFarm() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing autofarm...");
			StopAutoFarm(false);
			timers.Add("AutoFarmTimer", new Timer(AutoFarm, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Timeout.Infinite));
		}

		private static void StopAutoFarm(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping autofarm...");
			if (timers.TryGetValue("AutoFarmTimer", out Timer value))
				value.Dispose();
			timers.Remove("AutoFarmTimer");
		}

		private static void InitializeExpeditions() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing expeditions...");
			StopExpeditions(false);
			timers.Add("ExpeditionsTimer", new Timer(HandleExpeditions, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private static void StopExpeditions(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping expeditions...");
			if (timers.TryGetValue("ExpeditionsTimer", out Timer value))
				value.Dispose();
			timers.Remove("ExpeditionsTimer");
		}

		private static void InitializeHarvest() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing harvest...");
			StopHarvest(false);
			timers.Add("HarvestTimer", new Timer(HandleHarvest, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private static void StopHarvest(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping harvest...");
			if (timers.TryGetValue("HarvestTimer", out Timer value))
				value.Dispose();
			timers.Remove("HarvestTimer");
		}

		private static void InitializeColonize() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing colonize...");
			StopColonize(false);
			timers.Add("ColonizeTimer", new Timer(HandleColonize, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Timeout.Infinite));
		}

		private static void StopColonize(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping colonize...");
			if (timers.TryGetValue("ColonizeTimer", out Timer value))
				value.Dispose();
			timers.Remove("ColonizeTimer");
		}

		private static void InitializeSleepMode() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing sleep mode...");
			StopSleepMode(false);
			timers.Add("SleepModeTimer", new Timer(HandleSleepMode, null, 0, Timeout.Infinite));
		}

		private static void StopSleepMode(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping sleep mode...");
			if (timers.TryGetValue("SleepModeTimer", out Timer value))
				value.Dispose();
			timers.Remove("SleepModeTimer");
		}
		
		private static void InitializeFleetScheduler() {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing fleet scheduler...");
			scheduledFleets = new();
			StopFleetScheduler(false);
			timers.Add("FleetSchedulerTimer", new Timer(HandleScheduledFleet, null, Timeout.Infinite, Timeout.Infinite));
		}

		private static void StopFleetScheduler(bool echo = true) {
			if (echo)
				Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping fleet scheduler...");
			if (timers.TryGetValue("FleetSchedulerTimer", out Timer value))
				value.Dispose();
			timers.Remove("FleetSchedulerTimer");
		}

		private static void AutoFleetSave(Celestial celestial, bool isSleepTimeFleetSave = false, long minDuration = 0, bool forceUnsafe = false) {
			celestial = UpdatePlanet(celestial, UpdateType.Ships);
			if (celestial.Ships.GetMovableShips().IsEmpty()) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fleet to save!");
				return;
			}

			celestial = UpdatePlanet(celestial, UpdateType.Resources);
			Celestial destination = new() { ID = 0 };
			if (!forceUnsafe)
				forceUnsafe = (bool) settings.SleepMode.AutoFleetSave.ForceUnsafe;
			bool recall = false;

			if (celestial.Resources.Deuterium == 0) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: there is no fuel!");
				return;
			}
			long maxDeuterium = celestial.Resources.Deuterium;

			DateTime departureTime = GetDateTime();

			if (isSleepTimeFleetSave) {
				if (DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp)) {
					if (departureTime >= wakeUp)
						wakeUp = wakeUp.AddDays(1);
					minDuration = (long) wakeUp.Subtract(departureTime).TotalSeconds;
				} else {
					Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Could not plan fleetsave from {celestial.ToString()}: unable to parse comeback time");
					return;
				}
			}

			Missions mission = Missions.Deploy;
			FleetHypotesis fleetHypotesis = GetFleetSaveDestination(celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
			if ((bool) settings.SleepMode.AutoFleetSave.Recall)
				recall = true;
			if ((fleetHypotesis.Origin.Coordinate.Type == Celestials.Moon) || forceUnsafe) {
				if (fleetHypotesis.Destination.IsSame(new Coordinate(1, 1, 1, Celestials.Planet)) && celestial.Ships.EspionageProbe > 0) {
					mission = Missions.Spy;
					fleetHypotesis = GetFleetSaveDestination(celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				}
				if (fleetHypotesis.Destination.IsSame(new Coordinate(1, 1, 1, Celestials.Planet)) && celestial.Ships.ColonyShip > 0 && Helpers.CalcMaxPlanets(researches.Astrophysics) == celestials.Unique().Where(c => c.Coordinate.Type == Celestials.Planet).Count()) {
					mission = Missions.Colonize;
					fleetHypotesis = GetFleetSaveDestination(celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				}
				if (fleetHypotesis.Destination.IsSame(new Coordinate(1, 1, 1, Celestials.Planet)) && celestial.Ships.Recycler > 0) {
					mission = Missions.Harvest;
					fleetHypotesis = GetFleetSaveDestination(celestials, celestial, departureTime, minDuration, mission, maxDeuterium, forceUnsafe);
				}
			}
			if (celestial.Resources.Deuterium < fleetHypotesis.Fuel) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: not enough fuel!");
				return;
			}
			if (Helpers.CalcFleetFuelCapacity(fleetHypotesis.Ships, serverData.ProbeCargo) < fleetHypotesis.Fuel) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Skipping fleetsave from {celestial.ToString()}: ships don't have enough fuel capacity!");
				return;
			}

			var payload = celestial.Resources;
			if ((long) settings.SleepMode.AutoFleetSave.DeutToLeave > 0)
				payload.Deuterium -= (long) settings.SleepMode.AutoFleetSave.DeutToLeave;
			if (payload.Deuterium < 0)
				payload.Deuterium = 0;

			int fleetId = SendFleet(fleetHypotesis.Origin, fleetHypotesis.Ships, fleetHypotesis.Destination, fleetHypotesis.Mission, fleetHypotesis.Speed, payload, userInfo.Class, isSleepTimeFleetSave || forceUnsafe);
			if (recall && fleetId != 0) {
				Fleet fleet = fleets.Single(fleet => fleet.ID == fleetId);
				DateTime time = GetDateTime();
				var interval = ((minDuration / 2) * 1000) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
				if (interval <= 0)
					interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.Add($"RecallTimer-{fleetId.ToString()}", new Timer(RetireFleet, fleet, interval, Timeout.Infinite));
				Helpers.WriteLog(LogType.Info, LogSender.FleetScheduler, $"The fleet will be recalled at {newTime.ToString()}");
			}
		}

		private static FleetHypotesis GetFleetSaveDestination(List<Celestial> source, Celestial origin, DateTime departureDate, long minFlightTime, Missions mission, long maxFuel, bool forceUnsafe = false) {
			var validSpeeds = userInfo.Class == CharacterClass.General ? Speeds.GetGeneralSpeedsList() : Speeds.GetNonGeneralSpeedsList();
			List<FleetHypotesis> possibleFleets = new();
			List<Coordinate> possibleDestinations = new();

			origin = UpdatePlanet(origin, UpdateType.Resources);

			switch (mission) {
				case Missions.Deploy:
					possibleDestinations = celestials
						.Where(planet => planet.ID != origin.ID)
						.Where(planet => (planet.Coordinate.Type == Celestials.Moon) || forceUnsafe)
						.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon ? 0 : 1)
						.ThenBy(planet => Helpers.CalcDistance(origin.Coordinate, planet.Coordinate, serverData))
						.Select(planet => planet.Coordinate)
						.ToList();

					foreach (var possibleDestination in possibleDestinations) {
						foreach (var currentSpeed in validSpeeds) {
							FleetPrediction fleetPrediction = ogamedService.PredictFleet(origin, origin.Ships.GetMovableShips(), possibleDestination, mission, currentSpeed);
							FleetHypotesis fleetHypotesis = new() {
								Origin = origin,
								Destination = possibleDestination,
								Ships = origin.Ships.GetMovableShips(),
								Mission = mission,
								Speed = currentSpeed,
								Duration = fleetPrediction.Time,
								Fuel = fleetPrediction.Fuel
							};
							if (fleetHypotesis.Duration >= minFlightTime) {
								possibleFleets.Add(fleetHypotesis);
								break;
							}
						}
					}
					break;
				case Missions.Spy:
					Coordinate destination = new(origin.Coordinate.Galaxy, origin.Coordinate.System, 16, Celestials.Planet);
					foreach (var currentSpeed in validSpeeds) {
						FleetPrediction fleetPrediction = ogamedService.PredictFleet(origin, origin.Ships.GetMovableShips(), destination, mission, currentSpeed);
						FleetHypotesis fleetHypotesis = new() {
							Origin = origin,
							Destination = destination,
							Ships = origin.Ships.GetMovableShips(),
							Mission = mission,
							Speed = currentSpeed,
							Duration = fleetPrediction.Time,
							Fuel = fleetPrediction.Fuel
						};
						if (fleetHypotesis.Duration >= minFlightTime / 2) {
							possibleFleets.Add(fleetHypotesis);
							break;
						}
					}
					break;
				case Missions.Colonize:
					for (int pos = 1; pos <= 15; pos++) {
						if (pos == origin.Coordinate.Position)
							continue;
						GalaxyInfo galaxyInfo = ogamedService.GetGalaxyInfo(origin.Coordinate);
						List<int> occupiedPos = new();
						foreach (var planet in galaxyInfo.Planets) {
							occupiedPos.Add(planet.Coordinate.Position);
						}
						if (occupiedPos.Any(op => op == pos))
							continue;

						possibleDestinations.Add(new(origin.Coordinate.Galaxy, origin.Coordinate.System, pos, Celestials.Planet));
					}
					foreach (var possibleDestination in possibleDestinations) {
						foreach (var currentSpeed in validSpeeds) {
							FleetPrediction fleetPrediction = ogamedService.PredictFleet(origin, origin.Ships.GetMovableShips(), possibleDestination, mission, currentSpeed);
							FleetHypotesis fleetHypotesis = new() {
								Origin = origin,
								Destination = possibleDestination,
								Ships = origin.Ships.GetMovableShips(),
								Mission = mission,
								Speed = currentSpeed,
								Duration = fleetPrediction.Time,
								Fuel = fleetPrediction.Fuel
							};
							if (fleetHypotesis.Duration >= minFlightTime / 2) {
								possibleFleets.Add(fleetHypotesis);
								break;
							}
						}
					}
					break;
				case Missions.Harvest:
					for (int pos = 1; pos <= 15; pos++) {
						if (pos == origin.Coordinate.Position)
							continue;
						GalaxyInfo galaxyInfo = ogamedService.GetGalaxyInfo(origin.Coordinate);
						List<int> harvestablePos = new();
						foreach (var planet in galaxyInfo.Planets) {
							if (planet != null && planet.Debris != null && planet.Debris.Resources.TotalResources > 0)
								possibleDestinations.Add(new(origin.Coordinate.Galaxy, origin.Coordinate.System, pos, Celestials.Debris));
						}
					}
					foreach (var possibleDestination in possibleDestinations) {
						foreach (var currentSpeed in validSpeeds) {
							FleetPrediction fleetPrediction = ogamedService.PredictFleet(origin, origin.Ships.GetMovableShips(), possibleDestination, mission, currentSpeed);
							FleetHypotesis fleetHypotesis = new() {
								Origin = origin,
								Destination = possibleDestination,
								Ships = origin.Ships.GetMovableShips(),
								Mission = mission,
								Speed = currentSpeed,
								Duration = fleetPrediction.Time,
								Fuel = fleetPrediction.Fuel
							};
							if (fleetHypotesis.Duration >= minFlightTime / 2) {
								possibleFleets.Add(fleetHypotesis);
								break;
							}
						}
					}
					break;
				default:
					break;
			}
			if (possibleFleets.Count > 0) {
				return possibleFleets
					.Where(pf => pf.Fuel <= maxFuel)
					.OrderBy(pf => pf.Fuel)
					.ThenBy(pf => pf.Duration)
					.First();
			} else {
				mission = Missions.Transport;
				FleetPrediction fleetPrediction = ogamedService.PredictFleet(origin, origin.Ships.GetMovableShips(), new Coordinate(), mission, Speeds.TenPercent);
				return new() {
					Origin = origin,
					Destination = new Coordinate(),
					Ships = origin.Ships.GetMovableShips(),
					Mission = mission,
					Speed = Speeds.TenPercent,
					Duration = fleetPrediction.Time,
					Fuel = fleetPrediction.Fuel
				};
			}
		}

		private static void HandleSleepMode(object state) {
			try {
				xaSem[Feature.Defender].WaitOne();
				xaSem[Feature.Brain].WaitOne();
				xaSem[Feature.Expeditions].WaitOne();
				xaSem[Feature.Harvest].WaitOne();
				xaSem[Feature.Colonize].WaitOne();
				xaSem[Feature.AutoFarm].WaitOne();
				xaSem[Feature.SleepMode].WaitOne();

				DateTime time = GetDateTime();

				if (!(bool) settings.SleepMode.Active) {
					Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "Sleep mode is disabled");
					WakeUp(null);
				} else if (!DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep)) {
					Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "Unable to parse GoToSleep time. Sleep mode will be disabled");
					WakeUp(null);
				} else if (!DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp)) {
					Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "Unable to parse WakeUp time. Sleep mode will be disabled");
					WakeUp(null);
				} else if (goToSleep == wakeUp) {
					Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "GoToSleep time and WakeUp time must be different. Sleep mode will be disabled");
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
								Helpers.WriteLog(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
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
								Helpers.WriteLog(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
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
				Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, $"An error has occurred while handling sleep mode: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = GetDateTime();
				long interval = Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				UpdateTitle();
			} finally {
				xaSem[Feature.Defender].Release();
				xaSem[Feature.Brain].Release();
				xaSem[Feature.Expeditions].Release();
				xaSem[Feature.Harvest].Release();
				xaSem[Feature.Colonize].Release();
				xaSem[Feature.AutoFarm].Release();
				xaSem[Feature.SleepMode].Release();
			}
		}

		private static void GoToSleep(object state) {
			try {
				fleets = UpdateFleets();
				bool delayed = false;
				if ((bool) settings.SleepMode.PreventIfThereAreFleets && fleets.Count > 0) {
					if (DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp) && DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep)) {
						DateTime time = GetDateTime();
						if (time >= goToSleep && time >= wakeUp && goToSleep < wakeUp)
							goToSleep = goToSleep.AddDays(1);
						if (time >= goToSleep && time >= wakeUp && goToSleep >= wakeUp)
							wakeUp = wakeUp.AddDays(1);

						List<Fleet> tempFleets = new();
						var timeToWakeup = wakeUp.Subtract(time).TotalSeconds;
						// All Deployment Missions that will arrive during sleep
						tempFleets.AddRange(fleets
							.Where(f => f.Mission == Missions.Deploy)
							.Where(f => f.ArriveIn <= timeToWakeup)
						);
						// All other Fleets.Mission
						tempFleets.AddRange(fleets
							.Where(f => f.BackIn <= timeToWakeup)
						);
						if (tempFleets.Count > 0) {
							Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "There are fleets that would come back during sleep time. Delaying sleep mode.");
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
							interval *= 1000;
							if (interval <= 0)
								interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							DateTime newTime = time.AddMilliseconds(interval);
							timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
							delayed = true;
							Helpers.WriteLog(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
						}
					} else {
						Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "Unable to parse WakeUp or GoToSleep time.");
					}
				}
				if (!delayed) {
					// if ((bool)settings.SleepMode.AutoFleetSave.RunAutoMineFirst)
					//     AutoMine(null);
					// if ((bool)settings.SleepMode.AutoFleetSave.RunAutoResearchFirst)
					//     AutoResearch(null);

					Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "Going to sleep...");
					Helpers.WriteLog(LogType.Info, LogSender.SleepMode, $"Waking Up at {state.ToString()}");

					if ((bool) settings.SleepMode.AutoFleetSave.Active) {
						var celestialsToFleetsave = UpdateCelestials();
						if ((bool) settings.SleepMode.AutoFleetSave.OnlyMoons)
							celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
						foreach (Celestial celestial in celestialsToFleetsave)
							AutoFleetSave(celestial, true);
					}

					if ((bool) settings.TelegramMessenger.Active && (bool) settings.SleepMode.TelegramMessenger.Active && state != null) {
						telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] Going to sleep");
						telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] Waking Up at {state.ToString()}");
					}
					isSleeping = true;
				}
				InitializeFeatures();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, $"An error has occurred while going to sleep: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = GetDateTime();
				int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				UpdateTitle();
			}
		}

		private static void WakeUp(object state) {
			try {
				Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "Waking Up...");
				if ((bool) settings.TelegramMessenger.Active && (bool) settings.SleepMode.TelegramMessenger.Active && state != null) {
					telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] Waking up");
					telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] Going to sleep at {state.ToString()}");
				}
				isSleeping = false;
				InitializeFeatures();

			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, $"An error has occurred while waking up: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, $"Stacktrace: {e.StackTrace}");
				DateTime time = GetDateTime();
				int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Info, LogSender.SleepMode, $"Next check at {newTime.ToString()}");
				UpdateTitle();
			}
		}

		private static void Defender(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.Defender].WaitOne();
				Helpers.WriteLog(LogType.Info, LogSender.Defender, "Checking attacks...");

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Defender, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Defender].Release();
					return;
				}

				bool isUnderAttack = ogamedService.IsUnderAttack();
				DateTime time = GetDateTime();
				if (isUnderAttack) {
					if ((bool) settings.Defender.Alarm.Active)
						Task.Factory.StartNew(() => Helpers.PlayAlarm());
					UpdateTitle(false, true);
					Helpers.WriteLog(LogType.Warning, LogSender.Defender, "ENEMY ACTIVITY!!!");
					attacks = ogamedService.GetAttacks();
					foreach (AttackerFleet attack in attacks)
						HandleAttack(attack);
				} else {
					Helpers.SetTitle();
					Helpers.WriteLog(LogType.Info, LogSender.Defender, "Your empire is safe");
				}
				int interval = Helpers.CalcRandomInterval((int) settings.Defender.CheckIntervalMin, (int) settings.Defender.CheckIntervalMax);
				if (interval <= 0)
					interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Info, LogSender.Defender, $"Next check at {newTime.ToString()}");
				UpdateTitle();
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.Defender, $"An error has occurred while checking for attacks: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
				DateTime time = GetDateTime();
				int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Info, LogSender.Defender, $"Next check at {newTime.ToString()}");
				UpdateTitle();
			} finally {
				if (!isSleeping)
					xaSem[Feature.Defender].Release();
			}
		}

		private static void BuyOfferOfTheDay(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.Brain].WaitOne();

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				Helpers.WriteLog(LogType.Info, LogSender.Brain, "Buying offer of the day...");
				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}
				var result = ogamedService.BuyOfferOfTheDay();
				if (result)
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Offer of the day succesfully bought.");
				else
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Offer of the day already bought.");
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Brain, $"BuyOfferOfTheDay Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!isSleeping) {
					var time = GetDateTime();
					var interval = Helpers.CalcRandomInterval((int) settings.Brain.BuyOfferOfTheDay.CheckIntervalMin, (int) settings.Brain.BuyOfferOfTheDay.CheckIntervalMax);
					if (interval <= 0)
						interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					var newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("OfferOfTheDayTimer").Change(interval, Timeout.Infinite);
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Next BuyOfferOfTheDay check at {newTime.ToString()}");
					UpdateTitle();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private static void AutoResearch(object state) {
			int fleetId = 0;
			bool stop = false;
			try {
				xaSem[Feature.Brain].WaitOne();
				Helpers.WriteLog(LogType.Info, LogSender.Brain, "Running autoresearch...");

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				researches = ogamedService.GetResearches();
				Planet celestial;
				var parseSucceded = celestials
					.Any(c => c.HasCoords(new(
						(int) settings.Brain.AutoResearch.Target.Galaxy,
						(int) settings.Brain.AutoResearch.Target.System,
						(int) settings.Brain.AutoResearch.Target.Position,
						Celestials.Planet
					))
				);
				if (parseSucceded) {
					celestial = celestials
						.Unique()
						.Single(c => c.HasCoords(new(
							(int) settings.Brain.AutoResearch.Target.Galaxy,
							(int) settings.Brain.AutoResearch.Target.System,
							(int) settings.Brain.AutoResearch.Target.Position,
							Celestials.Planet
							)
						)) as Planet;
				} else {
					celestials = UpdatePlanets(UpdateType.Facilities);
					celestial = celestials
						.Where(c => c.Coordinate.Type == Celestials.Planet)
						.OrderByDescending(c => c.Facilities.ResearchLab)
						.First() as Planet;
				}

				celestial = UpdatePlanet(celestial, UpdateType.Facilities) as Planet;
				if (celestial.Facilities.ResearchLab == 0) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping AutoResearch: Research Lab is missing on target planet.");
					return;
				}
				celestial = UpdatePlanet(celestial, UpdateType.Constructions) as Planet;
				if (celestial.Constructions.ResearchID != 0) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping AutoResearch: there is already a research in progress.");
					return;
				}
				if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping AutoResearch: the Research Lab is upgrading.");
					return;
				}
				celestial = UpdatePlanet(celestial, UpdateType.Facilities) as Planet;
				celestial = UpdatePlanet(celestial, UpdateType.Resources) as Planet;
				slots = UpdateSlots();
				Buildables research = Helpers.GetNextResearchToBuild(celestial as Planet, researches, (bool) settings.Brain.AutoMine.PrioritizeRobotsAndNanitesOnNewPlanets, slots, (int) settings.Brain.AutoResearch.MaxEnergyTechnology, (int) settings.Brain.AutoResearch.MaxLaserTechnology, (int) settings.Brain.AutoResearch.MaxIonTechnology, (int) settings.Brain.AutoResearch.MaxHyperspaceTechnology, (int) settings.Brain.AutoResearch.MaxPlasmaTechnology, (int) settings.Brain.AutoResearch.MaxCombustionDrive, (int) settings.Brain.AutoResearch.MaxImpulseDrive, (int) settings.Brain.AutoResearch.MaxHyperspaceDrive, (int) settings.Brain.AutoResearch.MaxEspionageTechnology, (int) settings.Brain.AutoResearch.MaxComputerTechnology, (int) settings.Brain.AutoResearch.MaxAstrophysics, (int) settings.Brain.AutoResearch.MaxIntergalacticResearchNetwork, (int) settings.Brain.AutoResearch.MaxWeaponsTechnology, (int) settings.Brain.AutoResearch.MaxShieldingTechnology, (int) settings.Brain.AutoResearch.MaxArmourTechnology, (bool) settings.Brain.AutoResearch.OptimizeForStart, (bool) settings.Brain.AutoResearch.EnsureExpoSlots);
				int level = Helpers.GetNextLevel(researches, research);
				if (research != Buildables.Null) {
					celestial = UpdatePlanet(celestial, UpdateType.Resources) as Planet;
					Resources cost = Helpers.CalcPrice(research, level);
					if (celestial.Resources.IsEnoughFor(cost)) {
						var result = ogamedService.BuildCancelable(celestial, research);
						if (result)
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Research {research.ToString()} level {level.ToString()} started on {celestial.ToString()}");
						else
							Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Research {research.ToString()} level {level.ToString()} could not be started on {celestial.ToString()}");
					} else {
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Not enough resources to build: {research.ToString()} level {level.ToString()} on {celestial.ToString()}");
						if ((bool) settings.Brain.AutoResearch.Transports.Active) {
							fleets = UpdateFleets();
							if (!Helpers.IsThereTransportTowardsCelestial(celestial, fleets)) {
								Celestial origin = celestials
									.Unique()
									.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoResearch.Transports.Origin.Galaxy)
									.Where(c => c.Coordinate.System == (int) settings.Brain.AutoResearch.Transports.Origin.System)
									.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoResearch.Transports.Origin.Position)
									.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoResearch.Transports.Origin.Type))
									.SingleOrDefault() ?? new() { ID = 0 };
								fleetId = HandleMinerTransport(origin, celestial, cost);
								if (fleetId == -1) {
									stop = true;
								}
							} else {
								Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
								fleetId = (fleets
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
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Brain, $"AutoResearch Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!isSleeping) {
					if (stop) {
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Stopping feature.");
					} else {
						long interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoResearch.CheckIntervalMin, (int) settings.Brain.AutoResearch.CheckIntervalMax);
						Planet celestial = celestials
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
							fleets = UpdateFleets();
							celestial = UpdatePlanet(celestial, UpdateType.Constructions) as Planet;
							var incomingFleets = Helpers.GetIncomingFleets(celestial, fleets);
							if (celestial.Constructions.ResearchCountdown != 0)
								interval = (celestial.Constructions.ResearchCountdown * 1000) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							else if (fleetId != 0) {
								var fleet = fleets.Single(f => f.ID == fleetId && f.Mission == Missions.Transport);
								interval = (fleet.ArriveIn * 1000) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							} else if (celestial.Constructions.BuildingID == (int) Buildables.ResearchLab)
								interval = (celestial.Constructions.BuildingCountdown * 1000) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
							else if (incomingFleets.Count > 0) {
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
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Next AutoResearch check at {newTime.ToString()}");
						UpdateTitle();
					}
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private static void AutoFarm(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.AutoFarm].WaitOne();

				Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Running autofarm...");

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				if ((bool) settings.AutoFarm.Active) {
					// If not enough slots are free, the farmer cannot run.
					slots = UpdateSlots();

					int freeSlots = slots.Free;
					int slotsToLeaveFree = (int) settings.AutoFarm.SlotsToLeaveFree;

					if (freeSlots > slotsToLeaveFree) {
						try {
							// Prune all reports older than KeepReportFor and all reports of state AttackSent: information no longer actual.
							var newTime = GetDateTime();
							var removeReports = farmTargets.Where(t => t.State == FarmState.AttackSent || (t.Report != null && DateTime.Compare(t.Report.Date.AddMinutes((double) settings.AutoFarm.KeepReportFor), GetDateTime()) < 0)).ToList();
							foreach (var remove in removeReports) {
								var updateReport = remove;
								updateReport.State = FarmState.ProbesPending;
								updateReport.Report = null;
								farmTargets.Remove(remove);
								farmTargets.Add(updateReport);
							}

							// Keep local record of celestials, to be updated by autofarmer itself, to reduce ogamed calls.
							var localCelestials = UpdateCelestials();
							Dictionary<int, long> celestialProbes = new Dictionary<int, long>();
							foreach (var celestial in localCelestials) {
								Celestial tempCelestial = UpdatePlanet(celestial, UpdateType.Fast);
								tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Ships);
								celestialProbes.Add(tempCelestial.ID, tempCelestial.Ships.EspionageProbe);
							}

							// Keep track of number of targets probed.
							int numProbed = 0;

							/// Galaxy scanning + target probing.
							Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Detecting farm targets...");
							foreach (var range in settings.AutoFarm.ScanRange) {
								if (Helpers.IsSettingSet(settings.AutoFarm.TargetsProbedBeforeAttack) && settings.AutoFarm.TargetsProbedBeforeAttack != 0 && numProbed >= (int) settings.AutoFarm.TargetsProbedBeforeAttack) break;

								int galaxy		= (int) range.Galaxy;
								int startSystem = (int) range.StartSystem;
								int endSystem	= (int) range.EndSystem;

								// Loop from start to end system.
								for (var system = startSystem; system <= endSystem; system++) {
									if (Helpers.IsSettingSet(settings.AutoFarm.TargetsProbedBeforeAttack) && settings.AutoFarm.TargetsProbedBeforeAttack != 0 && numProbed >= (int) settings.AutoFarm.TargetsProbedBeforeAttack) break;

									// Check excluded system.
									bool excludeSystem = false;
									foreach (var exclude in settings.AutoFarm.Exclude) {
										bool hasPosition = false;
										foreach (var value in exclude.Keys) if (value == "Position") hasPosition = true;
										if ((int) exclude.Galaxy == galaxy && (int) exclude.System == system && !hasPosition) {
											Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Skipping system {system.ToString()}: system in exclude list.");
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

									// Add each planet that has inactive status to farmTargets.
									foreach (Celestial planet in scannedTargets) {
										// Check if target is below set minimum rank.
										if (Helpers.IsSettingSet(settings.AutoFarm.MinimumPlayerRank) && settings.AutoFarm.MinimumPlayerRank != 0) {
											int rank = 1;
											if (planet.Coordinate.Type == Celestials.Planet) {
												rank = (planet as Planet).Player.Rank;
											}
											else {
												if (scannedTargets.Any(t => t.HasCoords(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet)))) {
													rank = (scannedTargets.Single(t => t.HasCoords(new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Planet))) as Planet).Player.Rank;
												}
											}
											if ((int) settings.AutoFarm.MinimumPlayerRank < rank) {
												continue;
											}
										}

										if (Helpers.IsSettingSet(settings.AutoFarm.TargetsProbedBeforeAttack) &&
											settings.AutoFarm.TargetsProbedBeforeAttack != 0 && numProbed >= (int) settings.AutoFarm.TargetsProbedBeforeAttack) {
											Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Maximum number of targets to probe reached, proceeding to attack.");
											break;
										}

										// Check excluded planet.
										bool excludePlanet = false;
										foreach (var exclude in settings.AutoFarm.Exclude) {
											bool hasPosition = false;
											foreach (var value in exclude.Keys) if (value == "Position") hasPosition = true;
											if ((int) exclude.Galaxy == galaxy && (int) exclude.System == system && hasPosition && (int) exclude.Position == planet.Coordinate.Position) {
												Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Skipping {planet.ToString()}: celestial in exclude list.");
												excludePlanet = true;
												break;
											}
										}
										if (excludePlanet == true)
											continue;

										// Check if planet with coordinates exists already in farmTargets list.
										var exists = farmTargets.Where(t => t != null && t.Celestial.HasCoords(planet.Coordinate));
										if (exists.Count() > 1) {
											// BUG: Same coordinates should never appear multiple times in farmTargets. The list should only contain unique coordinates.
											Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, "BUG: Same coordinates appeared multiple times within farmTargets!");
											return;
										}

										FarmTarget target = new(planet, FarmState.ProbesPending);

										if (!exists.Any()) {
											// Does not exist, add to farmTargets list, set state to probes pending.
											farmTargets.Add(target);
										} else {
											// Already exists, update farmTargets list with updated planet.
											var farmTarget = exists.First();
											target = farmTarget;
											target.Celestial = planet;

											if (farmTarget.State == FarmState.Idle)
												target.State = FarmState.ProbesPending;

											farmTargets.Remove(farmTarget);
											farmTargets.Add(target);

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
										List<Celestial> tempCelestials = (settings.AutoFarm.Origin.Length > 0) ? Helpers.ParseCelestialsList(settings.AutoFarm.Origin, celestials) : celestials;
										List<Celestial> closestCelestials = tempCelestials
											.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
											.OrderBy(c => Helpers.CalcDistance(c.Coordinate, target.Celestial.Coordinate, serverData)).ToList();

										foreach (var closest in closestCelestials) {
											int neededProbes = (int) settings.AutoFarm.NumProbes;
											if (target.State == FarmState.ProbesRequired)
												neededProbes *= 3;
											if (target.State == FarmState.FailedProbesRequired)
												neededProbes *= 9;

											// If local record indicate not enough espionage probes are available, update record to make sure this is correct.
											if (celestialProbes[closest.ID] < neededProbes) {
												var tempCelestial = UpdatePlanet(closest, UpdateType.Ships);
												celestialProbes.Remove(closest.ID);
												celestialProbes.Add(closest.ID, tempCelestial.Ships.EspionageProbe);
											}

											// Check if probes are available: If not, wait for them.
											if (celestialProbes[closest.ID] < neededProbes) {
												// Wait for probes to come back to current celestial. If no on-route, continue to next iteration.
												fleets = UpdateFleets();
												var espionageMissions = Helpers.GetMissionsInProgress(closest.Coordinate, Missions.Spy, fleets);
												if (espionageMissions.Any()) {
													var returningProbes = espionageMissions.Sum(f => f.Ships.EspionageProbe);
													if (celestialProbes[closest.ID] + returningProbes >= neededProbes) {
														var returningFleets = espionageMissions.OrderBy(f => f.BackIn).ToArray();
														long probesCount = 0;
														for (int i = 0; i <= returningFleets.Length; i++) {
															probesCount += returningFleets[i].Ships.EspionageProbe;
															if (probesCount >= neededProbes) {
																int interval = (int) ((1000 * returningFleets[i].BackIn) + Helpers.CalcRandomInterval(IntervalType.LessThanASecond));
																Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Waiting for probes to return...");
																Thread.Sleep(interval);
																freeSlots++;
																break;
															}
														}
													}
												}
												else {
													Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Cannot spy {target.Celestial.Coordinate.ToString()} from {closest.Coordinate.ToString()}, insufficient probes ({celestialProbes[closest.ID]}/{neededProbes}).");
													continue;
												}												
											}

											if (freeSlots <= slotsToLeaveFree) {
												slots = UpdateSlots();
												freeSlots = slots.Free;
											}

											while (freeSlots <= slotsToLeaveFree) {
												// No slots available, wait for first fleet of any mission type to return.
												fleets = UpdateFleets();
												if (fleets.Any()) {
													int interval = (int) ((1000 * fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + Helpers.CalcRandomInterval(IntervalType.LessThanASecond));
													Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Out of fleet slots. Waiting for fleet to return...");
													Thread.Sleep(interval);
													slots = UpdateSlots();
													freeSlots = slots.Free;
												} else {
													Helpers.WriteLog(LogType.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
													return;
												}
											}

											if (Helpers.GetMissionsInProgress(closest.Coordinate, Missions.Spy, fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate))) {
												Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Probes already on route towards {target.ToString()}.");
												break;
											}
											if (Helpers.GetMissionsInProgress(closest.Coordinate, Missions.Attack, fleets).Any(f => f.Destination.IsSame(target.Celestial.Coordinate) && f.ReturnFlight == false)) {
												Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Attack already on route towards {target.ToString()}.");
												break;
											}

											// If local record indicate not enough espionage probes are available, update record to make sure this is correct.
											if (celestialProbes[closest.ID] < neededProbes) {
												var tempCelestial = UpdatePlanet(closest, UpdateType.Ships);
												celestialProbes.Remove(closest.ID);
												celestialProbes.Add(closest.ID, tempCelestial.Ships.EspionageProbe);
											}

											if (celestialProbes[closest.ID] >= neededProbes) {
												Ships ships = new();
												ships.Add(Buildables.EspionageProbe, neededProbes);

												Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Spying {target.ToString()} from {closest.ToString()} with {neededProbes} probes.");

												slots = UpdateSlots();
												var fleetId = SendFleet(closest, ships, target.Celestial.Coordinate, Missions.Spy, Speeds.HundredPercent);
												if (fleetId > 0) {
													freeSlots--;
													numProbed++;
													celestialProbes[closest.ID] -= neededProbes;

													if (target.State == FarmState.ProbesRequired || target.State == FarmState.FailedProbesRequired)
														break;

													farmTargets.Remove(target);
													target.State = FarmState.ProbesSent;
													farmTargets.Add(target);

													break;
												} else if (fleetId == -1) {
													stop = true;
													return;
												}
												else {
													continue;
												}
											} else {
												Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Insufficient probes ({celestialProbes[closest.ID]}/{neededProbes}).");
												if (Helpers.IsSettingSet(settings.AutoFarm.BuildProbes) && settings.AutoFarm.BuildProbes == true) {
													var buildProbes = neededProbes - celestialProbes[closest.ID];
													var cost = Helpers.CalcPrice(Buildables.EspionageProbe, (int) buildProbes);
													var tempCelestial = UpdatePlanet(closest, UpdateType.Resources);
													if (tempCelestial.Resources.IsEnoughFor(cost)) {
														Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {buildProbes}x{Buildables.EspionageProbe.ToString()}");
													} else {
														var buildableProbes = Helpers.CalcMaxBuildableNumber(Buildables.EspionageProbe, tempCelestial.Resources);
														Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Not enough resources to build {buildProbes}x{Buildables.EspionageProbe.ToString()}. {buildableProbes} will be built instead.");
														buildProbes = buildableProbes;
													}

													var result = ogamedService.BuildShips(tempCelestial, Buildables.EspionageProbe, buildProbes);
													if (result) {
														tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Facilities);
														int interval = (int) (Helpers.CalcProductionTime(Buildables.EspionageProbe, (int) buildProbes, serverData, tempCelestial.Facilities) + Helpers.CalcRandomInterval(IntervalType.AFewSeconds)) * 1000;
														Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Production succesfully started. Waiting for build order to finish...");
														Thread.Sleep(interval);
													}
													else {
														Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, "Unable to start ship production.");
													}
												}
												break;
											}
										}
									}
								}
							}
						} catch (Exception e) {
							Helpers.WriteLog(LogType.Debug, LogSender.AutoFarm, $"Exception: {e.Message}");
							Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
							Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, "Unable to parse scan range");
						}

						// Wait for all espionage fleets to return.
						fleets = UpdateFleets();
						Fleet firstReturning = Helpers.GetFirstReturningEspionage(fleets);
						if (firstReturning != null) {
							int interval = (int) ((1000 * firstReturning.BackIn) + Helpers.CalcRandomInterval(IntervalType.AFewSeconds));
							Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Waiting for probes to return...");
							Thread.Sleep(interval);
						}

						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Processing espionage reports of found inactives...");

						/// Process reports.
						AutoFarmProcessReports();

						/// Send attacks.
						List<FarmTarget> attackTargets;
						if (settings.AutoFarm.PreferedResource == "Metal")
							attackTargets = farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(userInfo.Class).Metal).ToList();
						else if (settings.AutoFarm.PreferedResource == "Crystal")
							attackTargets = farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(userInfo.Class).Crystal).ToList();
						else if (settings.AutoFarm.PreferedResource == "Deuterium")
							attackTargets = farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(userInfo.Class).Deuterium).ToList();
						else
							attackTargets = farmTargets.Where(t => t.State == FarmState.AttackPending).OrderByDescending(t => t.Report.Loot(userInfo.Class).TotalResources).ToList();

						if (attackTargets.Count() > 0) {
							Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Attacking suitable farm targets...");
						} else {
							Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "No suitable targets found.");
							return;
						}

						Buildables cargoShip = Buildables.LargeCargo;
						if (!Enum.TryParse<Buildables>((string) settings.AutoFarm.CargoType, true, out cargoShip)) {
							Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, "Unable to parse cargoShip. Falling back to default LargeCargo");
							cargoShip = Buildables.LargeCargo;
						}
						if (cargoShip == Buildables.Null) {
							Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip is Null");
							return;
						}
						if (cargoShip == Buildables.EspionageProbe && serverData.ProbeCargo == 0) {
							Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, "Unable to send attack: cargoShip set to EspionageProbe, but this universe does not have probe cargo.");
							return;
						}

						researches = UpdateResearches();
						celestials = UpdateCelestials();
						int attackTargetsCount = 0;
						foreach (FarmTarget target in attackTargets) {
							attackTargetsCount++;
							Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Attacking target {attackTargetsCount}/{attackTargets.Count} at {target.Celestial.Coordinate.ToString()} for {target.Report.Loot(userInfo.Class).TransportableResources}.");
							var loot = target.Report.Loot(userInfo.Class);
							var numCargo = Helpers.CalcShipNumberForPayload(loot, cargoShip, researches.HyperspaceTechnology, userInfo.Class, serverData.ProbeCargo);
							var attackingShips = new Ships().Add(cargoShip, numCargo);

							List<Celestial> tempCelestials = (settings.AutoFarm.Origin.Length > 0) ? Helpers.ParseCelestialsList(settings.AutoFarm.Origin, celestials) : celestials;
							List<Celestial> closestCelestials = tempCelestials
								.OrderByDescending(planet => planet.Coordinate.Type == Celestials.Moon)
								.OrderBy(c => Helpers.CalcDistance(c.Coordinate, target.Celestial.Coordinate, serverData)).ToList();

							Celestial fromCelestial = null;
							foreach (var c in closestCelestials) {
								var tempCelestial = UpdatePlanet(c, UpdateType.Ships);
								tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Resources);
								if (tempCelestial.Ships != null && tempCelestial.Ships.GetAmount(cargoShip) >= (numCargo + settings.AutoFarm.MinCargosToKeep)) {
									// TODO Future: If fleet composition is changed, update ships passed to CalcFlightTime.
									decimal lootFuelRatio = (decimal) 0.5;
									if (Helpers.IsSettingSet(settings.AutoFarm.MinLootFuelRatio) && settings.AutoFarm.MinLootFuelRatio != 0) {
										lootFuelRatio = (decimal) settings.AutoFarm.MinLootFuelRatio;
									}
									decimal speed;
									if (Helpers.IsSettingSet(settings.AutoFarm.FleetSpeed) && settings.AutoFarm.FleetSpeed > 0) {
										speed = (int) settings.AutoFarm.FleetSpeed / 10;
										if (!Helpers.GetValidSpeedsForClass(userInfo.Class).Any(s => s == speed)) {
											Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
											speed = Speeds.HundredPercent;
										}
									} else {
										speed = Speeds.HundredPercent;
										//speed = Helpers.CalcOptimalFarmSpeed(ogamedService, tempCelestial, target.Celestial.Coordinate, attackingShips, target.Report.Loot(userInfo.Class), lootFuelRatio, researches, serverData, userInfo.Class);
									}
									var prediction = ogamedService.PredictFleet(tempCelestial, attackingShips, target.Celestial.Coordinate, Missions.Attack, speed);
									if (
										(
											!Helpers.IsSettingSet(settings.AutoFarm.MaxFlightTime) ||
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
								Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"No origin celestial available near destination {target.Celestial.ToString()} with enough cargo ships.");
								// TODO Future: If prefered cargo ship is not available or not sufficient capacity, combine with other cargo type.
								foreach (var closest in closestCelestials) {
									Celestial tempCelestial = closest;
									tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Ships);
									tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Resources);
									// TODO Future: If fleet composition is changed, update ships passed to CalcFlightTime.
									decimal lootFuelRatio = (decimal) 0.5;
									if (Helpers.IsSettingSet(settings.AutoFarm.MinLootFuelRatio) && settings.AutoFarm.MinLootFuelRatio != 0) {
										lootFuelRatio = (decimal) settings.AutoFarm.MinLootFuelRatio;
									}
									decimal speed;
									if (Helpers.IsSettingSet(settings.AutoFarm.FleetSpeed) && settings.AutoFarm.FleetSpeed > 0) {
										speed = (int) settings.AutoFarm.FleetSpeed / 10;
										if (!Helpers.GetValidSpeedsForClass(userInfo.Class).Any(s => s == speed)) {
											Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
											speed = Speeds.HundredPercent;
										}
									}
									else {
										speed = Speeds.HundredPercent;
										//speed = Helpers.CalcOptimalFarmSpeed(ogamedService, tempCelestial, target.Celestial.Coordinate, attackingShips, target.Report.Loot(userInfo.Class), lootFuelRatio, researches, serverData, userInfo.Class);
									}
									var prediction = ogamedService.PredictFleet(tempCelestial, attackingShips, target.Celestial.Coordinate, Missions.Attack, speed);
									if (
										tempCelestial.Ships.GetAmount(cargoShip) < numCargo + (long) settings.AutoFarm.MinCargosToKeep &&
										tempCelestial.Resources.Deuterium >= prediction.Fuel &&
										(
											!Helpers.IsSettingSet(settings.AutoFarm.MaxFlightTime) ||
											(long) settings.AutoFarm.MaxFlightTime == 0 ||
											prediction.Time <= (long) settings.AutoFarm.MaxFlightTime
										)
									) {
										if (Helpers.IsSettingSet(settings.AutoFarm.BuildCargos) && settings.AutoFarm.BuildCargos == true) {
											var neededCargos = numCargo + (long) settings.AutoFarm.MinCargosToKeep - tempCelestial.Ships.GetAmount(cargoShip);
											var cost = Helpers.CalcPrice(cargoShip, (int) neededCargos);
											if (tempCelestial.Resources.IsEnoughFor(cost)) {
												Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Building {neededCargos}x{cargoShip.ToString()}");
											} else {
												var buildableCargos = Helpers.CalcMaxBuildableNumber(cargoShip, tempCelestial.Resources);
												Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{cargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
												neededCargos = buildableCargos;
											}

											var result = ogamedService.BuildShips(tempCelestial, cargoShip, neededCargos);
											if (result) {
												tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Facilities);
												int interval = (int) (Helpers.CalcProductionTime(cargoShip, (int) neededCargos, serverData, tempCelestial.Facilities) + Helpers.CalcRandomInterval(IntervalType.AFewSeconds)) * 1000;
												Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Production succesfully started. Waiting for build order to finish...");
												Thread.Sleep(interval);
											}
											else {
												Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, "Unable to start ship production.");
											}
										}

										if (tempCelestial.Ships.GetAmount(cargoShip) - (long) settings.AutoFarm.MinCargosToKeep < (long) settings.AutoFarm.MinCargosToSend) {
											Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Insufficient {cargoShip.ToString()} on {tempCelestial.Coordinate}, require {numCargo + (long) settings.AutoFarm.MinCargosToKeep} {cargoShip.ToString()}.");
											continue;
										}

										numCargo = tempCelestial.Ships.GetAmount(cargoShip) - (long) settings.AutoFarm.MinCargosToKeep;
										fromCelestial = tempCelestial;
										break;
									}
								}
							}

							if (fromCelestial == null) {
								Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. No suitable origin celestial available near the destination.");
								continue;
							}

							// Only execute update slots if our local copy indicates we have run out.
							if (freeSlots <= slotsToLeaveFree) {
								slots = UpdateSlots();
								freeSlots = slots.Free;
							}

							while (freeSlots <= slotsToLeaveFree) {
								fleets = UpdateFleets();
								// No slots free, wait for first fleet to come back.
								if (fleets.Any()) {
									int interval = (int) ((1000 * fleets.OrderBy(fleet => fleet.BackIn).First().BackIn) + Helpers.CalcRandomInterval(IntervalType.AFewSeconds));
									if (Helpers.IsSettingSet(settings.AutoFarm.MaxWaitTime) && (int) settings.AutoFarm.MaxWaitTime != 0 && interval > (int) settings.AutoFarm.MaxWaitTime) {
										Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Out of fleet slots. Time to wait greater than set {(int) settings.AutoFarm.MaxWaitTime} seconds. Stopping autofarm.");
										return;										
									} else {
										Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, "Out of fleet slots. Waiting for first fleet to return...");
										Thread.Sleep(interval);
										slots = UpdateSlots();
										freeSlots = slots.Free;
									}
								} else {
									Helpers.WriteLog(LogType.Error, LogSender.AutoFarm, "Error: No fleet slots available and no fleets returning!");
									return;
								}
							}

							if (slots.Free > slotsToLeaveFree) {
								Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Attacking {target.ToString()} from {fromCelestial} with {numCargo} {cargoShip.ToString()}.");
								Ships ships = new();
								ships.Add(cargoShip, numCargo);

								decimal lootFuelRatio = (decimal) 0.5;
								if (Helpers.IsSettingSet(settings.AutoFarm.MinLootFuelRatio) && settings.AutoFarm.MinLootFuelRatio != 0) {
									lootFuelRatio = (decimal) settings.AutoFarm.MinLootFuelRatio;
								}

								decimal speed;
								if (Helpers.IsSettingSet(settings.AutoFarm.FleetSpeed) && settings.AutoFarm.FleetSpeed > 0) {
									speed = (int) settings.AutoFarm.FleetSpeed / 10;
									if (!Helpers.GetValidSpeedsForClass(userInfo.Class).Any(s => s == speed)) {
										Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Invalid FleetSpeed, falling back to default 100%.");
										speed = Speeds.HundredPercent;
									}
								} else {
									speed = Speeds.HundredPercent;
								}
								var prediction = ogamedService.PredictFleet(fromCelestial, ships, target.Celestial.Coordinate, Missions.Attack, speed);
								/*
								var optimalSpeed = Helpers.CalcOptimalFarmSpeed(fromCelestial.Coordinate, target.Celestial.Coordinate, ships, target.Report.Loot(userInfo.Class), lootFuelRatio, researches, serverData, userInfo.Class);
								Helpers.WriteLog(LogType.Debug, LogSender.AutoFarm, $"Calculated optimal speed: {(int) Math.Round(optimalSpeed * 10, 0)}%");
								var fleetPrediction = Helpers.CalcFleetPrediction(fromCelestial.Coordinate, target.Celestial.Coordinate, ships, Missions.Attack, optimalSpeed, researches, serverData, userInfo.Class);
								Helpers.WriteLog(LogType.Debug, LogSender.AutoFarm, $"Calculated flight time: {fleetPrediction.Time} s");
								Helpers.WriteLog(LogType.Debug, LogSender.AutoFarm, $"Calculated flight fuel: {fleetPrediction.Fuel}");
								var fleetId = SendFleet(fromCelestial, ships, target.Celestial.Coordinate, Missions.Attack, optimalSpeed);
								*/
								var fleetId = SendFleet(fromCelestial, ships, target.Celestial.Coordinate, Missions.Attack, speed);

								if (fleetId > 0) {
									freeSlots--;
								}
								else if (fleetId == -1) {
									stop = true;
									return;
								}

								farmTargets.Remove(target);
								target.State = FarmState.AttackSent;
								farmTargets.Add(target);
							} else {
								Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Unable to attack {target.Celestial.Coordinate}. {slots.Free} free, {slotsToLeaveFree} required.");
								return;
							}
						}
					} else {
						Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to start auto farm, no slots available");
						return;
					}
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.AutoFarm, $"AutoFarm Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.AutoFarm, $"Stacktrace: {e.StackTrace}");
			} finally {
				Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Attacked targets: {farmTargets.Where(t => t.State == FarmState.AttackSent).Count()}");
				if (!isSleeping) {
					if (stop) {
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Stopping feature.");
					}
					else {
						var time = GetDateTime();
						var interval = Helpers.CalcRandomInterval((int) settings.AutoFarm.CheckIntervalMin, (int) settings.AutoFarm.CheckIntervalMax);
						if (interval <= 0)
							interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("AutoFarmTimer").Change(interval, Timeout.Infinite);
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Next autofarm check at {newTime.ToString()}");
						UpdateTitle();
					}
					
					xaSem[Feature.AutoFarm].Release();
				}
			}
		}

		/// <summary>
		/// Checks all received espionage reports and updates farmTargets to reflect latest data retrieved from reports.
		/// </summary>
		private static void AutoFarmProcessReports() {
			// TODO Future: Read espionage reports in separate thread (concurently with probing itself).
			// TODO Future: Check if probes were destroyed, blacklist target if so to avoid additional kills.
			List<EspionageReportSummary> summaryReports = ogamedService.GetEspionageReports();
			foreach (var summary in summaryReports) {
				if (summary.Type == EspionageReportType.Action)
					continue;

				var report = ogamedService.GetEspionageReport(summary.ID);
				if (DateTime.Compare(report.Date.AddMinutes((double) settings.AutoFarm.KeepReportFor), GetDateTime()) < 0) {
					ogamedService.DeleteReport(report.ID);
					continue;
				}

				var matchingTarget = farmTargets.Where(t => t.HasCoords(report.Coordinate));
				if (matchingTarget.Count() == 0) {
					// Report received of planet not in farmTargets. If inactive: add, otherwise: ignore.
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

				if (settings.AutoFarm.PreferedResource == "Metal" && report.Loot(userInfo.Class).Metal > settings.AutoFarm.MinimumResources
					|| settings.AutoFarm.PreferedResource == "Crystal" && report.Loot(userInfo.Class).Crystal > settings.AutoFarm.MinimumResources
					|| settings.AutoFarm.PreferedResource == "Deuterium" && report.Loot(userInfo.Class).Deuterium > settings.AutoFarm.MinimumResources
					|| (settings.AutoFarm.PreferedResource == "" && report.Loot(userInfo.Class).TotalResources > settings.AutoFarm.MinimumResources)) {
					if (!report.HasFleetInformation || !report.HasDefensesInformation) {
						if (target.State == FarmState.ProbesRequired)
							newFarmTarget.State = FarmState.FailedProbesRequired;
						else if (target.State == FarmState.FailedProbesRequired)
							newFarmTarget.State = FarmState.NotSuitable;
						else
							newFarmTarget.State = FarmState.ProbesRequired;

						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Need more probes on {report.Coordinate}. Loot: {report.Loot(userInfo.Class)}");
					} else if (report.IsDefenceless()) {
						newFarmTarget.State = FarmState.AttackPending;
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Attack pending on {report.Coordinate}. Loot: {report.Loot(userInfo.Class)}");
					} else {
						newFarmTarget.State = FarmState.NotSuitable;
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - defences present.");
					}
				} else {
					newFarmTarget.State = FarmState.NotSuitable;
					Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Target {report.Coordinate} not suitable - insufficient loot ({report.Loot(userInfo.Class)})");
				}

				farmTargets.Remove(target);
				farmTargets.Add(newFarmTarget);
			}

			ogamedService.DeleteAllEspionageReports();
		}

		private static void AutoMine(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.Brain].WaitOne();
				Helpers.WriteLog(LogType.Info, LogSender.Brain, "Running automine...");

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

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
					MaxDaysOfInvestmentReturn = (int) settings.Brain.AutoMine.MaxDaysOfInvestmentReturn,
					DepositHours = (int) settings.Brain.AutoMine.DepositHours,
					BuildDepositIfFull = (bool) settings.Brain.AutoMine.BuildDepositIfFull,
					DeutToLeaveOnMoons = (int) settings.Brain.AutoMine.DeutToLeaveOnMoons
				};

				List<Celestial> celestialsToExclude = Helpers.ParseCelestialsList(settings.Brain.AutoMine.Exclude, celestials);
				List<Celestial> celestialsToMine = new();
				if (state == null)
					celestialsToMine = celestials;
				else
					celestialsToMine.Add(state as Celestial);

				foreach (Celestial celestial in (bool) settings.Brain.AutoMine.RandomOrder ? celestialsToMine.Shuffle().ToList() : celestialsToMine.OrderBy(c => c.Fields.Built).ToList()) {
					if (celestialsToExclude.Has(celestial)) {
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
						continue;
					}

					AutoMineCelestial(celestial, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Brain, $"AutoMine Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!isSleeping) {
					UpdateTitle();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private static void AutoMineCelestial(Celestial celestial, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			int fleetId = 0;
			Buildables buildable = Buildables.Null;
			int level = 0;
			bool started = false;
			bool stop = false;
			try {
				Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Running AutoMine on {celestial.ToString()}");
				celestial = UpdatePlanet(celestial, UpdateType.Fast);
				if (celestial.Fields.Free == 0) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: not enough fields available.");
					return;
				}

				celestial = UpdatePlanet(celestial, UpdateType.Constructions);
				if (celestial.Constructions.BuildingID != 0) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: there is already a building in production.");
					return;
				}

				celestial = UpdatePlanet(celestial, UpdateType.Resources);
				celestial = UpdatePlanet(celestial, UpdateType.Facilities);
				celestial = UpdatePlanet(celestial, UpdateType.Productions);

				if (celestial is Planet) {
					celestial = UpdatePlanet(celestial, UpdateType.Buildings);
					celestial = UpdatePlanet(celestial, UpdateType.ResourcesProduction);

					buildable = Helpers.GetNextBuildingToBuild(celestial as Planet, researches, maxBuildings, maxFacilities, userInfo.Class, staff, serverData, autoMinerSettings);
					level = Helpers.GetNextLevel(celestial as Planet, buildable, userInfo.Class == CharacterClass.Collector, staff.Engineer, staff.IsFull);
				} else {
					buildable = Helpers.GetNextLunarFacilityToBuild(celestial as Moon, researches, maxLunarFacilities);
					level = Helpers.GetNextLevel(celestial as Moon, buildable, userInfo.Class == CharacterClass.Collector, staff.Engineer, staff.IsFull);
				}

				if (buildable != Buildables.Null && level > 0) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Best building for {celestial.ToString()}: {buildable.ToString()}");
					if (buildable == Buildables.MetalMine || buildable == Buildables.CrystalMine || buildable == Buildables.DeuteriumSynthesizer) {
						float daysOfReturn = Helpers.CalcDaysOfInvestmentReturn(celestial as Planet, buildable, researches, serverData.Speed, 1, userInfo.Class, staff.Geologist, staff.IsFull);
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Investment return: {Math.Round(daysOfReturn, 2).ToString()} days.");
					}

					Resources xCostBuildable = Helpers.CalcPrice(buildable, level);
					if (celestial is Moon) xCostBuildable.Deuterium += (long) autoMinerSettings.DeutToLeaveOnMoons;
					
					if (celestial.Resources.IsEnoughFor(xCostBuildable)) {
						bool result = false;
						if (buildable == Buildables.SolarSatellite) {
							if (celestial.Productions.Count == 0) {
								Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Building {level.ToString()} x {buildable.ToString()} on {celestial.ToString()}");
								result = ogamedService.BuildShips(celestial, buildable, level);
							} else {
								Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: There is already a production ongoing.");
							}
						} else {
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Building {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
							result = ogamedService.BuildConstruction(celestial, buildable);
						}

						if (result) {
							if (buildable == Buildables.SolarSatellite) {
								celestial = UpdatePlanet(celestial, UpdateType.Productions);
								if (celestial.Productions.First().ID == (int) buildable) {
									started = true;
									Helpers.WriteLog(LogType.Info, LogSender.Brain, $"{celestial.Productions.First().Nbr.ToString()}x {buildable.ToString()} succesfully started.");
								} else {
									celestial = UpdatePlanet(celestial, UpdateType.Resources);
									if (celestial.Resources.Energy >= 0) {
										started = true;
										Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"{level.ToString()}x {buildable.ToString()} succesfully built");
									} else
										Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Unable to start {level.ToString()}x {buildable.ToString()} construction: an unknow error has occurred");
								}
							} else {
								celestial = UpdatePlanet(celestial, UpdateType.Constructions);
								if (celestial.Constructions.BuildingID == (int) buildable) {
									started = true;
									Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building succesfully started.");
								} else {
									celestial = UpdatePlanet(celestial, UpdateType.Buildings);
									celestial = UpdatePlanet(celestial, UpdateType.Facilities);
									if (celestial.GetLevel(buildable) != level)
										Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to start building construction: an unknown error has occurred");
									else {
										started = true;
										Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building succesfully started.");
									}
								}
							}
						} else if (buildable != Buildables.SolarSatellite)
							Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to start building construction: a network error has occurred");
					} else {
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Not enough resources to build: {buildable.ToString()} level {level.ToString()} on {celestial.ToString()}");
						if ((bool) settings.Brain.AutoMine.Transports.Active) {
							fleets = UpdateFleets();
							if (!Helpers.IsThereTransportTowardsCelestial(celestial, fleets)) {
								Celestial origin = celestials
										.Unique()
										.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
										.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
										.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
										.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
										.SingleOrDefault() ?? new() { ID = 0 };
								fleetId = HandleMinerTransport(origin, celestial, xCostBuildable);
								if (fleetId == -1) {
									stop = true;
									return;
								}
							} else {
								Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping transport: there is already a transport incoming in {celestial.ToString()}");
							}
						}
					}
				} else {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: nothing to build.");
					if (
						(
							celestial.Coordinate.Type == Celestials.Planet && (
								(celestial as Planet).HasMines(maxBuildings) ||
								Helpers.CalcNextDaysOfInvestmentReturn(celestial as Planet, researches, serverData.Speed, 1, userInfo.Class, staff.Geologist, staff.IsFull) > autoMinerSettings.MaxDaysOfInvestmentReturn
							)
						) ||
						(
							celestial.Coordinate.Type == Celestials.Moon &&
							(celestial as Moon).HasLunarFacilities(maxLunarFacilities)
						)
					) {
						stop = true;
						string buildings = (celestial.Coordinate.Type == Celestials.Planet ? "mines" : "facilities"); 
						//Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}: {buildings} are at set level.");
					}
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Brain, $"AutoMineCelestial Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				var time = GetDateTime();
				string autoMineTimer = $"AutoMineTimer-{celestial.ID.ToString()}";
				DateTime newTime;
				if (stop) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}.");
					if (timers.TryGetValue($"AutoMineTimer-{celestial.ID.ToString()}", out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);
				} else {
					long interval = CalcAutoMineTimer(celestial, buildable, level, started, maxBuildings, maxFacilities, maxLunarFacilities, autoMinerSettings);
					if (interval == long.MaxValue || interval == long.MinValue)
						interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);

					if (timers.TryGetValue(autoMineTimer, out Timer value))
						value.Dispose();
					timers.Remove(autoMineTimer);

					newTime = time.AddMilliseconds(interval);
					timers.Add(autoMineTimer, new Timer(AutoMine, celestial, interval, Timeout.Infinite));
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Next AutoMine check for {celestial.ToString()} at {newTime.ToString()}");
				}
			}
		}

		private static long CalcAutoMineTimer(Celestial celestial, Buildables buildable, int level, bool started, Buildings maxBuildings, Facilities maxFacilities, Facilities maxLunarFacilities, AutoMinerSettings autoMinerSettings) {
			long interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoMine.CheckIntervalMin, (int) settings.Brain.AutoMine.CheckIntervalMax);
			try {
				if (celestial.Fields.Free == 0) {
					interval = long.MaxValue;
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Stopping AutoMine check for {celestial.ToString()}: not enough fields available.");
				}

				celestial = UpdatePlanet(celestial, UpdateType.Constructions);
				if (started) {
					if (buildable == Buildables.SolarSatellite) {
						celestial = UpdatePlanet(celestial, UpdateType.Productions);
						celestial = UpdatePlanet(celestial, UpdateType.Facilities);
						interval = Helpers.CalcProductionTime((Buildables) celestial.Productions.First().ID, celestial.Productions.First().Nbr, serverData, celestial.Facilities) * 1000;
					}						
					else {
						if (celestial.HasConstruction())
							interval = (celestial.Constructions.BuildingCountdown * 1000);
						else
							interval = 0;
					}
				}
				else if (celestial.HasConstruction()) {
					interval = (celestial.Constructions.BuildingCountdown * 1000);
				} else {
					celestial = UpdatePlanet(celestial, UpdateType.Buildings);
					celestial = UpdatePlanet(celestial, UpdateType.Facilities);

					if (buildable != Buildables.Null) {
						var price = Helpers.CalcPrice(buildable, level);
						var productionTime = long.MaxValue;
						var transportTime = long.MaxValue;
						var returningExpoTime = long.MaxValue;
						var transportOriginTime = long.MaxValue;
						var returningExpoOriginTime = long.MaxValue;

						celestial = UpdatePlanet(celestial, UpdateType.ResourcesProduction);
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
								Helpers.WriteLog(LogType.Debug, LogSender.Brain, $"The required resources will be produced by {now.AddMilliseconds(productionTime).ToString()}");
							}
						}

						fleets = UpdateFleets();
						var incomingFleets = Helpers.GetIncomingFleetsWithResources(celestial, fleets);
						if (incomingFleets.Any()) {
							var fleet = incomingFleets.First();
							transportTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
							//Helpers.WriteLog(LogType.Debug, LogSender.Brain, $"Next fleet with resources arriving by {now.AddMilliseconds(transportTime).ToString()}");
						}

						var returningExpo = Helpers.GetFirstReturningExpedition(celestial.Coordinate, fleets);
						if (returningExpo != null) {
							returningExpoTime = (long) (returningExpo.BackIn * 1000) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
							//Helpers.WriteLog(LogType.Debug, LogSender.Brain, $"Next expedition returning by {now.AddMilliseconds(returningExpoTime).ToString()}");
						}

						if ((bool) settings.Brain.AutoMine.Transports.Active) {
							Celestial origin = celestials
									.Unique()
									.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
									.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
									.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
									.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
									.SingleOrDefault() ?? new() { ID = 0 };
							var returningExpoOrigin = Helpers.GetFirstReturningExpedition(origin.Coordinate, fleets);
							if (returningExpoOrigin != null) {
								returningExpoOriginTime = (long) (returningExpoOrigin.BackIn * 1000) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
								//Helpers.WriteLog(LogType.Debug, LogSender.Brain, $"Next expedition returning in transport origin celestial by {now.AddMilliseconds(returningExpoOriginTime).ToString()}");
							}

							var incomingOriginFleets = Helpers.GetIncomingFleetsWithResources(origin, fleets);
							if (incomingOriginFleets.Any()) {
								var fleet = incomingOriginFleets.First();
								transportOriginTime = ((fleet.Mission == Missions.Transport || fleet.Mission == Missions.Deploy) && !fleet.ReturnFlight ? (long) fleet.ArriveIn : (long) fleet.BackIn) * 1000;
								//Helpers.WriteLog(LogType.Debug, LogSender.Brain, $"Next fleet with resources arriving in transport origin celestial by {DateTime.Now.AddMilliseconds(transportOriginTime).ToString()}");
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
				Helpers.WriteLog(LogType.Error, LogSender.Brain, $"AutoMineCelestial Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
				return interval + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
			}
			if (interval < 0)
				interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
			if (interval == long.MaxValue)
				return interval;
			return interval + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
		}

		private static int HandleMinerTransport(Celestial origin, Celestial destination, Resources resources) {
			try {
				if (origin.ID == destination.ID) {
					Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Skipping transport: origin and destination are the same.");
					return 0;
				} else if (origin.ID == 0) {
					Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Skipping transport: unable to parse transport origin.");
					return 0;
				} else {
					var missingResources = resources.Difference(destination.Resources);

					Resources resToLeave = new();
					if ((long) settings.Brain.AutoMine.Transports.DeutToLeave > 0)
						resToLeave.Deuterium = (long) settings.Brain.AutoMine.Transports.DeutToLeave;

					origin = UpdatePlanet(origin, UpdateType.Resources);
					if (origin.Resources.IsEnoughFor(missingResources, resToLeave)) {
						origin = UpdatePlanet(origin, UpdateType.Ships);
						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoMine.Transports.CargoType, true, out preferredShip)) {
							Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
							preferredShip = Buildables.Null;
						}
						long idealShips = Helpers.CalcShipNumberForPayload(missingResources, preferredShip, researches.HyperspaceTechnology, userInfo.Class, serverData.ProbeCargo);
						Ships ships = new();
						if (idealShips <= origin.Ships.GetAmount(preferredShip)) {
							ships.Add(preferredShip, idealShips);

							if (destination.Coordinate.Type == Celestials.Planet) {
								destination = UpdatePlanet(destination, UpdateType.ResourceSettings);
								destination = UpdatePlanet(destination, UpdateType.Buildings);
								destination = UpdatePlanet(destination, UpdateType.ResourcesProduction);

								var flightPrediction = ogamedService.PredictFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent);
								var flightTime = flightPrediction.Time;

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
									Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
									return SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, userInfo.Class);
								} else {
									Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping transport: it is quicker to wait for production.");
									return 0;
								}
							} else {
								Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Sending {ships.ToString()} with {missingResources.TransportableResources} from {origin.ToString()} to {destination.ToString()}");
								return SendFleet(origin, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, missingResources, userInfo.Class);
							}
						} else {
							Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping transport: not enough ships to transport required resources.");
							return 0;
						}
					} else {
						Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping transport: not enough resources in origin.");
						return 0;
					}
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Brain, $"HandleMinerTransport Exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
				return 0;
			}
		}

		private static void AutoBuildCargo(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.Brain].WaitOne();
				Helpers.WriteLog(LogType.Info, LogSender.Brain, "Running autocargo...");

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				fleets = UpdateFleets();
				List<Celestial> newCelestials = celestials.ToList();
				List<Celestial> celestialsToExclude = Helpers.ParseCelestialsList(settings.Brain.AutoCargo.Exclude, celestials);

				foreach (Celestial celestial in (bool) settings.Brain.AutoCargo.RandomOrder ? celestials.Shuffle().ToList() : celestials) {
					if (celestialsToExclude.Has(celestial)) {
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
						continue;
					}

					var tempCelestial = UpdatePlanet(celestial, UpdateType.Fast);

					fleets = UpdateFleets();
					if ((bool) settings.Brain.AutoCargo.SkipIfIncomingTransport && Helpers.IsThereTransportTowardsCelestial(tempCelestial, fleets)) {
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
						continue;
					}

					tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Productions);
					if (tempCelestial.HasProduction()) {
						Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is already a production ongoing.");
						foreach (Production production in tempCelestial.Productions) {
							Buildables productionType = (Buildables) production.ID;
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are already in production.");
						}
						continue;
					}
					tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Constructions);
					if (tempCelestial.Constructions.BuildingID == (int) Buildables.Shipyard || tempCelestial.Constructions.BuildingID == (int) Buildables.NaniteFactory) {
						Buildables buildingInProgress = (Buildables) tempCelestial.Constructions.BuildingID;
						Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: {buildingInProgress.ToString()} is upgrading.");

					}

					tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Ships);
					tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Resources);
					var capacity = Helpers.CalcFleetCapacity(tempCelestial.Ships, researches.HyperspaceTechnology, userInfo.Class, serverData.ProbeCargo);
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: Available capacity: {capacity.ToString("N0")} - Resources: {tempCelestial.Resources.TotalResources.ToString("N0")}");
					if (tempCelestial.Coordinate.Type == Celestials.Moon && (bool) settings.Brain.AutoCargo.ExcludeMoons) {
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
						continue;
					}
					long neededCargos;
					Buildables preferredCargoShip = Buildables.SmallCargo;
					if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoCargo.CargoType, true, out preferredCargoShip)) {
						Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
						preferredCargoShip = Buildables.SmallCargo;
					}
					if (capacity <= tempCelestial.Resources.TotalResources && (bool) settings.Brain.AutoCargo.LimitToCapacity) {
						long difference = tempCelestial.Resources.TotalResources - capacity;
						int oneShipCapacity = Helpers.CalcShipCapacity(preferredCargoShip, researches.HyperspaceTechnology, userInfo.Class, serverData.ProbeCargo);
						neededCargos = (long) Math.Round((float) difference / (float) oneShipCapacity, MidpointRounding.ToPositiveInfinity);
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"{difference.ToString("N0")} more capacity is needed, {neededCargos} more {preferredCargoShip.ToString()} are needed.");
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
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"{tempCelestial.ToString()}: Building {neededCargos}x{preferredCargoShip.ToString()}");
						else {
							var buildableCargos = Helpers.CalcMaxBuildableNumber(preferredCargoShip, tempCelestial.Resources);
							Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"{tempCelestial.ToString()}: Not enough resources to build {neededCargos}x{preferredCargoShip.ToString()}. {buildableCargos.ToString()} will be built instead.");
							neededCargos = buildableCargos;
						}

						if (neededCargos > 0) {
							var result = ogamedService.BuildShips(tempCelestial, preferredCargoShip, neededCargos);
							if (result)
								Helpers.WriteLog(LogType.Info, LogSender.Brain, "Production succesfully started.");
							else
								Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to start ship production.");
						}

						tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Productions);
						foreach (Production production in tempCelestial.Productions) {
							Buildables productionType = (Buildables) production.ID;
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"{tempCelestial.ToString()}: {production.Nbr}x{productionType.ToString()} are in production.");
						}
					} else {
						Helpers.WriteLog(LogType.Debug, LogSender.Brain, $"{tempCelestial.ToString()}: No ships will be built.");
					}

					newCelestials.Remove(celestial);
					newCelestials.Add(tempCelestial);
				}
				celestials = newCelestials;
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Brain, $"Unable to complete autocargo: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!isSleeping) {
					var time = GetDateTime();
					var interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoCargo.CheckIntervalMin, (int) settings.Brain.AutoCargo.CheckIntervalMax);
					if (interval <= 0)
						interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					var newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("CapacityTimer").Change(interval, Timeout.Infinite);
					Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Next capacity check at {newTime.ToString()}");
					UpdateTitle();
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private static void AutoRepatriate(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.Brain].WaitOne();
				Helpers.WriteLog(LogType.Info, LogSender.Brain, "Repatriating resources...");

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Brain].Release();
					return;
				}

				// if ((bool)settings.AutoRepatriate.RunAutoMineFirst)
				//     AutoMine(null);
				// if ((bool)settings.AutoRepatriate.RunAutoResearchFirst)
				//     AutoResearch(null);

				if (settings.Brain.AutoRepatriate.Target) {
					fleets = UpdateFleets();

					Coordinate destinationCoordinate = new(
						(int) settings.Brain.AutoRepatriate.Target.Galaxy,
						(int) settings.Brain.AutoRepatriate.Target.System,
						(int) settings.Brain.AutoRepatriate.Target.Position,
						Enum.Parse<Celestials>((string) settings.Brain.AutoRepatriate.Target.Type)
					);
					List<Celestial> newCelestials = celestials.ToList();
					List<Celestial> celestialsToExclude = Helpers.ParseCelestialsList(settings.Brain.AutoRepatriate.Exclude, celestials);

					foreach (Celestial celestial in (bool) settings.Brain.AutoRepatriate.RandomOrder ? celestials.Shuffle().ToList() : celestials) {
						if (celestialsToExclude.Has(celestial)) {
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial in exclude list.");
							continue;
						}
						if (celestial.Coordinate.IsSame(destinationCoordinate)) {
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {celestial.ToString()}: celestial is the target.");
							continue;
						}

						var tempCelestial = UpdatePlanet(celestial, UpdateType.Fast);

						fleets = UpdateFleets();
						if ((bool) settings.Brain.AutoRepatriate.SkipIfIncomingTransport && Helpers.IsThereTransportTowardsCelestial(celestial, fleets)) {
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: there is a transport incoming.");
							continue;
						}
						if (celestial.Coordinate.Type == Celestials.Moon && (bool) settings.Brain.AutoRepatriate.ExcludeMoons) {
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: celestial is a moon.");
							continue;
						}

						tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Resources);

						if (tempCelestial.Resources.TotalResources < (int) settings.Brain.AutoRepatriate.MinimumResources) {
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: resources under set limit");
							continue;
						}

						tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Ships);

						Buildables preferredShip = Buildables.SmallCargo;
						if (!Enum.TryParse<Buildables>((string) settings.Brain.AutoRepatriate.CargoType, true, out preferredShip)) {
							Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to parse CargoType. Falling back to default SmallCargo");
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

						if (payload.IsEmpty()) {
							Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Skipping {tempCelestial.ToString()}: resources under set limit");
							continue;
						}

						long idealShips = Helpers.CalcShipNumberForPayload(payload, preferredShip, researches.HyperspaceTechnology, userInfo.Class, serverData.ProbeCargo);

						Ships ships = new();
						if (idealShips <= tempCelestial.Ships.GetAmount(preferredShip)) {
							ships.Add(preferredShip, idealShips);
						} else {
							ships.Add(preferredShip, tempCelestial.Ships.GetAmount(preferredShip));
						}
						payload = Helpers.CalcMaxTransportableResources(ships, payload, researches.HyperspaceTechnology, userInfo.Class, serverData.ProbeCargo);

						var fleetId = SendFleet(tempCelestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
						if (fleetId == -1) {
							stop = true;
							return;
						}

						newCelestials.Remove(celestial);
						newCelestials.Add(tempCelestial);
					}
					celestials = newCelestials;
				} else {
					Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Skipping autorepatriate: unable to parse custom destination");
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Unable to complete repatriate: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!isSleeping) {
					if (stop) {
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Stopping feature.");
					} else {
						var time = GetDateTime();
						var interval = Helpers.CalcRandomInterval((int) settings.Brain.AutoRepatriate.CheckIntervalMin, (int) settings.Brain.AutoRepatriate.CheckIntervalMax);
						if (interval <= 0)
							interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
						Helpers.WriteLog(LogType.Info, LogSender.Brain, $"Next repatriate check at {newTime.ToString()}");
						UpdateTitle();
					}					
					xaSem[Feature.Brain].Release();
				}
			}
		}

		private static int SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Model.Resources payload = null, CharacterClass playerClass = CharacterClass.NoClass, bool force = false) {
			Helpers.WriteLog(LogType.Info, LogSender.FleetScheduler, $"Sending fleet from {origin.Coordinate.ToString()} to {destination.ToString()}. Mission: {mission.ToString()}. Speed: {(speed * 10).ToString()}% Ships: {ships.ToString()}");

			if (playerClass == CharacterClass.NoClass)
				playerClass = userInfo.Class;

			if (!ships.HasMovableFleet()) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: there are no ships to send");
				return 0;
			}
			if (origin.Coordinate.IsSame(destination)) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: origin and destination are the same");
				return 0;
			}
			if (destination.Galaxy <= 0 || destination.Galaxy > serverData.Galaxies || destination.System <= 0 || destination.System > 500 || destination.Position <= 0 || destination.Position > 17) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: invalid destination");
				return 0;
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
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: speed not available for your class");
				return 0;
			}

			var fleetPrediction = ogamedService.PredictFleet(origin, ships, destination, mission, speed);
			var flightTime = mission switch {
				Missions.Deploy => fleetPrediction.Time,
				Missions.Expedition => (long) Math.Round((double) (2 * fleetPrediction.Time) + 3600, 0, MidpointRounding.ToPositiveInfinity),
				_ => (long) Math.Round((double) (2 * fleetPrediction.Time), 0, MidpointRounding.ToPositiveInfinity),
			};

			origin = UpdatePlanet(origin, UpdateType.Resources);
			if (origin.Resources.Deuterium < fleetPrediction.Fuel) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: not enough deuterium!");
				return 0;
			}

			// TODO: Fix ugly workaround.
			if (Helpers.CalcFleetFuelCapacity(ships, serverData.ProbeCargo) != 0 && Helpers.CalcFleetFuelCapacity(ships, serverData.ProbeCargo) < fleetPrediction.Fuel) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: ships don't have enough fuel capacity!");
				return 0;
			}

			if (
				(bool) settings.SleepMode.Active &&
				DateTime.TryParse((string) settings.SleepMode.GoToSleep, out DateTime goToSleep) &&
				DateTime.TryParse((string) settings.SleepMode.WakeUp, out DateTime wakeUp) &&
				!force
			) {
				DateTime time = GetDateTime();

				if (time >= goToSleep && time >= wakeUp) {
					if (goToSleep >= wakeUp)
						wakeUp = wakeUp.AddDays(1);
					else
						goToSleep = goToSleep.AddDays(1);
				}

				var maxDepartureTime = goToSleep.Subtract(TimeSpan.FromSeconds(flightTime)).Subtract(TimeSpan.FromMilliseconds(Helpers.CalcRandomInterval(IntervalType.SomeSeconds)));
				var returnTime = time.Add(TimeSpan.FromSeconds(flightTime)).Add(TimeSpan.FromMilliseconds(Helpers.CalcRandomInterval(IntervalType.SomeSeconds)));
				var minReturnTime = wakeUp.Add(TimeSpan.FromMilliseconds(Helpers.CalcRandomInterval(IntervalType.SomeSeconds)));

				if (time > maxDepartureTime || returnTime < minReturnTime) {
					Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet: it would come back during sleep time");
					return -1;
				}
			}

			slots = UpdateSlots();
			int slotsToLeaveFree = (int) settings.General.SlotsToLeaveFree;
			if (slots.Free > slotsToLeaveFree || force) {
				if (payload == null)
					payload = new();
				try {
					Fleet fleet = ogamedService.SendFleet(origin, ships, destination, mission, speed, payload);
					fleets = ogamedService.GetFleets();
					slots = UpdateSlots();
					return fleet.ID;
				} catch (Exception e) {
					Helpers.WriteLog(LogType.Error, LogSender.FleetScheduler, $"Unable to send fleet: an exception has occurred: {e.Message}");
					Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
					return 0;
				}
			} else {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, "Unable to send fleet, no slots available");
				return 0;
			}
		}

		private static void CancelFleet(Fleet fleet) {
			Helpers.WriteLog(LogType.Info, LogSender.FleetScheduler, $"Recalling fleet id {fleet.ID} originally from {fleet.Origin.ToString()} to {fleet.Destination.ToString()} with mission: {fleet.Mission.ToString()}. Start time: {fleet.StartTime.ToString()} - Arrival time: {fleet.ArrivalTime.ToString()} - Ships: {fleet.Ships.ToString()}");
			slots = UpdateSlots();
			try {
				ogamedService.CancelFleet(fleet);
				Thread.Sleep((int) IntervalType.AFewSeconds);
				fleets = UpdateFleets();
				Fleet recalledFleet = fleets.SingleOrDefault(f => f.ID == fleet.ID) ?? new() { ID = 0 };
				if (recalledFleet.ID == 0) {
					Helpers.WriteLog(LogType.Error, LogSender.FleetScheduler, "Unable to recall fleet: an unknon error has occurred.");
					if ((bool) settings.TelegramMessenger.Active && (bool) settings.Defender.TelegramMessenger.Active) {
						telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] Unable to recall fleet: an unknon error has occurred.");
					}
				}
				Helpers.WriteLog(LogType.Info, LogSender.FleetScheduler, $"Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
				if ((bool) settings.TelegramMessenger.Active && (bool) settings.Defender.TelegramMessenger.Active) {
					telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] Fleet recalled. Arrival time: {recalledFleet.BackTime.ToString()}");
				}
				return;
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.FleetScheduler, $"Unable to recall fleet: an exception has occurred: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
				if ((bool) settings.TelegramMessenger.Active && (bool) settings.Defender.TelegramMessenger.Active) {
					telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] Unable to recall fleet: an exception has occurred.");
				}
				return;
			} finally {
				timers.GetValueOrDefault($"RecallTimer-{fleet.ID.ToString()}").Dispose();
				timers.Remove($"RecallTimer-{fleet.ID.ToString()}");
			}
		}

		private static void RetireFleet(object fleet) {
			CancelFleet((Fleet) fleet);
		}

		private static void HandleAttack(AttackerFleet attack) {
			if (celestials.Count == 0) {
				DateTime time = GetDateTime();
				int interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Unable to handle attack at the moment: bot is still getting account info.");
				Helpers.WriteLog(LogType.Info, LogSender.Defender, $"Next check at {newTime.ToString()}");
				return;
			}

			Celestial attackedCelestial = celestials.Unique().SingleOrDefault(planet => planet.HasCoords(attack.Destination));
			attackedCelestial = UpdatePlanet(attackedCelestial, UpdateType.Ships);

			try {
				if ((settings.Defender.WhiteList as long[]).Any()) {
					foreach (int playerID in (long[]) settings.Defender.WhiteList) {
						if (attack.AttackerID == playerID) {
							Helpers.WriteLog(LogType.Info, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: attacker {attack.AttackerName} whitelisted.");
							return;
						}
					}
				}
			} catch {
				Helpers.WriteLog(LogType.Warning, LogSender.Defender, "An error has occurred while checking Defender WhiteList");
			}

			try {
				if (attack.Ships != null) {
					if ((bool) settings.Defender.IgnoreProbes && attack.IsOnlyProbes()) {
						if (attack.MissionType == Missions.Spy)
							Helpers.WriteLog(LogType.Info, LogSender.Defender, "Espionage action skipped.");
						else
							Helpers.WriteLog(LogType.Info, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: only Espionage Probes.");

						return;
					}
					if (
						(bool) settings.Defender.IgnoreWeakAttack &&
						attack.Ships.GetFleetPoints() < (attackedCelestial.Ships.GetFleetPoints() / (int) settings.Defender.WeakAttackRatio)
					) {
						Helpers.WriteLog(LogType.Info, LogSender.Defender, $"Attack {attack.ID.ToString()} skipped: weak attack.");
						return;
					}
				}
				else {
					Helpers.WriteLog(LogType.Info, LogSender.Defender, "Unable to detect fleet composition.");
				}				
			} catch {
				Helpers.WriteLog(LogType.Warning, LogSender.Defender, "An error has occurred while checking attacker fleet composition");
			}

			if ((bool) settings.TelegramMessenger.Active && (bool) settings.Defender.TelegramMessenger.Active) {
				telegramMessenger.SendMessage($"[{userInfo.PlayerName}@{serverData.Name}.{serverData.Language}] Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attack.Destination.ToString()} arriving at {attack.ArrivalTime.ToString()}");
				if (attack.Ships != null)
					telegramMessenger.SendMessage($"The attack is composed by: {attack.Ships.ToString()}");
			}
			Helpers.WriteLog(LogType.Warning, LogSender.Defender, $"Player {attack.AttackerName} ({attack.AttackerID}) is attacking your planet {attackedCelestial.ToString()} arriving at {attack.ArrivalTime.ToString()}");
			if (attack.Ships != null)
				Helpers.WriteLog(LogType.Warning, LogSender.Defender, $"The attack is composed by: {attack.Ships.ToString()}");

			if ((bool) settings.Defender.SpyAttacker.Active) {
				slots = UpdateSlots();
				if (attackedCelestial.Ships.EspionageProbe == 0) {
					Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not spy attacker: no probes available.");
				} else {
					try {
						Coordinate destination = attack.Origin;
						Ships ships = new() { EspionageProbe = (int) settings.Defender.SpyAttacker.Probes };
						int fleetId = SendFleet(attackedCelestial, ships, destination, Missions.Spy, Speeds.HundredPercent, new Resources(), userInfo.Class);
						Fleet fleet = fleets.Single(fleet => fleet.ID == fleetId);
						Helpers.WriteLog(LogType.Info, LogSender.Defender, $"Spying attacker from {attackedCelestial.ToString()} to {destination.ToString()} with {settings.Defender.SpyAttacker.Probes} probes. Arrival at {fleet.ArrivalTime.ToString()}");
					} catch (Exception e) {
						Helpers.WriteLog(LogType.Error, LogSender.Defender, $"Could not spy attacker: an exception has occurred: {e.Message}");
						Helpers.WriteLog(LogType.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
					}
				}
			}

			if ((bool) settings.Defender.MessageAttacker.Active) {
				try {
					Random random = new();
					string[] messages = settings.Defender.MessageAttacker.Messages;
					string message = messages.ToList().Shuffle().First();
					Helpers.WriteLog(LogType.Info, LogSender.Defender, $"Sending message \"{message}\" to attacker {attack.AttackerName}");
					var result = ogamedService.SendMessage(attack.AttackerID, message);
					if (result)
						Helpers.WriteLog(LogType.Info, LogSender.Defender, "Message succesfully sent.");
					else
						Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Unable send message.");

				} catch (Exception e) {
					Helpers.WriteLog(LogType.Error, LogSender.Defender, $"Could not message attacker: an exception has occurred: {e.Message}");
					Helpers.WriteLog(LogType.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
				}
			}

			if ((bool) settings.Defender.Autofleet.Active) {
				var minFlightTime = attack.ArriveIn + (attack.ArriveIn / 100 * 30) + (Helpers.CalcRandomInterval(IntervalType.SomeSeconds) / 1000);
				AutoFleetSave(attackedCelestial, false, minFlightTime, true);
			}
		}

		private static void HandleExpeditions(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.Expeditions].WaitOne();
				int interval;
				DateTime time;
				DateTime newTime;

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Expeditions].Release();
					return;
				}

				if ((bool) settings.Expeditions.Active) {
					researches = UpdateResearches();
					if (researches.Astrophysics == 0) {
						Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Skipping: Astrophysics not yet researched!");
						time = GetDateTime();
						interval = Helpers.CalcRandomInterval(IntervalType.AboutHalfAnHour);
						newTime = time.AddMilliseconds(interval);						
						timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
						Helpers.WriteLog(LogType.Info, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
						return;
					}

					slots = UpdateSlots();
					fleets = UpdateFleets();
					serverData = ogamedService.GetServerData();
					int expsToSend;
					if ((bool) settings.Expeditions.WaitForAllExpeditions) {
						if (slots.ExpInUse == 0)
							expsToSend = slots.ExpTotal;
						else
							expsToSend = 0;
					} else {
						expsToSend = Math.Min(slots.ExpFree, slots.Free);
					}

					if (expsToSend > 0) {
						if (slots.ExpFree > 0) {
							if (slots.Free > 0) {
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
											Celestial customOrigin = celestials
												.Unique()
												.Single(planet => planet.HasCoords(customOriginCoords));
											customOrigin = UpdatePlanet(customOrigin, UpdateType.Ships);
											origins.Add(customOrigin);
										}
									} catch (Exception e) {
										Helpers.WriteLog(LogType.Debug, LogSender.Expeditions, $"Exception: {e.Message}");
										Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, $"Stacktrace: {e.StackTrace}");
										Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to parse custom origin");

										celestials = UpdatePlanets(UpdateType.Ships);
										origins.Add(celestials
											.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
											.OrderByDescending(planet => Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class, serverData.ProbeCargo))
											.First()
										);
									}
								} else {
									celestials = UpdatePlanets(UpdateType.Ships);
									origins.Add(celestials
										.OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
										.OrderByDescending(planet => Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class, serverData.ProbeCargo))
										.First()
									);
								}
								if ((bool) settings.Expeditions.RandomizeOrder) {
									origins = origins.Shuffle().ToList();
								}
								foreach (var origin in origins) {
									int expsToSendFromThisOrigin;
									if (origins.Count >= expsToSend) {
										expsToSendFromThisOrigin = 1;
									} else {
										expsToSendFromThisOrigin = (int) Math.Round((float) expsToSend / (float) origins.Count, MidpointRounding.ToZero);
										if (origin == origins.Last()) {
											expsToSendFromThisOrigin = (int) Math.Round((float) expsToSend / (float) origins.Count, MidpointRounding.ToZero) + (expsToSend % origins.Count);
										}
									}
									if (origin.Ships.IsEmpty()) {
										Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no ships available");
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
												Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
												continue;
											}
										} else {
											Buildables primaryShip = Buildables.LargeCargo;
											if (!Enum.TryParse<Buildables>(settings.Expeditions.PrimaryShip.ToString(), true, out primaryShip)) {
												Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to parse PrimaryShip. Falling back to default LargeCargo");
												primaryShip = Buildables.LargeCargo;
											}
											if (primaryShip == Buildables.Null) {
												Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: primary ship is Null");
												continue;
											}

											fleet = Helpers.CalcFullExpeditionShips(origin.Ships, primaryShip, expsToSendFromThisOrigin, serverData, researches, userInfo.Class, serverData.ProbeCargo);
											if (fleet.GetAmount(primaryShip) < (long) settings.Expeditions.MinPrimaryToSend) {
												fleet.SetAmount(primaryShip, (long) settings.Expeditions.MinPrimaryToSend);
												if (!origin.Ships.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
													Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, $"Unable to send expeditions: available {primaryShip.ToString()} in origin {origin.ToString()} under set min number of {(long) settings.Expeditions.MinPrimaryToSend}");
													continue;
												}
											}
											Buildables secondaryShip = Buildables.Null;
											if (!Enum.TryParse<Buildables>(settings.Expeditions.SecondaryShip, true, out secondaryShip)) {
												Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to parse SecondaryShip. Falling back to default Null");
												secondaryShip = Buildables.Null;
											}
											if (secondaryShip != Buildables.Null) {
												long secondaryToSend = Math.Min(
													(long) Math.Round(
														origin.Ships.GetAmount(secondaryShip) / (float) expsToSendFromThisOrigin,
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
													Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, $"Unable to send expeditions: available {secondaryShip.ToString()} in origin {origin.ToString()} under set number of {(long) settings.Expeditions.MinSecondaryToSend}");
													continue;
												} else {
													fleet.Add(secondaryShip, secondaryToSend);
													if (!origin.Ships.HasAtLeast(fleet, expsToSendFromThisOrigin)) {
														Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, $"Unable to send expeditions: not enough ships in origin {origin.ToString()}");
														continue;
													}
												}
											}
										}

										Helpers.WriteLog(LogType.Info, LogSender.Expeditions, $"{expsToSendFromThisOrigin.ToString()} expeditions with {fleet.ToString()} will be sent from {origin.ToString()}");
										for (int i = 0; i < expsToSendFromThisOrigin; i++) {
											Coordinate destination;
											if ((bool) settings.Expeditions.SplitExpeditionsBetweenSystems.Active) {
												var rand = new Random();

												int range = (int) settings.Expeditions.SplitExpeditionsBetweenSystems.Range;
												destination = new Coordinate {
													Galaxy = origin.Coordinate.Galaxy,
													System = rand.Next(origin.Coordinate.System - range, origin.Coordinate.System + range + 1),
													Position = 16,
													Type = Celestials.DeepSpace
												};
												if (destination.System <= 0)
													destination.System = 499;
												if (destination.System >= 500)
													destination.System = 1;
											} else {
												destination = new Coordinate {
													Galaxy = origin.Coordinate.Galaxy,
													System = origin.Coordinate.System,
													Position = 16,
													Type = Celestials.DeepSpace
												};
											}
											slots = UpdateSlots();
											Resources payload = new();
											if ((long) settings.Expeditions.FuelToCarry > 0) {
												payload.Deuterium = (long) settings.Expeditions.FuelToCarry;
											}
											if (slots.ExpFree > 0) {
												var fleetId = SendFleet(origin, fleet, destination, Missions.Expedition, Speeds.HundredPercent, payload);
												if (fleetId == -1) {
													stop = true;
													return;
												}
												Thread.Sleep((int) IntervalType.AFewSeconds);
											} else {
												Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Unable to send expeditions: no expedition slots available.");
												break;
											}
										}
									}
								}
							} else {
								Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no fleet slots available");
							}
						} else {
							Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no expeditions slots available");
						}
					}


					fleets = UpdateFleets();
					List<Fleet> orderedFleets = fleets
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

					slots = UpdateSlots();
					if (orderedFleets.Count == 0 || slots.ExpFree > 0) {
						interval = Helpers.CalcRandomInterval(IntervalType.AboutFiveMinutes);
					} else {
						interval = (int) ((1000 * orderedFleets.First().BackIn) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo));
					}
					time = GetDateTime();
					if (interval <= 0)
						interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
					Helpers.WriteLog(LogType.Info, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
					UpdateTitle();
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, $"HandleExpeditions exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, $"Stacktrace: {e.StackTrace}");
				int interval = (int) (Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo));
				var time = GetDateTime();
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Info, LogSender.Expeditions, $"Next check at {newTime.ToString()}");
				UpdateTitle();
			} finally {
				if (!isSleeping) {
					if (stop) {
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Stopping feature.");
					}
					xaSem[Feature.Expeditions].Release();
				}
			}
		}

		private static void HandleHarvest(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.Harvest].WaitOne();

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Harvest].Release();
					return;
				}

				if ((bool) settings.AutoHarvest.Active) {
					Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Detecting harvest targets");

					List<Celestial> newCelestials = celestials.ToList();
					var dic = new Dictionary<Coordinate, Celestial>();

					fleets = UpdateFleets();

					foreach (Planet planet in celestials.Where(c => c is Planet)) {
						Planet tempCelestial = UpdatePlanet(planet, UpdateType.Fast) as Planet;
						tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Ships) as Planet;
						Moon moon = new() {
							Ships = new()
						};

						bool hasMoon = celestials.Count(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) == 1;
						if (hasMoon) {
							moon = celestials.Unique().Single(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) as Moon;
							moon = UpdatePlanet(moon, UpdateType.Ships) as Moon;
						}

						if ((bool) settings.AutoHarvest.HarvestOwnDF) {
							Coordinate dest = new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris);
							if (dic.Keys.Any(d => d.IsSame(dest)))
								continue;
							if (fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
								continue;
							tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Debris) as Planet;
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
									Helpers.WriteLog(LogType.Info, LogSender.Harvest, $"Skipping harvest in {dest.ToString()}: not enough recyclers.");
							}
						}

						if ((bool) settings.AutoHarvest.HarvestDeepSpace) {
							Coordinate dest = new(tempCelestial.Coordinate.Galaxy, tempCelestial.Coordinate.System, 16, Celestials.DeepSpace);
							if (dic.Keys.Any(d => d.IsSame(dest)))
								continue;
							if (fleets.Any(f => f.Mission == Missions.Harvest && f.Destination == dest))
								continue;
							ExpeditionDebris expoDebris = ogamedService.GetGalaxyInfo(tempCelestial.Coordinate).ExpeditionDebris;
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
									Helpers.WriteLog(LogType.Info, LogSender.Harvest, $"Skipping harvest in {dest.ToString()}: not enough pathfinders.");
							}
						}

						newCelestials.Remove(planet);
						newCelestials.Add(tempCelestial);
					}
					celestials = newCelestials;

					if (dic.Count == 0)
						Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Skipping harvest: there are no fields to harvest.");

					foreach (Coordinate destination in dic.Keys) {
						var fleetId = 0;
						Celestial origin = dic[destination];
						if (destination.Position == 16) {
							ExpeditionDebris debris = ogamedService.GetGalaxyInfo(destination).ExpeditionDebris;
							long pathfindersToSend = Math.Min(Helpers.CalcShipNumberForPayload(debris.Resources, Buildables.Pathfinder, researches.HyperspaceTechnology, userInfo.Class), origin.Ships.Pathfinder);
							Helpers.WriteLog(LogType.Info, LogSender.Harvest, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {pathfindersToSend.ToString()} {Buildables.Pathfinder.ToString()}");
							fleetId = SendFleet(origin, new Ships { Pathfinder = pathfindersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
						} else {
							if (celestials.Any(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet)))) {
								Debris debris = (celestials.Where(c => c.HasCoords(new(destination.Galaxy, destination.System, destination.Position, Celestials.Planet))).First() as Planet).Debris;
								long recyclersToSend = Math.Min(Helpers.CalcShipNumberForPayload(debris.Resources, Buildables.Recycler, researches.HyperspaceTechnology, userInfo.Class), origin.Ships.Recycler);
								Helpers.WriteLog(LogType.Info, LogSender.Harvest, $"Harvesting debris in {destination.ToString()} from {origin.ToString()} with {recyclersToSend.ToString()} {Buildables.Recycler.ToString()}");
								fleetId = SendFleet(origin, new Ships { Recycler = recyclersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
							}
						}
						if (fleetId == -1) {
							stop = true;
							return;
						}
					}

					int interval = (int) Helpers.CalcRandomInterval((int) settings.AutoHarvest.CheckIntervalMin, (int) settings.AutoHarvest.CheckIntervalMax);
					var time = GetDateTime();
					if (interval <= 0)
						interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
					DateTime newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
					Helpers.WriteLog(LogType.Info, LogSender.Harvest, $"Next check at {newTime.ToString()}");
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.Harvest, $"HandleHarvest exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Harvest, $"Stacktrace: {e.StackTrace}");
				int interval = (int) Helpers.CalcRandomInterval((int) settings.AutoHarvest.CheckIntervalMin, (int) settings.AutoHarvest.CheckIntervalMax);
				var time = GetDateTime();
				if (interval <= 0)
					interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Info, LogSender.Harvest, $"Next check at {newTime.ToString()}");
				UpdateTitle();
			} finally {
				if (!isSleeping) {
					if (stop) {
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Stopping feature.");
					}
					xaSem[Feature.Harvest].Release();
				}
			}
		}
		private static void HandleColonize(object state) {
			bool stop = false;
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				xaSem[Feature.Colonize].WaitOne();

				if (isSleeping) {
					Helpers.WriteLog(LogType.Info, LogSender.Colonize, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Colonize].Release();
					return;
				}

				if ((bool) settings.AutoColonize.Active) {
					int interval = Helpers.CalcRandomInterval((int) settings.AutoColonize.CheckIntervalMin, (int) settings.AutoColonize.CheckIntervalMax);
					Helpers.WriteLog(LogType.Info, LogSender.Colonize, "Checking if a new planet is needed...");

					researches = UpdateResearches();
					var maxPlanets = Helpers.CalcMaxPlanets(researches);
					var currentPlanets = celestials.Where(c => c.Coordinate.Type == Celestials.Planet).Count();
					var slotsToLeaveFree = (int) (settings.AutoColonize.SlotsToLeaveFree ?? 0);
					if (currentPlanets + slotsToLeaveFree < maxPlanets) {
						Helpers.WriteLog(LogType.Info, LogSender.Colonize, "A new planet is needed.");

						fleets = UpdateFleets();
						if (fleets.Any(f => f.Mission == Missions.Colonize && !f.ReturnFlight)) {
							Helpers.WriteLog(LogType.Info, LogSender.Colonize, "Colony Ship(s) already in flight.");
							interval = fleets
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
							Celestial origin = celestials.Single(c => c.HasCoords(originCoords));
							UpdatePlanet(origin, UpdateType.Ships);

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
									if (celestials.Any(c => c.HasCoords(t))) {
										continue;
									}
									GalaxyInfo galaxy = ogamedService.GetGalaxyInfo(t);
									if (galaxy.Planets.Any(p => p != null && p.HasCoords(t))) {
										continue;
									}
									filteredTargets.Add(t);
								}
								if (filteredTargets.Count > 0) {
									filteredTargets = filteredTargets
										.OrderBy(t => Helpers.CalcDistance(origin.Coordinate, t, serverData))
										.Take(maxPlanets - currentPlanets)
										.ToList();
									foreach (var target in filteredTargets) {
										Ships ships = new() { ColonyShip = 1 };
										var fleetId = SendFleet(origin, ships, target, Missions.Colonize, Speeds.HundredPercent);
										if (fleetId == -1) {
											stop = true;
											return;
										}
									}
								} else {
									Helpers.WriteLog(LogType.Info, LogSender.Colonize, "No valid coordinate in target list.");
								}
							} else {
								UpdatePlanet(origin, UpdateType.Productions);
								UpdatePlanet(origin, UpdateType.Facilities);
								if (origin.Productions.Any(p => p.ID == (int) Buildables.ColonyShip)) {
									Helpers.WriteLog(LogType.Info, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed. {origin.Productions.First(p => p.ID == (int) Buildables.ColonyShip).Nbr} colony ship(s) already in production.");
									interval = (int) Helpers.CalcProductionTime(Buildables.ColonyShip, origin.Productions.First(p => p.ID == (int) Buildables.ColonyShip).Nbr, serverData, origin.Facilities) * 1000;
								} else {
									Helpers.WriteLog(LogType.Info, LogSender.Colonize, $"{neededColonizers} colony ship(s) needed.");
									UpdatePlanet(origin, UpdateType.Resources);
									var cost = Helpers.CalcPrice(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip);
									if (origin.Resources.IsEnoughFor(cost)) {
										UpdatePlanet(origin, UpdateType.Constructions);
										if (origin.HasConstruction() && (origin.Constructions.BuildingID == (int) Buildables.Shipyard || origin.Constructions.BuildingID == (int) Buildables.NaniteFactory)) {
											Helpers.WriteLog(LogType.Info, LogSender.Colonize, $"Unable to build colony ship: {((Buildables) origin.Constructions.BuildingID).ToString()} is in construction");
										}
										else if (origin.Facilities.Shipyard >= 4 && researches.ImpulseDrive >= 3) {
											Helpers.WriteLog(LogType.Info, LogSender.Colonize, $"Building {neededColonizers - origin.Ships.ColonyShip}....");
											ogamedService.BuildShips(origin, Buildables.ColonyShip, neededColonizers - origin.Ships.ColonyShip);
											interval = (int) Helpers.CalcProductionTime(Buildables.ColonyShip, neededColonizers - (int) origin.Ships.ColonyShip, serverData, origin.Facilities) * 1000;
										}
										else {
											Helpers.WriteLog(LogType.Info, LogSender.Colonize, $"Requirements to build colony ship not met");
										}
									}
									else {
										Helpers.WriteLog(LogType.Info, LogSender.Colonize, $"Not enough resources to build {neededColonizers} colony ship(s).");
									}
								}
							}							
						}
					} else {
						Helpers.WriteLog(LogType.Info, LogSender.Colonize, "No new planet is needed.");
					}

					DateTime time = GetDateTime();
					if (interval <= 0) {
						interval = Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
					}

					DateTime newTime = time.AddMilliseconds(interval);
					timers.GetValueOrDefault("ColonizeTimer").Change(interval, Timeout.Infinite);
					Helpers.WriteLog(LogType.Info, LogSender.Colonize, $"Next check at {newTime}");
				}
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.Colonize, $"HandleColonize exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.Colonize, $"Stacktrace: {e.StackTrace}");
				int interval = Helpers.CalcRandomInterval((int) settings.AutoColonize.CheckIntervalMin, (int) settings.AutoColonize.CheckIntervalMax);
				DateTime time = GetDateTime();
				if (interval <= 0)
					interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("ColonizeTimer").Change(interval, Timeout.Infinite);
				Helpers.WriteLog(LogType.Info, LogSender.Colonize, $"Next check at {newTime}");
				UpdateTitle();
			} finally {
				if (!isSleeping) {
					if (stop) {
						Helpers.WriteLog(LogType.Info, LogSender.AutoFarm, $"Stopping feature.");
					}
					xaSem[Feature.Colonize].Release();
				}
			}
		}

		private static void ScheduleFleet(object scheduledFleet) {
			FleetSchedule _scheduledFleet = scheduledFleet as FleetSchedule;
			try {
				xaSem[Feature.FleetScheduler].WaitOne();
				scheduledFleets.Add(_scheduledFleet);
				SendFleet(_scheduledFleet.Origin, _scheduledFleet.Ships, _scheduledFleet.Destination, _scheduledFleet.Mission, _scheduledFleet.Speed, _scheduledFleet.Payload, userInfo.Class);
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"ScheduleFleet exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
			} finally {
				scheduledFleets = scheduledFleets.OrderBy(f => f.Departure).ToList();
				if (scheduledFleets.Count > 0) {
					long nextTime = (long) scheduledFleets.FirstOrDefault().Departure.Subtract(GetDateTime()).TotalMilliseconds;
					timers.GetValueOrDefault("FleetSchedulerTimer").Change(nextTime, Timeout.Infinite);
					Helpers.WriteLog(LogType.Info, LogSender.FleetScheduler, $"Next scheduled fleet at {scheduledFleets.First().ToString()}");
				}
				xaSem[Feature.FleetScheduler].Release();
			}
		}

		private static void HandleScheduledFleet(object scheduledFleet) {
			FleetSchedule _scheduledFleet = scheduledFleet as FleetSchedule;
			try {
				xaSem[Feature.FleetScheduler].WaitOne();
				SendFleet(_scheduledFleet.Origin, _scheduledFleet.Ships, _scheduledFleet.Destination, _scheduledFleet.Mission, _scheduledFleet.Speed, _scheduledFleet.Payload, userInfo.Class);
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"HandleScheduledFleet exception: {e.Message}");
				Helpers.WriteLog(LogType.Warning, LogSender.FleetScheduler, $"Stacktrace: {e.StackTrace}");
			} finally {
				scheduledFleets.Remove(_scheduledFleet);
				scheduledFleets = scheduledFleets.OrderBy(f => f.Departure).ToList();
				if (scheduledFleets.Count > 0) {
					long nextTime = (long) scheduledFleets.FirstOrDefault().Departure.Subtract(GetDateTime()).TotalMilliseconds;
					timers.GetValueOrDefault("FleetSchedulerTimer").Change(nextTime, Timeout.Infinite);
					Helpers.WriteLog(LogType.Info, LogSender.FleetScheduler, $"Next scheduled fleet at {scheduledFleets.First().ToString()}");
				}
				xaSem[Feature.FleetScheduler].Release();
			}
		}
	}
}
