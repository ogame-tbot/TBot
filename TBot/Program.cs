using Tbot.Services;
using Tbot.Model;
using Tbot.Includes;
using System;
using System.Threading;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System.Collections.Concurrent;

namespace Tbot
{
    class Program
    {
        static volatile OgamedService ogamedService;

        static volatile TelegramMessenger telegramMessenger;

        static volatile Dictionary<string, Timer> timers;

        static volatile dynamic settings;

        static volatile Server serverInfo;
        static volatile ServerData serverData;
        static volatile UserInfo userInfo;
        static volatile List<Celestial> celestials;
        static volatile List<Fleet> fleets;
        static volatile List<GalaxyInfo> galaxyInfos;
        static volatile List<AttackerFleet> attacks;
        static volatile Slots slots;
        static volatile Researches researches;
        static volatile ConcurrentDictionary<Feature, bool> features;
        static volatile List<FleetSchedule> scheduledFleets;
        static volatile bool isSleeping;

        /*Lorenzo 07/02/2021
         * Added array of semaphore to manage the cuncurrency
         * for timers.
         */
        static Dictionary<Feature, Semaphore> xaSem = new();

        static void Main(string[] args)
        {
            Helpers.SetTitle();
            isSleeping = false;

            ReadSettings();
            FileSystemWatcher settingsWatcher = new(Path.GetFullPath(AppContext.BaseDirectory));
            settingsWatcher.Filter = "settings.json";
            settingsWatcher.NotifyFilter = NotifyFilters.LastWrite;
            settingsWatcher.Changed += new(OnSettingsChanged);
            settingsWatcher.EnableRaisingEvents = true;

            Credentials credentials = new()
            {
                Universe = (string)settings.Credentials.Universe,
                Username = (string)settings.Credentials.Email,
                Password = (string)settings.Credentials.Password,
                Language = (string)settings.Credentials.Language
            };

            try
            {
                /**
                 * Tralla 20/2/21
                 * 
                 * add ability to set custom host 
                 */
                string host = (string)settings.General.Host ?? "localhost";
                string port = (string)settings.General.Port ?? "8080";
                string captchaKey = (string)settings.General.CaptchaAPIKey ?? "";
                ProxySettings proxy = null;
                if ((bool)settings.General.Proxy.Active && (string)settings.General.Proxy.Address != "" )
                {
                    Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing proxy");
                    proxy.Enabled = (bool)settings.General.Proxy.Active;
                    proxy.Address = (string)settings.General.Proxy.Address;
                    proxy.Type = (string)settings.General.Proxy.Type ?? "socks5";
                    proxy.Username = (string)settings.General.Proxy.Username ?? "";
                    proxy.Password = (string)settings.General.Proxy.Password ?? "";

                }
                ogamedService = new OgamedService(credentials, (string)host, int.Parse(port), (string)captchaKey, proxy);
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to start ogamed: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }

            try
            {
                ogamedService.SetUserAgent((string)settings.General.UserAgent);
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to set user agent: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
            Thread.Sleep(Helpers.CalcRandomInterval(IntervalType.LessThanASecond));


            var isLoggedIn = false;
            try
            {
                isLoggedIn = ogamedService.Login();
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to login: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
            Thread.Sleep(Helpers.CalcRandomInterval(IntervalType.AFewSeconds));

            if (!isLoggedIn)
            {
                ogamedService.KillOgamedExecultable();
                Console.ReadLine();
            }
            else
            {
                serverInfo = UpdateServerInfo();
                serverData = UpdateServerData();
                userInfo = UpdateUserInfo();

                UpdateTitle(false);

                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Server time: " + GetDateTime().ToString());

                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Player name: " + userInfo.PlayerName);
                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Player class: " + userInfo.Class.ToString());
                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Player rank: " + userInfo.Rank);
                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Player points: " + userInfo.Points);
                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Player honour points: " + userInfo.HonourPoints);

                if (!ogamedService.IsVacationMode())
                {
                    if ((bool)settings.TelegramMessenger.Active)
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Activating Telegram Messenger");
                        telegramMessenger = new TelegramMessenger((string)settings.TelegramMessenger.API, (string)settings.TelegramMessenger.ChatId);
                        telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] TBot activated");
                    }                    

                    timers = new Dictionary<string, Timer>();

                    /*Lorenzo 07/02/2020 initialize semaphores
                     * For the same reason in the xaSem declaration
                     * if you add some timers add the initialization
                     * of their timer
                     * 
                     * Tralla 12/2/20
                     * change index to enum
                     */
                    xaSem[Feature.Defender] = new Semaphore(1, 1); //Defender
                    xaSem[Feature.BrainAutobuildCargo] = new Semaphore(1, 1); //Brain - Autobuild cargo
                    xaSem[Feature.BrainAutoRepatriate] = new Semaphore(1, 1); //Brain - AutoRepatriate
                    xaSem[Feature.BrainAutoMine] = new Semaphore(1, 1); //Brain - Auto mine
                    xaSem[Feature.BrainOfferOfTheDay] = new Semaphore(1, 1); //Brain - Offer of the day
                    xaSem[Feature.Expeditions] = new Semaphore(1, 1); //Expeditions
                    xaSem[Feature.Harvest] = new Semaphore(1, 1); //Harvest
                    xaSem[Feature.FleetScheduler] = new Semaphore(1, 1); //FleetScheduler
                    xaSem[Feature.SleepMode] = new Semaphore(1, 1); //SleepMode

                    features = new();
                    features.AddOrUpdate(Feature.Defender, false, HandleStartStopFeatures);
                    features.AddOrUpdate(Feature.Brain, false, HandleStartStopFeatures);
                    features.AddOrUpdate(Feature.BrainAutobuildCargo, false, HandleStartStopFeatures);
                    features.AddOrUpdate(Feature.BrainAutoRepatriate, false, HandleStartStopFeatures);
                    features.AddOrUpdate(Feature.BrainAutoMine, false, HandleStartStopFeatures);
                    features.AddOrUpdate(Feature.BrainOfferOfTheDay, false, HandleStartStopFeatures);
                    features.AddOrUpdate(Feature.Expeditions, false, HandleStartStopFeatures);
                    features.AddOrUpdate(Feature.Harvest, false, HandleStartStopFeatures);
                    features.AddOrUpdate(Feature.SleepMode, false, HandleStartStopFeatures);

                    Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing data...");
                    celestials = GetPlanets();
                    researches = ogamedService.GetResearches();
                    scheduledFleets = new();

                    Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing features...");
                    InitializeFeatures();
                }
                else
                {
                    Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "Account in vacation mode");
                }

                Console.ReadLine();
                ogamedService.KillOgamedExecultable();
            }

        }

        private static bool HandleStartStopFeatures(Feature feature, bool currentValue)
        {
            if (isSleeping && (bool)settings.SleepMode.Active)
                switch (feature)
                {
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
                    case Feature.Expeditions:
                        if (currentValue)
                            StopExpeditions();
                        return false;
                    case Feature.Harvest:
                        if (currentValue)
                            StopHarvest();
                        return false;
                    case Feature.SleepMode:
                        if (!currentValue)
                            InitializeSleepMode();
                        return true;
                    default:
                        return false;
                }

            switch (feature)
            {
                case Feature.Defender:
                    if ((bool)settings.Defender.Active)
                    {
                        if (!currentValue)
                            InitializeDefender();
                        return true;
                    }
                    else
                    {
                        if (currentValue)
                            StopDefender();
                        return false;
                    }
                case Feature.Brain:
                    if ((bool)settings.Brain.Active)
                        return true;
                    else
                        return false;
                case Feature.BrainAutobuildCargo:
                    if ((bool)settings.Brain.Active && (bool)settings.Brain.AutoCargo.Active)
                    {
                        if (!currentValue)
                            InitializeBrainAutoCargo();
                        return true;
                    }
                    else
                    {
                        if (currentValue)
                            StopBrainAutoCargo();
                        return false;
                    }
                case Feature.BrainAutoRepatriate:
                    if ((bool)settings.Brain.Active && (bool)settings.Brain.AutoRepatriate.Active)
                    {
                        if (!currentValue)
                            InitializeBrainRepatriate();
                        return true;
                    }
                    else
                    {
                        if (currentValue)
                            StopBrainRepatriate();
                        return false;
                    }
                case Feature.BrainAutoMine:
                    if ((bool)settings.Brain.Active && (bool)settings.Brain.AutoMine.Active)
                    {
                        if (!currentValue)
                            InitializeBrainAutoMine();
                        return true;
                    }
                    else
                    {
                        if (currentValue)
                            StopBrainAutoMine();
                        return false;
                    }
                case Feature.BrainOfferOfTheDay:
                    if ((bool)settings.Brain.Active && (bool)settings.Brain.BuyOfferOfTheDay.Active)
                    {
                        if (!currentValue)
                            InitializeBrainOfferOfTheDay();
                        return true;
                    }
                    else
                    {
                        if (currentValue)
                            StopBrainOfferOfTheDay();
                        return false;
                    }
                case Feature.Expeditions:
                    if ((bool)settings.Expeditions.Active)
                    {
                        if (!currentValue)
                            InitializeExpeditions();
                        return true;
                    }
                    else
                    {
                        if (currentValue)
                            StopExpeditions();
                        return false;
                    }
                case Feature.Harvest:
                    if ((bool)settings.AutoHarvest.Active)
                    {
                        if (!currentValue)
                            InitializeHarvest();
                        return true;
                    }
                    else
                    {
                        if (currentValue)
                            StopHarvest();
                        return false;
                    }
                case Feature.SleepMode:
                    if ((bool)settings.SleepMode.Active)
                    {
                        if (!currentValue)
                            InitializeSleepMode();
                        return true;
                    }
                    else
                    {
                        if (currentValue)
                            StopSleepMode();
                        return false;
                    }
                default:
                    return false;
            }
        }

        private static void InitializeFeatures()
        {
            features.AddOrUpdate(Feature.Defender, false, HandleStartStopFeatures);
            features.AddOrUpdate(Feature.Brain, false, HandleStartStopFeatures);
            features.AddOrUpdate(Feature.BrainAutobuildCargo, false, HandleStartStopFeatures);
            features.AddOrUpdate(Feature.BrainAutoRepatriate, false, HandleStartStopFeatures);
            features.AddOrUpdate(Feature.BrainAutoMine, false, HandleStartStopFeatures);
            features.AddOrUpdate(Feature.BrainOfferOfTheDay, false, HandleStartStopFeatures);
            features.AddOrUpdate(Feature.Expeditions, false, HandleStartStopFeatures);
            features.AddOrUpdate(Feature.Harvest, false, HandleStartStopFeatures);
            features.AddOrUpdate(Feature.SleepMode, false, HandleStartStopFeatures);
        }

        private static void InitializeFleetScheduler()
        {
            scheduledFleets = new();

        }

        private static void ReadSettings()
        {
            settings = SettingsService.GetSettings();
        }

        private static void OnSettingsChanged(object sender, FileSystemEventArgs e)
        {
            if (e.ChangeType != WatcherChangeTypes.Changed)
            {
                return;
            }
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Settings file changed");
            ReadSettings();
            InitializeFeatures();
            UpdateTitle();
        }

        private static DateTime GetDateTime()
        {
            DateTime dateTime = ogamedService.GetServerTime();
            if (dateTime.Kind == DateTimeKind.Utc)
                return dateTime.ToLocalTime();
            else
                return dateTime;
        }

        private static Slots UpdateSlots()
        {
            return ogamedService.GetSlots();
        }

        private static List<Fleet> UpdateFleets()
        {
            return ogamedService.GetFleets();
        }

        private static List<GalaxyInfo> UpdateGalaxyInfos()
        {
            List<GalaxyInfo> galaxyInfos = new();
            Planet newPlanet = new();
            List<Celestial> newCelestials = celestials.ToList();
            foreach (Planet planet in celestials.Where(p => p is Planet))
            {
                newPlanet = planet;
                var gi = ogamedService.GetGalaxyInfo(planet.Coordinate);
                newPlanet.Debris = gi.Planets.Single(p => p != null && p.ID == planet.ID).Debris ?? new();
                galaxyInfos.Add(gi);
                Planet oldPlanet = celestials.Single(p => p.HasCoords(newPlanet.Coordinate)) as Planet;
                newCelestials.Remove(oldPlanet);
                newCelestials.Add(newPlanet);
            }            
            celestials = newCelestials;
            return galaxyInfos;
        }

        private static ServerData UpdateServerData()
        {
            return ogamedService.GetServerData();
        }

        private static Server UpdateServerInfo()
        {
            return ogamedService.GetServerInfo();
        }

        private static UserInfo UpdateUserInfo()
        {
            UserInfo user = ogamedService.GetUserInfo();
            user.Class = ogamedService.GetUserClass();
            return user;
        }

        private static List<Celestial> GetPlanets()
        {
            List<Celestial> localPlanets = celestials ?? new();
            List<Celestial> ogamedPlanets = ogamedService.GetCelestials();
            if (localPlanets.Count == 0 || ogamedPlanets.Count != celestials.Count)
            {
                localPlanets = ogamedPlanets.ToList();
            }
            return localPlanets;
        }

        private static List<Celestial> UpdatePlanets(UpdateType updateType = UpdateType.Full)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Updating celestials... Mode: " + updateType.ToString());
            List<Celestial> localPlanets = GetPlanets();
            List<Celestial> newPlanets = new();
            foreach (Celestial planet in localPlanets)
            {
                newPlanets.Add(UpdatePlanet(planet, updateType));
            }
            return newPlanets;
        }

        private static Celestial UpdatePlanet(Celestial planet, UpdateType updateType = UpdateType.Full)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Updating celestial " + planet.ToString() + ". Mode: " + updateType.ToString());
            try
            {
                switch (updateType)
                {
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
                        if (planet is Planet)
                        {
                            planet.ResourceSettings = ogamedService.GetResourceSettings(planet as Planet);
                        }
                        break;
                    case UpdateType.ResourceProduction:
                        if (planet is Planet)
                        {
                            planet.ResourceProduction = ogamedService.GetResourceProduction(planet as Planet);
                        }
                        break;
                    case UpdateType.Techs:
                        var techs = ogamedService.GetTechs(planet);
                        planet.Defences = techs.defenses;
                        planet.Facilities = techs.facilities;
                        planet.Ships = techs.ships;
                        planet.Buildings = techs.supplies;
                        break;
                    case UpdateType.Full:
                    default:
                        planet.Resources = ogamedService.GetResources(planet);
                        planet.Productions = ogamedService.GetProductions(planet);
                        planet.Constructions = ogamedService.GetConstructions(planet);
                        if (planet is Planet)
                        {
                            planet.ResourceSettings = ogamedService.GetResourceSettings(planet as Planet);
                            planet.ResourceProduction = ogamedService.GetResourceProduction(planet as Planet);
                        }
                        planet.Buildings = ogamedService.GetBuildings(planet);
                        planet.Facilities = ogamedService.GetFacilities(planet);
                        planet.Ships = ogamedService.GetShips(planet);
                        planet.Defences = ogamedService.GetDefences(planet);
                        break;
                }
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Debug, LogSender.Tbot, "Exception: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "An error has occurred. Skipping update");
            }
            return planet;
        }

        private static void UpdateTitle(bool force = true)
        {
            if (force)
            {
                serverInfo = UpdateServerInfo();
                serverData = UpdateServerData();
                userInfo = UpdateUserInfo();
            }
            if ((string)settings.General.CustomTitle != "")
                Helpers.SetTitle((string)settings.General.CustomTitle + " - [" + serverInfo.Name + "." + serverInfo.Language + "]" + " " + userInfo.PlayerName + " - Rank: " + userInfo.Rank);
            else
                Helpers.SetTitle("[" + serverInfo.Name + "." + serverInfo.Language + "]" + " " + userInfo.PlayerName + " - Rank: " + userInfo.Rank);
        }

        private static void InitializeDefender()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing defender...");
            timers.Add("DefenderTimer", new Timer(Defender, null, 0, Helpers.CalcRandomInterval((int)settings.Defender.CheckIntervalMin, (int)settings.Defender.CheckIntervalMax)));
        }

        private static void StopDefender()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping defender...");
            timers.GetValueOrDefault("DefenderTimer").Dispose();
            timers.Remove("DefenderTimer");
        }

        private static void InitializeBrainAutoCargo()
        {            
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing autocargo...");
            timers.Add("CapacityTimer", new Timer(AutoBuildCargo, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax)));
        }

        private static void StopBrainAutoCargo()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping autocargo...");
            timers.GetValueOrDefault("CapacityTimer").Dispose();
            timers.Remove("CapacityTimer");
        }

        private static void InitializeBrainRepatriate()
        {            
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing repatriate...");
            timers.Add("RepatriateTimer", new Timer(AutoRepatriate, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax)));
        }

        private static void StopBrainRepatriate()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping repatriate...");
            timers.GetValueOrDefault("RepatriateTimer").Dispose();
            timers.Remove("RepatriateTimer");
        }

        private static void InitializeBrainAutoMine()
        {            
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing automine...");
            timers.Add("AutoMineTimer", new Timer(AutoMine, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax)));
        }

        private static void StopBrainAutoMine()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping automine...");
            timers.GetValueOrDefault("AutoMineTimer").Dispose();
            timers.Remove("AutoMineTimer");
        }

        private static void InitializeBrainOfferOfTheDay()
        {            
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing offer of the day...");
            timers.Add("OfferOfTheDayTimer", new Timer(BuyOfferOfTheDay, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax)));
        }

        private static void StopBrainOfferOfTheDay()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping offer of the day...");
            timers.GetValueOrDefault("OfferOfTheDayTimer").Dispose();
            timers.Remove("OfferOfTheDayTimer");
        }

        private static void InitializeExpeditions()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing expeditions...");
            timers.Add("ExpeditionsTimer", new Timer(HandleExpeditions, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo)));
        }

        private static void StopExpeditions()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping expeditions...");
            timers.GetValueOrDefault("ExpeditionsTimer").Dispose();
            timers.Remove("ExpeditionsTimer");
        }

        private static void InitializeHarvest()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing harvest...");
            timers.Add("HarvestTimer", new Timer(HandleHarvest, null, Helpers.CalcRandomInterval(IntervalType.AFewSeconds), Helpers.CalcRandomInterval(IntervalType.AboutFiveMinutes)));
        }

        private static void StopHarvest()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping harvest...");
            timers.GetValueOrDefault("HarvestTimer").Dispose();
            timers.Remove("HarvestTimer");
        }

        private static void InitializeSleepMode()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing sleep mode...");
            timers.Add("SleepModeTimer", new Timer(HandleSleepMode, null, 0, Helpers.CalcRandomInterval(IntervalType.AboutFiveMinutes)));            
        }

        private static void StopSleepMode()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Stopping sleep mode...");
            timers.GetValueOrDefault("SleepModeTimer").Dispose();
            timers.Remove("SleepModeTimer");
        }

        private static void HandleSleepMode(object state)
        {
            DateTime time = GetDateTime();
            DateTime goToSleep = time;
            DateTime wakeUp = time;

            if (!DateTime.TryParse((string)settings.SleepMode.GoToSleep, out goToSleep))
            {
                Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "Unable to parse GoToSleep time. Sleep mode will be disabled");
            }
            else if (!DateTime.TryParse((string)settings.SleepMode.WakeUp, out wakeUp))
            {
                Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "Unable to parse WakeUp time. Sleep mode will be disabled");
            }
            else if (goToSleep == wakeUp)
            {
                Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "GoToSleep time and WakeUp time must be different. Sleep mode will be disabled");
            }
            else
            {
                long interval;

                if (time >= wakeUp)
                {
                    if (goToSleep < wakeUp)
                        goToSleep = goToSleep.AddDays(1);
                    interval = (long)goToSleep.Subtract(DateTime.Now).TotalMilliseconds + (long)Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
                    timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
                    DateTime newTime = time.AddMilliseconds(interval);
                    WakeUp(newTime);
                    Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "Going to sleep at " + newTime.ToString());
                }
                if (time >= goToSleep)
                {
                    if (goToSleep > wakeUp)
                        wakeUp = wakeUp.AddDays(1);
                    interval = (long)wakeUp.Subtract(DateTime.Now).TotalMilliseconds + (long)Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo);
                    timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
                    DateTime newTime = time.AddMilliseconds(interval);
                    GoToSleep(newTime);
                    Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "Waking up at " + newTime.ToString());
                }
                
            }
        }

        private static void GoToSleep(object state)
        {
            try
            {
                xaSem[Feature.SleepMode].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "Going to sleep...");
                if ((bool)settings.TelegramMessenger.Active && (bool)settings.SleepMode.TelegramMessenger.Active)
                {
                    telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Going to sleep");
                    telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Waking Up at " + state.ToString());
                }
                isSleeping = true;
                InitializeFeatures();
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "An error has occurred while going to sleep: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                DateTime time = GetDateTime();
                int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                DateTime newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "Next check at " + newTime.ToString());
                UpdateTitle();
            }
            finally
            {
                xaSem[Feature.SleepMode].Release();
            }
        }

        private static void WakeUp(object state)
        {
            try
            {
                xaSem[Feature.SleepMode].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "Waking Up...");
                if ((bool)settings.TelegramMessenger.Active && (bool)settings.SleepMode.TelegramMessenger.Active)
                {
                    telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Waking up");
                    telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Going to sleep at " + state.ToString());
                }
                isSleeping = false;
                InitializeFeatures();

            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Warning, LogSender.SleepMode, "An error has occurred while going to sleep: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                DateTime time = GetDateTime();
                int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                DateTime newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("SleepModeTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.SleepMode, "Next check at " + newTime.ToString());
                UpdateTitle();
            }
            finally
            {
                xaSem[Feature.SleepMode].Release();
            }
        }

        private static void Defender(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[Feature.Defender].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.Defender, "Checking attacks...");
                bool isUnderAttack = ogamedService.IsUnderAttack();
                DateTime time = GetDateTime();
                if (isUnderAttack)
                {
                    if ((bool)settings.Defender.Alarm.Active)
                        Task.Factory.StartNew(() => Helpers.PlayAlarm());
                    Helpers.SetTitle("ENEMY ACTIVITY DETECTED - [" + serverInfo.Name + "." + serverInfo.Language + "]" + " " + userInfo.PlayerName + " - Rank: " + userInfo.Rank);
                    Helpers.WriteLog(LogType.Warning, LogSender.Defender, "ENEMY ACTIVITY!!!");
                    attacks = ogamedService.GetAttacks();
                    HandleAttack(state);
                }
                else
                {
                    Helpers.SetTitle("[" + serverInfo.Name + "." + serverInfo.Language + "]" + " " + userInfo.PlayerName + " - Rank: " + userInfo.Rank);
                    Helpers.WriteLog(LogType.Info, LogSender.Defender, "Your empire is safe");
                }
                int interval = Helpers.CalcRandomInterval((int)settings.Defender.CheckIntervalMin, (int)settings.Defender.CheckIntervalMax);
                DateTime newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                UpdateTitle();
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "An error has occurred while checking for attacks: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                DateTime time = GetDateTime();
                int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                DateTime newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                UpdateTitle();
            }
            finally
            {
                //Release its semaphore
                xaSem[Feature.Defender].Release();
            }

        }

        private static void BuyOfferOfTheDay(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[Feature.BrainOfferOfTheDay].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Buying offer of the day...");
                var result = ogamedService.BuyOfferOfTheDay();
                if (result)
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Offer of the day succesfully bought.");
                else
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Offer of the day already bought.");
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Brain, "BuyOfferOfTheDay Exception: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
            finally
            {
                var time = GetDateTime();
                var interval = Helpers.CalcRandomInterval((int)settings.Brain.BuyOfferOfTheDay.CheckIntervalMin, (int)settings.Brain.BuyOfferOfTheDay.CheckIntervalMax);
                var newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("OfferOfTheDayTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next BuyOfferOfTheDay check at " + newTime.ToString());
                UpdateTitle();
                //Release its semaphore
                xaSem[Feature.BrainOfferOfTheDay].Release();
            }
        }

        /*Lorenzo 06/02/2021
         * 
         * Method call by the timer to manage the auto build for mines.
         * 
         * All the logic is based on the MetalMine level (n).
         * 
         * At now there are 3 steps: n<=15 --> n;n-2;n-1 / 15<n<=30 n;n-4;n-2 / n>30 n;n-7;n-3
         * 
         * If all the mines level rules are satisfied by default it will be request to build a Metal Mine
         * to force the dissatisfaction of them
         */
        private static void AutoMine(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[Feature.BrainAutoMine].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Running automine");

                Buildables xBuildable = Buildables.Null;
                int nLevelToReach = 0;
                List<Celestial> newCelestials = celestials.ToList();
                foreach (Celestial xCelestial in celestials)
                {
                    var tempCelestial = xCelestial;                    
                    tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Constructions);
                    if (tempCelestial.Constructions.BuildingID != 0)
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping celestial " + tempCelestial.ToString() + ": there is already a building in production.");
                        continue;
                    }
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Running AutoMine for celestial " + tempCelestial.ToString());
                    if (tempCelestial is Planet)
                    {
                        tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Resources);
                        tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Buildings);
                        if (Helpers.ShouldBuildEnergySource(tempCelestial as Planet))
                        {
                            //Checks if energy is needed
                            xBuildable = Helpers.GetNextEnergySourceToBuild(tempCelestial as Planet, (int)settings.Brain.AutoMine.MaxSolarPlant, (int)settings.Brain.AutoMine.MaxFusionReactor);
                            nLevelToReach = Helpers.GetNextLevel(tempCelestial as Planet, xBuildable);
                        }
                        tempCelestial = UpdatePlanet(tempCelestial, UpdateType.Facilities);
                        if (xBuildable == Buildables.Null && Helpers.ShouldBuildNanites(tempCelestial as Planet, (int)settings.Brain.AutoMine.MaxNaniteFactory))
                        {
                            //Manage the need of nanites
                            xBuildable = Buildables.NaniteFactory;
                            nLevelToReach = Helpers.GetNextLevel(tempCelestial as Planet, xBuildable);
                        }
                        if (xBuildable == Buildables.Null && Helpers.ShouldBuildRoboticFactory(tempCelestial as Planet, (int)settings.Brain.AutoMine.MaxRoboticsFactory))
                        {
                            //Manage the need of robotics factory
                            xBuildable = Buildables.RoboticsFactory;
                            nLevelToReach = Helpers.GetNextLevel(tempCelestial as Planet, xBuildable);
                        }
                        if (xBuildable == Buildables.Null && Helpers.ShouldBuildShipyard(tempCelestial as Planet, (int)settings.Brain.AutoMine.MaxShipyard))
                        {
                            //Manage the need of shipyard
                            xBuildable = Buildables.Shipyard;
                            nLevelToReach = Helpers.GetNextLevel(tempCelestial as Planet, xBuildable);
                        }
                        if (xBuildable == Buildables.Null)
                        {
                            //Manage the need of build some deposit
                            mHandleDeposit(tempCelestial, ref xBuildable, ref nLevelToReach);
                        }
                        //If it isn't needed to build deposit
                        //check if it needs to build some mines 
                        if (xBuildable == Buildables.Null)
                        {
                            mHandleMines(tempCelestial, ref xBuildable, ref nLevelToReach);
                        }

                        if (xBuildable != Buildables.Null && nLevelToReach > 0)
                            mHandleBuildCelestialBuild(tempCelestial, xBuildable, nLevelToReach);
                    }
                    else
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping moon " + tempCelestial.ToString());
                    }
                    newCelestials.Remove(xCelestial);
                    newCelestials.Add(tempCelestial);
                    xBuildable = Buildables.Null;
                    nLevelToReach = 0;
                }
                celestials = newCelestials;
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Brain, "AutoMine Exception: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
            finally
            {                
                var time = GetDateTime();
                //celestials = UpdatePlanets(UpdateType.Constructions);
                //var nextTimeToCompletion = celestials.Min(celestial => celestial.Constructions.BuildingCountdown) * 1000;
                //var interval = nextTimeToCompletion + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
                var interval = Helpers.CalcRandomInterval((int)settings.Brain.AutoMine.CheckIntervalMin, (int)settings.Brain.AutoMine.CheckIntervalMax);
                var newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("AutoMineTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next AutoMine check at " + newTime.ToString());
                UpdateTitle();
                //Release its semaphore
                xaSem[Feature.BrainAutoMine].Release();
            }
        }


        /*Lorenzo 06/02/2021
         * 
         * Method that allows bot to check if there is the need to build some storage
         * 
         * IN PARAMETER:
         * 
         * - Celestial xCelestial: Celestial object used when the method need to know the range level of metal mine
         * - ref Buildables xBuildable: Buildables object passed by reference to allow the method to set a specific type of building
         *                              that needs to be built
         * - ref int nLevelToReach: integer passed by reference to allow the method to set the building level that needs to be build
         * 
         * 
         * OUT PARAMETER
         * 
         * NaN
         */
        private static void mHandleDeposit(Celestial xCelestial, ref Buildables xBuildable, ref int nLevelToReach)
        {
            try
            {
                //Check if it is necessary to build a Deuterium tank
                if (xBuildable == Buildables.Null && Helpers.ShouldBuildDeuteriumTank((Planet)xCelestial, (int)settings.Brain.AutoMine.MaxDeuteriumTank, (int)settings.Brain.AutoMine.DepositHours, serverData.Speed, 1, researches, userInfo.Class))
                {
                    //Yes, need it

                    //Set the type of building to build
                    xBuildable = Buildables.DeuteriumTank;
                    //Set the level
                    nLevelToReach = Helpers.GetNextLevel(xCelestial as Planet, xBuildable);
                }


                //Check if it is necessary to build a Crystal storage
                if (xBuildable == Buildables.Null && Helpers.ShouldBuildCrystalStorage((Planet)xCelestial, (int)settings.Brain.AutoMine.MaxCrystalStorage, (int)settings.Brain.AutoMine.DepositHours, serverData.Speed, 1, researches, userInfo.Class))
                {
                    //Yes, need it

                    //Set the type of building to build
                    xBuildable = Buildables.CrystalStorage;
                    //Set the level
                    nLevelToReach = Helpers.GetNextLevel(xCelestial as Planet, xBuildable);
                }

                //Check if it is necessary to build a Metal storage
                if (xBuildable == Buildables.Null && Helpers.ShouldBuildMetalStorage((Planet)xCelestial, (int)settings.Brain.AutoMine.MaxMetalStorage, (int)settings.Brain.AutoMine.DepositHours, serverData.Speed, 1, researches, userInfo.Class))
                {
                    //Yes, need it

                    //Set the type of building to build
                    xBuildable = Buildables.MetalStorage;
                    //Set the level
                    nLevelToReach = Helpers.GetNextLevel(xCelestial as Planet, xBuildable);
                }



            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Brain, "mHandleDeposit Exception: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
        }
        /*Lorenzo 06/02/2021
         * 
         * Method that allows bot to check if there is the need to build some mines
         * 
         * IN PARAMETER:
         * 
         * - Celestial xCelestial: Celestial object used when the method need to know the range level of metal mine
         * - ref Buildables xBuildable: Buildables object passed by reference to allow the method to set a specific type of building
         *                              that needs to be built
         * - ref int nLevelToReach: integer passed by reference to allow the method to set the building level that needs to be build
         * 
         * 
         * OUT PARAMETER
         * 
         * NaN
         */
        private static void mHandleMines(Celestial xCelestial, ref Buildables xBuildable, ref int nLevelToReach)
        {
            try
            {
                xBuildable = Helpers.GetNextMineToBuild(xCelestial as Planet, (int)settings.Brain.AutoMine.MaxMetalMine, (int)settings.Brain.AutoMine.MaxCrystalMine, (int)settings.Brain.AutoMine.MaxDeuteriumSynthetizer);
                if (xBuildable != Buildables.Null)
                    nLevelToReach = Helpers.GetNextLevel(xCelestial as Planet, xBuildable);
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Brain, "mHandleMines Exception: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
        }

        /*Lorenzo 06/02/2021
         * 
         * Method to manage the possibility of build some mine if required.
         * 
         * IN PARAMETERS:
         * 
         * - Celestial xCelestial: Celestial object used when the method need to know the next mine level cost.
         * - ref bool bBuildMetalMine: bool that indicates the request for build a Metal mine. When the request is managed it will return to false
         * - ref bool bBuildCrystalMine: bool that indicates the request for build a Crystal mine. When the request is managed it will return to false
         * - ref bool bBuildDeutMine: bool that indicates the request for build a Deuteryum mine. When the request is managed it will return to false
         * 
         * OUT PARAMETERS:
         * 
         * void
         */
        private static void mHandleBuildCelestialBuild(Celestial xCelestial, Buildables xBuildableToBuild, int nLevelToBuild)
        {
            try
            {
                Resources xCostBuildable = Helpers.CalcPrice(xBuildableToBuild, nLevelToBuild);

                if (xCelestial.Resources.IsEnoughFor(xCostBuildable))
                {
                    bool result = false;
                    //Yes, i can build it
                    if (xBuildableToBuild == Buildables.SolarSatellite)
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building " + nLevelToBuild.ToString() + "x " + xBuildableToBuild.ToString() + " on " + xCelestial.ToString());
                        result = ogamedService.BuildShips(xCelestial, xBuildableToBuild, nLevelToBuild);
                    }                        
                    else
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building " + xBuildableToBuild.ToString() + " level " + nLevelToBuild.ToString() + " on " + xCelestial.ToString());
                        result = ogamedService.BuildConstruction(xCelestial, xBuildableToBuild);                        
                    }
                        
                    if (result)
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building succesfully started.");
                    else
                        Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to start building construction.");
                }
                else
                {
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Not enough resources to build: " + xBuildableToBuild.ToString() + " level " + nLevelToBuild.ToString() + " on " + xCelestial.ToString());
                }
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Brain, "mHandleBuildCelestialMines Exception: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
        }

        private static void AutoBuildCargo(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[Feature.BrainAutobuildCargo].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Checking capacity...");
                celestials = UpdatePlanets(UpdateType.Ships);
                celestials = UpdatePlanets(UpdateType.Resources);
                celestials = UpdatePlanets(UpdateType.Productions);
                foreach (Celestial planet in celestials)
                {
                    var capacity = Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class);
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Celestial " + planet.ToString() + ": Available capacity: " + capacity.ToString("N0") + " - Resources: " + planet.Resources.TotalResources.ToString("N0"));
                    if (planet.Coordinate.Type == Celestials.Moon && (bool)settings.Brain.AutoCargo.ExcludeMoons)
                    {
                        Helpers.WriteLog(LogType.Debug, LogSender.Brain, "Celestial " + planet.ToString() + " is a moon - Skipping moon.");
                        continue;
                    }
                    if (capacity <= planet.Resources.TotalResources)
                    {
                        long difference = planet.Resources.TotalResources - capacity;
                        Buildables preferredCargoShip = Buildables.SmallCargo;
                        Enum.TryParse<Buildables>((string)settings.Brain.AutoCargo.CargoType, true, out preferredCargoShip);
                        int oneShipCapacity = Helpers.CalcShipCapacity(preferredCargoShip, researches.HyperspaceTechnology, userInfo.Class);
                        long neededCargos = (long)Math.Round((float)difference / (float)oneShipCapacity, MidpointRounding.ToPositiveInfinity);
                        Helpers.WriteLog(LogType.Debug, LogSender.Brain, difference.ToString("N0") + " more capacity is needed, " + neededCargos + " more " + preferredCargoShip.ToString() + " are needed.");
                        if (planet.HasProduction())
                        {
                            Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Celestial " + planet.ToString() + " - There is already a production ongoing. Skipping planet.");
                            foreach (Production production in planet.Productions)
                            {
                                Buildables productionType = (Buildables)production.ID;
                                Helpers.WriteLog(LogType.Debug, LogSender.Brain, "Celestial " + planet.ToString() + " - " + production.Nbr + "x" + productionType.ToString() + " are already in production.");
                            }
                            continue;
                        }

                        /*Tralla 14/2/21
                         * 
                         * Add settings to provide a better autocargo configurability
                         */
                        if (neededCargos > (long)settings.Brain.AutoCargo.MaxCargosToBuild)
                            neededCargos = (long)settings.Brain.AutoCargo.MaxCargosToBuild;

                        if (planet.Ships.GetAmount(preferredCargoShip) + neededCargos > (long)settings.Brain.AutoCargo.MaxCargosToKeep)
                            neededCargos = (long)settings.Brain.AutoCargo.MaxCargosToKeep - planet.Ships.GetAmount(preferredCargoShip);

                        var cost = ogamedService.GetPrice(preferredCargoShip, neededCargos);
                        if (planet.Resources.IsEnoughFor(cost))
                        {
                            Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building " + neededCargos + "x" + preferredCargoShip.ToString());
                            var result = ogamedService.BuildShips(planet, preferredCargoShip, neededCargos);
                            if (result)
                                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building succesfully started.");
                            else
                                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to start ship construction.");
                        }
                        else
                        {
                            Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Celestial " + planet.ToString() + " - Not enough resources to build " + neededCargos + "x" + preferredCargoShip.ToString());
                            var result = ogamedService.BuildShips(planet, preferredCargoShip, neededCargos);
                            if (result)
                                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building succesfully started.");
                            else
                                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to start ship construction.");
                        }
                        planet.Productions = ogamedService.GetProductions(planet);
                        foreach (Production production in planet.Productions)
                        {
                            Buildables productionType = (Buildables)production.ID;
                            Helpers.WriteLog(LogType.Info, LogSender.Brain, "Celestial " + planet.ToString() + " - " + production.Nbr + "x" + productionType.ToString() + " are in production.");
                        }
                    }
                    else
                    {
                        Helpers.WriteLog(LogType.Debug, LogSender.Brain, "Celestial " + planet.ToString() + " - Capacity is ok.");
                    }
                }
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Brain, "Unable to complete autocargo: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
            finally
            {
                var time = GetDateTime();
                var interval = Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax);
                var newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("CapacityTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next capacity check at " + newTime.ToString());
                UpdateTitle();
                //Release its semaphore
                xaSem[Feature.BrainAutobuildCargo].Release();
            }
        }


        private static void AutoRepatriate(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[Feature.BrainAutoRepatriate].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Repatriating resources...");
                celestials = UpdatePlanets(UpdateType.Ships);
                celestials = UpdatePlanets(UpdateType.Resources);

                var rand = new Random();
                foreach (Celestial celestial in settings.Brain.AutoRepatriate.RandomOrder ? celestials.OrderBy(celestial => rand.Next()).ToList() : celestials)
                {
                    if (celestial.Coordinate.Type == Celestials.Moon && settings.Brain.AutoRepatriate.ExcludeMoons)
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping moon.");
                        continue;
                    }
                    if (celestial.Resources.TotalResources < (int)settings.Brain.AutoRepatriate.MinimumResources)
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping celestial: resources under set limit");
                        continue;
                    }
                    Buildables preferredShip = Buildables.SmallCargo;
                    Enum.TryParse<Buildables>((string)settings.Brain.AutoCargo.CargoType, true, out preferredShip);
                    long idealShips = Helpers.CalcShipNumberForPayload(celestial.Resources, preferredShip, researches.HyperspaceTechnology, userInfo.Class);
                    Ships ships = new();
                    if (idealShips <= celestial.Ships.GetAmount(preferredShip))
                    {
                        ships.Add(preferredShip, idealShips);
                    }
                    else
                    {
                        ships.Add(preferredShip, celestial.Ships.GetAmount(preferredShip));
                    }
                    Resources payload = Helpers.CalcMaxTransportableResources(ships, celestial.Resources, researches.HyperspaceTechnology, userInfo.Class, settings.Brain.AutoRepatriate.DeutToLeave);
                    Celestial destination = celestials
                                .OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
                                .OrderByDescending(planet => Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class))
                                .First();
                    if (settings.Brain.AutoRepatriate.Target)
                    {
                        try
                        {
                            Celestial customDestination = celestials
                                .Where(planet => planet.Coordinate.Galaxy == (int)settings.Brain.AutoRepatriate.Target.Galaxy)
                                .Where(planet => planet.Coordinate.System == (int)settings.Brain.AutoRepatriate.Target.System)
                                .Where(planet => planet.Coordinate.Position == (int)settings.Brain.AutoRepatriate.Target.Position)
                                .Where(planet => planet.Coordinate.Type == Enum.Parse<Celestials>((string)settings.Brain.AutoRepatriate.Target.Type))
                                .Single();
                            destination = customDestination;
                            Coordinate destinationCoordinate = destination.Coordinate;
                            if (celestial.ID == customDestination.ID)
                            {
                                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping celestial: this celestial is the destination");
                                continue;
                            }
                        }
                        catch (Exception e)
                        {
                            try
                            {
                                Coordinate destinationCoordinate = Coordinate((int)settings.Brain.AutoRepatriate.Target.Galaxy,
                                                                              (int)settings.Brain.AutoRepatriate.Target.System,
                                                                              (int)settings.Brain.AutoRepatriate.Target.Position,
                                                                              Enum.Parse<Celestials>((string)settings.Brain.AutoRepatriate.Target.Type));
                            }
                            catch (Exception e)
                            {
                                Helpers.WriteLog(LogType.Debug, LogSender.Brain, "Exception: " + e.Message);
                                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to parse custom destination");
                            }
                        }
                    }
                    SendFleet(celestial, ships, destinationCoordinate, Missions.Transport, Speeds.HundredPercent, payload);
                }
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to complete repatriate: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
            }
            finally
            {                
                var time = GetDateTime();
                var interval = Helpers.CalcRandomInterval((int)settings.Brain.AutoRepatriate.CheckIntervalMin, (int)settings.Brain.AutoRepatriate.CheckIntervalMax);
                var newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next repatriate check at " + newTime.ToString());
                UpdateTitle();
                //Release its semaphore
                xaSem[Feature.BrainAutoRepatriate].Release();
            }
        }

        private static int SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, decimal speed, Model.Resources payload = null, Classes playerClass = Classes.NoClass, bool force = false)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Sending fleet from " + origin.Coordinate.ToString() + " to " + destination.ToString() + ". Mission: " + mission.ToString() + ". Speed: " + (speed * 10).ToString() + "% . Ships: " + ships.ToString());
            if (playerClass == Classes.NoClass)
                playerClass = userInfo.Class;

            if (
                playerClass != Classes.General && (
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
            )
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "Unable to send fleet, speed not available for your class");
                return 0;
            }

            slots = UpdateSlots();
            if (slots.Free > 1 || force)
            {
                if (payload == null)
                {
                    payload = new Resources { Metal = 0, Crystal = 0, Deuterium = 0 };
                }
                try
                {
                    Fleet fleet = ogamedService.SendFleet(origin, ships, destination, mission, speed, payload);
                    fleets = ogamedService.GetFleets();
                    slots = UpdateSlots();
                    return fleet.ID;
                }
                catch (Exception e)
                {
                    Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to send fleet: an exception has occurred: " + e.Message);
                    Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                    return 0;
                }
            }
            else
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "Unable to send fleet, no slots available");
                return 0;
            }
        }

        private static bool CancelFleet(Fleet fleet)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Recalling fleet id " + fleet.ID + " originally from " + fleet.Origin.ToString() + " to " + fleet.Destination.ToString() + " with mission: " + fleet.Mission.ToString() + ". Start time: " + fleet.StartTime.ToString() + " - Arrival time: " + fleet.ArrivalTime.ToString() + " - Ships: " + fleet.Ships.ToString());
            slots = UpdateSlots();
            try
            {
                var result = ogamedService.CancelFleet(fleet);
                if (result)
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Fleet succesfully recalled.");
                else
                    Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to recall fleet.");
                Thread.Sleep((int)IntervalType.AFewSeconds);
                fleets = UpdateFleets();
                var recalledFleet = fleets.SingleOrDefault(f => f.ID == fleet.ID);
                if (recalledFleet.ID == 0)
                {
                    Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to recall fleet: an unknon error has occurred.");
                    if ((bool)settings.TelegramMessenger.Active && (bool)settings.Defender.TelegramMessenger.Active)
                    {
                        telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Unable to recall fleet: an unknon error has occurred.");
                    }
                }
                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Fleet recalled: return time: " + fleet.BackTime.ToString());
                if ((bool)settings.TelegramMessenger.Active && (bool)settings.Defender.TelegramMessenger.Active)
                {
                    telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Fleet recalled: return time: " + fleet.BackTime.ToString());
                }
                return result;
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to recall fleet: an exception has occurred: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                if ((bool)settings.TelegramMessenger.Active && (bool)settings.Defender.TelegramMessenger.Active)
                {
                    telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Unable to recall fleet: an exception has occurred.");
                }
                return false;
            }
        }

        private static void RetireFleet(object state)
        {
            CancelFleet((Fleet)state);
        }

        private static void HandleAttack(object state)
        {
            if (celestials.Count == 0)
            {
                DateTime time = GetDateTime();
                int interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
                DateTime newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Unable to handle attack at the moment: bot is still getting account info.");
                Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                return;
            }
            foreach (AttackerFleet attack in attacks)
            {
                if (attack.IsOnlyProbes())
                {
                    if (attack.MissionType == Missions.Spy)
                        Helpers.WriteLog(LogType.Info, LogSender.Defender, "Espionage action skipped.");
                    else
                        Helpers.WriteLog(LogType.Info, LogSender.Defender, "Attack " + attack.ID.ToString() + " skipped: only Espionage Probes.");

                    continue;
                }
                if ((bool)settings.TelegramMessenger.Active && (bool)settings.Defender.TelegramMessenger.Active)
                {
                    telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Player " + attack.AttackerName + " (" + attack.AttackerID + ") is attacking your planet " + attack.Destination.ToString() + " arriving at " + attack.ArrivalTime.ToString());
                }
                Celestial attackedCelestial = celestials.SingleOrDefault(planet => planet.Coordinate.Galaxy == attack.Destination.Galaxy && planet.Coordinate.System == attack.Destination.System && planet.Coordinate.Position == attack.Destination.Position && planet.Coordinate.Type == attack.Destination.Type);
                attackedCelestial = UpdatePlanet(attackedCelestial, UpdateType.Ships);
                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Player " + attack.AttackerName + " (" + attack.AttackerID + ") is attacking your planet " + attackedCelestial.ToString() + " arriving at " + attack.ArrivalTime.ToString());
                if ((bool)settings.Defender.SpyAttacker.Active)
                {
                    slots = UpdateSlots();
                    if (slots.Free > 0)
                    {
                        if (attackedCelestial.Ships.EspionageProbe == 0)
                        {
                            Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not spy attacker: no probes available.");
                        }
                        else
                        {
                            try
                            {
                                Coordinate destination = attack.Origin;
                                Ships ships = new() { EspionageProbe = (int)settings.Defender.SpyAttacker.Probes };
                                int fleetId = SendFleet(attackedCelestial, ships, destination, Missions.Spy, Speeds.HundredPercent, new Resources(), userInfo.Class);
                                Fleet fleet = fleets.Single(fleet => fleet.ID == fleetId);
                                Helpers.WriteLog(LogType.Info, LogSender.Defender, "Spying attacker from " + attackedCelestial.ToString() + " to " + destination.ToString() + " with " + settings.Defender.SpyAttacker.Probes + " probes. Arrival at " + fleet.ArrivalTime.ToString());
                            }
                            catch (Exception e)
                            {
                                Helpers.WriteLog(LogType.Error, LogSender.Defender, "Could not spy attacker: an exception has occurred: " + e.Message);
                                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                            }
                        }

                    }
                    else
                    {
                        Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not send probes: no slots available.");
                    }
                }
                if ((bool)settings.Defender.MessageAttacker.Active)
                {
                    try
                    {
                        Random random = new();
                        string[] messages = settings.Defender.MessageAttacker.Messages;
                        string message = messages.ToList().Shuffle().First();
                        Helpers.WriteLog(LogType.Info, LogSender.Defender, "Sending message \"" + message + "\" to attacker" + attack.AttackerName);
                        var result = ogamedService.SendMessage(attack.AttackerID, message);
                        if (result)
                            Helpers.WriteLog(LogType.Info, LogSender.Brain, "Message succesfully sent.");
                        else
                            Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable send message.");
                        
                    }
                    catch (Exception e)
                    {
                        Helpers.WriteLog(LogType.Error, LogSender.Defender, "Could not message attacker: an exception has occurred: " + e.Message);
                        Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                    }
                }
                if ((bool)settings.Defender.Autofleet.Active)
                {
                    slots = UpdateSlots();
                    if (slots.Free > 0)
                    {
                        try
                        {
                            attackedCelestial = UpdatePlanet(attackedCelestial, UpdateType.Resources);
                            Celestial destination;
                            Missions mission = Missions.Deploy;
                            destination = celestials
                                .Where(planet => planet.ID != attackedCelestial.ID)
                                .Where(planet => planet.Coordinate.Type == (attackedCelestial.Coordinate.Type == Celestials.Moon ? Celestials.Planet : Celestials.Moon))
                                .Where(planet => Helpers.CalcDistance(attackedCelestial.Coordinate, planet.Coordinate, serverData) == 5)
                                .FirstOrDefault() ?? new Celestial { ID = 0 };
                            if (destination.ID == 0)
                            {
                                destination = celestials
                                    .Where(planet => planet.ID != attackedCelestial.ID)
                                    .Where(planet => planet.Coordinate.Type == Celestials.Moon)
                                    .OrderBy(planet => Helpers.CalcDistance(attackedCelestial.Coordinate, planet.Coordinate, serverData))
                                    .FirstOrDefault() ?? new Celestial { ID = 0 };
                            }
                            if (destination.ID == 0)
                            {
                                destination = celestials
                                    .Where(planet => planet.ID != attackedCelestial.ID)
                                    .OrderBy(planet => Helpers.CalcDistance(attackedCelestial.Coordinate, planet.Coordinate, serverData))
                                    .FirstOrDefault() ?? new Celestial { ID = 0 };
                            }
                            if (destination.ID == 0)
                            {
                                destination.ID = -99;
                                destination.Coordinate = new Coordinate();
                                mission = Missions.Transport;
                            }

                            if (destination.ID == 0)
                            {
                                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not fleetsave: no valid destination exists");
                                DateTime time = GetDateTime();
                                int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                                DateTime newTime = time.AddMilliseconds(interval);
                                timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
                                Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                                return;
                            }

                            Ships ships = attackedCelestial.Ships;
                            ships.Crawler = 0;
                            ships.SolarSatellite = 0;
                            if (ships.IsEmpty())
                            {
                                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not fleetsave: there is no fleet.");
                            }
                            else
                            {
                                Resources resources = Helpers.CalcMaxTransportableResources(ships, attackedCelestial.Resources, researches.HyperspaceTechnology, userInfo.Class);
                                int fleetId = SendFleet(attackedCelestial, ships, destination.Coordinate, mission, Speeds.TenPercent, resources, userInfo.Class, true);
                                if (fleetId != 0)
                                {
                                    Fleet fleet = fleets.Single(fleet => fleet.ID == fleetId);
                                    Helpers.WriteLog(LogType.Info, LogSender.Defender, "Fleetsaved to " + destination.ToString() + ". Arrival at " + fleet.ArrivalTime.ToString());
                                    if ((bool)settings.TelegramMessenger.Active && (bool)settings.Defender.Autofleet.TelegramMessenger.Active)
                                    {
                                        telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] Fleetsaved to " + destination.ToString() + ". Arrival at " + fleet.ArrivalTime.ToString());
                                    }
                                    if ((bool)settings.Defender.Autofleet.Recall)
                                    {
                                        DateTime time = GetDateTime();
                                        var interval = (((attack.ArriveIn * 1000) + (((attack.ArriveIn * 1000) / 100) * 30)) / 2) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
                                        DateTime newTime = time.AddMilliseconds(interval);
                                        timers.Add(attack.ID.ToString(), new Timer(RetireFleet, fleet, interval, Timeout.Infinite));
                                        Helpers.WriteLog(LogType.Info, LogSender.Defender, "The fleet will be recalled at " + newTime.ToString());
                                    }
                                }
                                else
                                {
                                    Helpers.WriteLog(LogType.Error, LogSender.Defender, "Could not fleetsave: an unknown error has occurred.");
                                    DateTime time = GetDateTime();
                                    int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                                    DateTime newTime = time.AddMilliseconds(interval);
                                    timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
                                    Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Helpers.WriteLog(LogType.Error, LogSender.Defender, "Could not fleetsave: an exception has occurred: " + e.Message);
                            Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                            DateTime time = GetDateTime();
                            int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                            DateTime newTime = time.AddMilliseconds(interval);
                            timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
                            Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                        }
                    }
                    else
                    {
                        Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not fleetsave: no slots available.");
                        DateTime time = GetDateTime();
                        int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                        DateTime newTime = time.AddMilliseconds(interval);
                        timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
                        Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                    }
                }
            }
        }

        private static void HandleExpeditions(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[Feature.Expeditions].WaitOne();

                if ((bool)settings.Expeditions.AutoSendExpeditions.Active)
                {
                    slots = UpdateSlots();
                    fleets = UpdateFleets();
                    int expsToSend;
                    if ((bool)settings.Expeditions.AutoSendExpeditions.WaitForAllExpeditions)
                    {
                        if (slots.ExpInUse == 0)
                            expsToSend = slots.ExpTotal;
                        else
                            expsToSend = 0;
                    }
                    else
                    {
                        expsToSend = Math.Min(slots.ExpFree, slots.Free);
                    }

                    if (expsToSend > 0)
                    {
                        if (slots.ExpFree > 0)
                        {
                            if (slots.Free > 0)
                            {
                                List<Celestial> origins = new();
                                if (settings.Expeditions.AutoSendExpeditions.Origin.Length > 0)
                                {
                                    try
                                    {
                                        foreach (var origin in settings.Expeditions.AutoSendExpeditions.Origin)
                                        {
                                            Coordinate customOriginCoords = new(
                                                (int)origin.Galaxy,
                                                (int)origin.System,
                                                (int)origin.Position,
                                                Enum.Parse<Celestials>(origin.Type.ToString())
                                            );
                                            Celestial customOrigin = celestials
                                                .Single(planet => planet.HasCoords(customOriginCoords));
                                            customOrigin = UpdatePlanet(customOrigin, UpdateType.Ships);
                                            origins.Add(customOrigin);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Helpers.WriteLog(LogType.Debug, LogSender.Expeditions, "Exception: " + e.Message);
                                        Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                                        Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to parse custom origin");

                                        celestials = UpdatePlanets(UpdateType.Ships);
                                        origins.Add(celestials
                                            .OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
                                            .OrderByDescending(planet => Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class))
                                            .First()
                                        );
                                    }
                                }
                                else
                                {
                                    celestials = UpdatePlanets(UpdateType.Ships);
                                    origins.Add(celestials
                                        .OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
                                        .OrderByDescending(planet => Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class))
                                        .First()
                                    );
                                }
                                if ((bool)settings.Expeditions.AutoSendExpeditions.RandomizeOrder)
                                {
                                    origins = origins.Shuffle().ToList();
                                }                                
                                foreach (var origin in origins)
                                {
                                    int expsToSendFromThisOrigin;
                                    if (origins.Count >= expsToSend)
                                    {
                                        expsToSendFromThisOrigin = 1;
                                    }
                                    else
                                    {
                                        expsToSendFromThisOrigin = (int)Math.Round((float)expsToSend / (float)origins.Count, MidpointRounding.ToZero);
                                        if (origin == origins.Last())
                                        {
                                            expsToSendFromThisOrigin = (int)Math.Round((float)expsToSend / (float)origins.Count, MidpointRounding.ToZero) + (expsToSend % origins.Count);
                                        }
                                    } 
                                    if (origin.Ships.IsEmpty())
                                    {
                                        Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no ships available");
                                        continue;
                                    }
                                    else
                                    {
                                        Buildables mainShip = Enum.Parse<Buildables>(settings.Expeditions.AutoSendExpeditions.MainShip.ToString() ?? "LargeCargo") ?? Buildables.LargeCargo;
                                        Ships fleet = Helpers.CalcFullExpeditionShips(origin.Ships, mainShip, expsToSendFromThisOrigin, serverData, researches, userInfo.Class);
                                        if (fleet.GetAmount(mainShip) < (long)settings.Expeditions.AutoSendExpeditions.MinCargosToSend)
                                        {
                                            Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: cargos under set number");
                                            continue;
                                        }

                                        Helpers.WriteLog(LogType.Info, LogSender.Expeditions, expsToSendFromThisOrigin.ToString() + " expeditions with " + fleet.ToString() + " will be sent from " + origin.ToString());
                                        for (int i = 0; i < expsToSendFromThisOrigin; i++)
                                        {
                                            Coordinate destination;
                                            if (settings.Expeditions.AutoSendExpeditions.SplitExpeditionsBetweenSystems)
                                            {
                                                var rand = new Random();

                                                destination = new Coordinate
                                                {
                                                    Galaxy = origin.Coordinate.Galaxy,
                                                    System = rand.Next(origin.Coordinate.System - 1, origin.Coordinate.System + 2),
                                                    Position = 16,
                                                    Type = Celestials.DeepSpace
                                                };
                                            }
                                            else
                                            {
                                                destination = new Coordinate
                                                {
                                                    Galaxy = origin.Coordinate.Galaxy,
                                                    System = origin.Coordinate.System,
                                                    Position = 16,
                                                    Type = Celestials.DeepSpace
                                                };
                                            }
                                            slots = UpdateSlots();
                                            if (slots.ExpFree > 0)
                                            {
                                                SendFleet(origin, fleet, destination, Missions.Expedition, Speeds.HundredPercent);
                                                Thread.Sleep((int)IntervalType.AFewSeconds);
                                            }
                                            else
                                            {
                                                Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Unable to send expeditions: no expedition slots available.");
                                                break;
                                            }                                            
                                        }
                                    }
                                }                                
                            }
                            else
                            {
                                Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no fleet slots available");
                            }
                        }
                        else
                        {
                            Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no expeditions slots available");
                        }
                    }

                    slots = UpdateSlots();
                    fleets = UpdateFleets();

                    List<Fleet> orderedFleets = fleets
                        .Where(fleet => fleet.Mission == Missions.Expedition)
                        .ToList();
                    if ((bool)settings.Expeditions.AutoSendExpeditions.WaitForAllExpeditions)
                    {
                        orderedFleets = orderedFleets
                            .OrderByDescending(fleet => fleet.BackIn)
                            .ToList();
                    }
                    else
                    {
                        orderedFleets = orderedFleets
                            .OrderBy(fleet => fleet.BackIn)
                            .ToList();
                    }
                    int interval;
                    if (orderedFleets.Count == 0 || slots.ExpFree > 0)
                    {
                        interval = Helpers.CalcRandomInterval(IntervalType.AboutFiveMinutes);
                    }
                    else
                    {
                        interval = (int)((1000 * orderedFleets.First().BackIn) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo));
                    }
                    var time = GetDateTime();
                    DateTime newTime = time.AddMilliseconds(interval);
                    timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
                    Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Next check at " + newTime.ToString());
                    UpdateTitle();
                }

                /*
                if ((bool)settings.Expeditions.AutoHarvest.Active)
                {
                    slots = UpdateSlots();
                    fleets = UpdateFleets();
                    Celestial origin = celestials
                        .OrderBy(planet => planet.Coordinate.Type == Celestials.Moon)
                        .OrderByDescending(planet => Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class))
                        .First();
                    if (settings.Expeditions.AutoSendExpeditions.Origin)
                    {
                        try
                        {
                            Celestial customOrigin = celestials
                                .Where(planet => planet.Coordinate.Galaxy == (int)settings.Expeditions.AutoSendExpeditions.Origin.Galaxy)
                                .Where(planet => planet.Coordinate.System == (int)settings.Expeditions.AutoSendExpeditions.Origin.System)
                                .Where(planet => planet.Coordinate.Position == (int)settings.Expeditions.AutoSendExpeditions.Origin.Position)
                                .Where(planet => planet.Coordinate.Type == Enum.Parse<Celestials>(settings.Expeditions.AutoSendExpeditions.Origin.Type.ToString()))
                                .Single();
                            origin = customOrigin;
                        }
                        catch (Exception e)
                        {
                            Helpers.WriteLog(LogType.Debug, LogSender.Expeditions, "Exception: " + e.Message);
                            Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to parse custom origin");
                        }
                    }
                    List<Coordinate> destinations = new List<Coordinate>();
                    if (settings.Expeditions.AutoSendExpeditions.SplitExpeditionsBetweenSystems)
                    {
                        var rand = new Random();
                        for (int i = origin.Coordinate.System - 1; i < origin.Coordinate.System + 2; i++)
                        {
                            destinations.Add(new Coordinate
                            {
                                Galaxy = origin.Coordinate.Galaxy,
                                System = rand.Next(i, i + 1),
                                Position = 16,
                                Type = Celestials.DeepSpace
                            });
                        }

                    }
                    else
                    {
                        destinations.Add(new Coordinate
                        {
                            Galaxy = origin.Coordinate.Galaxy,
                            System = origin.Coordinate.System,
                            Position = 16,
                            Type = Celestials.DeepSpace
                        });
                    }
                    foreach (Coordinate destination in destinations)
                    {
                        var galaxyInfos = ogamedService.GetGalaxyInfo(destination);
                        if (galaxyInfos.ExpeditionDebris.Resources.TotalResources > 0)
                        {
                            Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Debris detected at " + destination.ToString());
                            if (galaxyInfos.ExpeditionDebris.Resources.TotalResources >= settings.Expeditions.AutoHarvest.MinimumResources)
                            {
                                long pathfindersToSend = Helpers.CalcShipNumberForPayload(galaxyInfos.ExpeditionDebris.Resources, Buildables.Pathfinder, researches.HyperspaceTechnology, userInfo.Class);
                                SendFleet(origin, new Ships { Pathfinder = pathfindersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
                            }
                            else
                            {
                                Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Skipping harvest: resources under set limit.");
                            }
                        }
                    }
                }
                */
            }

            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "HandleExpeditions exception: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Stacktrace: " + e.StackTrace);
                int interval = (int)(Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo));
                var time = GetDateTime();
                DateTime newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("ExpeditionsTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Next check at " + newTime.ToString());
                UpdateTitle();
            }
            finally
            {
                //Release its semaphore
                xaSem[Feature.Expeditions].Release();
            }

        }

        private static void HandleHarvest(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[Feature.Harvest].WaitOne();

                if ((bool)settings.AutoHarvest.Active)
                {
                    Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Detecting harvest targets");

                    galaxyInfos = UpdateGalaxyInfos();
                    celestials = UpdatePlanets(UpdateType.Ships);
                    var dic = new Dictionary<Coordinate, Celestial>();

                    foreach (Planet planet in celestials.Where(c => c is Planet))
                    {
                        Moon moon = new()
                        {
                            Ships = new()
                        };

                        bool hasMoon = celestials.Count(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) == 1;                        
                        if (hasMoon)
                        {
                            moon = celestials.Single(c => c.HasCoords(new Coordinate(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Moon))) as Moon;
                        }

                        if ((bool)settings.AutoHarvest.HarvestOwnDF)
                        {
                            Coordinate dest = new(planet.Coordinate.Galaxy, planet.Coordinate.System, planet.Coordinate.Position, Celestials.Debris);
                            if (planet.Debris.Resources.TotalResources >= settings.AutoHarvest.MinimumResources)
                            {                                
                                if (moon.Ships.Recycler >= planet.Debris.RecyclersNeeded)
                                    dic.Add(dest, moon);
                                else if (planet.Ships.Recycler >= planet.Debris.RecyclersNeeded)
                                    dic.Add(dest, planet);
                                else
                                    Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Skipping harvest in " + dest.ToString() + ": not enough recyclers.");
                            }
                            else if (planet.Debris.Resources.TotalResources == 0)
                            {
                                Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Skipping harvest in " + dest.ToString() + ": there are no debris");
                            }
                            else
                            {
                                Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Skipping harvest in " + dest.ToString() + ": resources under set limit.");
                            }
                        }

                        if ((bool)settings.AutoHarvest.HarvestDeepSpace)
                        {
                            ExpeditionDebris expoDebris = ogamedService.GetGalaxyInfo(planet.Coordinate).ExpeditionDebris;
                            Coordinate dest = new(planet.Coordinate.Galaxy, planet.Coordinate.System, 16, Celestials.DeepSpace);
                            if (expoDebris.Resources.TotalResources >= settings.AutoHarvest.MinimumResources)
                            {                                
                                if (moon.Ships.Pathfinder >= expoDebris.PathfindersNeeded)
                                    dic.Add(dest, moon);
                                else if (planet.Ships.Pathfinder >= expoDebris.PathfindersNeeded)
                                    dic.Add(dest, planet);
                                else
                                    Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Skipping harvest in " + dest.ToString() + ": not enough pathfinders.");
                            }
                            else if (expoDebris.Resources.TotalResources == 0)
                            {
                                Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Skipping harvest in " + dest.ToString() + ": there are no debris");
                            }
                            else
                            {
                                Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Skipping harvest in " + dest.ToString() + ": resources under set limit.");
                            }
                        }
                    }

                    foreach (Coordinate destination in dic.Keys)
                    {
                        Celestial origin = dic[destination];
                        if (destination.Position == 16)
                        {
                            ExpeditionDebris debris = ogamedService.GetGalaxyInfo(destination).ExpeditionDebris;                            
                            Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Harvesting debris in " + destination.ToString());
                            long pathfindersToSend = Helpers.CalcShipNumberForPayload(debris.Resources, Buildables.Pathfinder, researches.HyperspaceTechnology, userInfo.Class);
                            SendFleet(origin, new Ships { Pathfinder = pathfindersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);                                
                        }
                        else
                        {
                            Debris debris = (celestials.Where(c => c.HasCoords(destination)).First() as Planet).Debris;                            
                            Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Harvesting debris in " + destination.ToString());
                            long recyclersToSend = Helpers.CalcShipNumberForPayload(debris.Resources, Buildables.Pathfinder, researches.HyperspaceTechnology, userInfo.Class);
                            SendFleet(origin, new Ships { Recycler = recyclersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);                            
                        }
                    }

                    int interval = (int)Helpers.CalcRandomInterval((int)settings.AutoHarvest.CheckIntervalMin, (int)settings.AutoHarvest.CheckIntervalMax);
                    var time = GetDateTime();
                    DateTime newTime = time.AddMilliseconds(interval);
                    timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
                    Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Next check at " + newTime.ToString());
                }
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Harvest, "HandleHarvest exception: " + e.Message);
                Helpers.WriteLog(LogType.Warning, LogSender.Harvest, "Stacktrace: " + e.StackTrace);
                int interval = (int)Helpers.CalcRandomInterval((int)settings.AutoHarvest.CheckIntervalMin, (int)settings.AutoHarvest.CheckIntervalMax);
                var time = GetDateTime();
                DateTime newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("HarvestTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Harvest, "Next check at " + newTime.ToString());
                UpdateTitle();
            }
            finally
            {
                //Release its semaphore
                xaSem[Feature.Harvest].Release();
            }

        }
    }
}
