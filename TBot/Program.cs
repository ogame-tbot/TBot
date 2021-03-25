using Tbot.Services;
using Tbot.Model;
using Tbot.Includes;
using System;
using System.Threading;
using System.Collections.Generic;
using JsonConfig;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Linq;
using System.Threading.Tasks;

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
        static volatile List<AttackerFleet> attacks;
        static volatile Slots slots;
        static volatile Researches researches;
        /*Lorenzo 07/02/2021
         * Added array of semaphore to manage the cuncurrency
         * for timers. 
         * ATTENTION!!In case of adding some timers
         * you need to redim the Semaphore array!!!
         */
        static Semaphore[] xaSem = new Semaphore[6];

        static void Main(string[] args)
        {
            Helpers.SetTitle();

            settings = Config.Global;
            Credentials credentials = new Credentials
            {
                Universe = settings.Credentials.Universe.ToString(),
                Username = settings.Credentials.Email.ToString(),
                Password = settings.Credentials.Password.ToString(),
                Language = settings.Credentials.Language.ToString().ToLower()
            };


            try
            {
                /**
                 * Tralla 20/2/21
                 * 
                 * add ability to set custom host 
                 */
                var host = settings.General.Host ?? "localhost";
                var port = settings.General.Port ?? "8080";
                ogamedService = new OgamedService(credentials, (string)host, int.Parse(port));
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to start ogamed: " + e.Message);
            }
            try
            {
                ogamedService.SetUserAgent(settings.General.UserAgent.ToString());
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to set user agent: " + e.Message);
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
                        telegramMessenger = new TelegramMessenger(settings.TelegramMessenger.API.ToString(), settings.TelegramMessenger.ChatId.ToString());
                        telegramMessenger.SendMessage("[" + userInfo.PlayerName + "@" + serverData.Name + "." + serverData.Language + "] TBot activated");
                    }

                    Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing data...");
                    celestials = UpdatePlanets(UpdateType.Fast);
                    researches = ogamedService.GetResearches();

                    timers = new Dictionary<string, Timer>();

                    /*Lorenzo 07/02/2020 initialize semaphores
                     * For the same reason in the xaSem declaration
                     * if you add some timers add the initialization
                     * of their timer
                     * 
                     * Tralla 12/2/20
                     * change index to enum
                     */
                    xaSem[(int)Feature.Defender] = new Semaphore(1, 1); //Defender
                    xaSem[(int)Feature.BrainAutobuildCargo] = new Semaphore(1, 1); //Brain - Autobuild cargo
                    xaSem[(int)Feature.BrainAutoRepatriate] = new Semaphore(1, 1); //Brain - AutoRepatriate
                    xaSem[(int)Feature.BrainAutoMine] = new Semaphore(1, 1); //Brain - Auto mine
                    xaSem[(int)Feature.Expeditions] = new Semaphore(1, 1); //Expeditions
                    xaSem[(int)Feature.Harvest] = new Semaphore(1, 1); //Harvest


                    if ((bool)settings.Defender.Active)
                    {
                        InitializeDefender();
                    }

                    if ((bool)settings.Brain.Active)
                    {
                        InitializeBrain();
                    }

                    if ((bool)settings.Expeditions.Active)
                    {
                        InitializeExpeditions();
                    }

                    if ((bool)settings.AutoHarvest.Active)
                    {
                        InitializeHarvest();
                    }
                }
                else
                {
                    Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "Account in vacation mode");
                }

                Console.ReadLine();
                ogamedService.KillOgamedExecultable();
            }

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
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Updating player data...");
            UserInfo user = ogamedService.GetUserInfo();
            user.Class = ogamedService.GetUserClass();
            return user;
        }

        private static List<Celestial> UpdatePlanets(UpdateType updateType = UpdateType.Full)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Updating celestials... Mode: " + updateType.ToString());
            List<Celestial> localPlanets;
            if (celestials == null)
            {
                localPlanets = ogamedService.GetCelestials();
            }
            else
            {
                localPlanets = celestials;
            }
            List<Celestial> newPlanets = new List<Celestial>();
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
            Helpers.SetTitle("[" + serverInfo.Name + "." + serverInfo.Language + "]" + " " + userInfo.PlayerName + " - Rank: " + userInfo.Rank);
        }

        private static void InitializeDefender()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing defender...");

            timers.Add("DefenderTimer", new Timer(Defender, null, 0, Helpers.CalcRandomInterval((int)settings.Defender.CheckIntervalMin, (int)settings.Defender.CheckIntervalMax)));
        }
        private static void InitializeBrain()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing brain...");

            if (settings.Brain.AutoCargo.Active)
            {
                timers.Add("CapacityTimer", new Timer(AutoBuildCargo, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax)));
            }
            if (settings.Brain.AutoRepatriate.Active)
            {
                timers.Add("RepatriateTimer", new Timer(AutoRepatriate, null, Helpers.CalcRandomInterval(IntervalType.AboutFiveMinutes), Helpers.CalcRandomInterval((int)settings.Brain.AutoRepatriate.CheckIntervalMin, (int)settings.Brain.AutoRepatriate.CheckIntervalMax)));
            }
            /*Lorenzo 05/02/2021
             * Adding timer for auto buil mine
             */
            if (settings.Brain.AutoMine.Active)
            {
                timers.Add("AutoMineTimer", new Timer(AutoMine, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Helpers.CalcRandomInterval((int)settings.Brain.Automine.CheckIntervalMin, (int)settings.Brain.Automine.CheckIntervalMax)));
            }
        }

        private static void InitializeExpeditions()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing expeditions...");

            timers.Add("ExpeditionsTimer", new Timer(HandleExpeditions, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo)));
        }

        private static void InitializeHarvest()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing Harvest...");
            Helpers.WriteLog(LogType.Info, LogSender.Harvest, "This feature will be reintroduced in the next version, in a hopefully better way");
            //timers.Add("HarvestTimer", new Timer(HandleExpeditions, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Helpers.CalcRandomInterval(IntervalType.AboutAQuarterHour)));
        }

        private static void Defender(object state)
        {

            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[(int)Feature.Defender].WaitOne();
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

            }
            finally
            {
                //Release its semaphore
                xaSem[(int)Feature.Defender].Release();
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
                xaSem[(int)Feature.BrainAutoMine].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Checking mines and resources..");
                celestials = UpdatePlanets(UpdateType.Resources);
                celestials = UpdatePlanets(UpdateType.Buildings);
                celestials = UpdatePlanets(UpdateType.Facilities);
                celestials = UpdatePlanets(UpdateType.Constructions);

                Buildables xBuildable = Buildables.Null;
                int nLevelToReach = 0;
                foreach (Celestial xCelestial in celestials)
                {
                    if (xCelestial.Constructions.BuildingID != 0)
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping celestial " + xCelestial.ToString() + ": there is already a building in production.");
                        continue;
                    }
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Running AutoMine for celestial " + xCelestial.ToString());
                    if (xCelestial is Planet)
                    {
                        if (Helpers.ShouldBuildEnergySource(xCelestial as Planet))
                        {
                            //Checks if energy is needed
                            xBuildable = Helpers.GetNextEnergySourceToBuild(xCelestial as Planet, (int)settings.Brain.AutoMine.MaxSolarPlant, (int)settings.Brain.AutoMine.MaxFusionReactor);
                            nLevelToReach = Helpers.GetNextLevel(xCelestial as Planet, xBuildable);
                        }
                        if (xBuildable == Buildables.Null && Helpers.ShouldBuildNanites(xCelestial as Planet, (int)settings.Brain.AutoMine.MaxNaniteFactory))
                        {
                            //Manage the need of nanites
                            xBuildable = Buildables.NaniteFactory;
                            nLevelToReach = Helpers.GetNextLevel(xCelestial as Planet, xBuildable);
                        }
                        if (xBuildable == Buildables.Null && Helpers.ShouldBuildRoboticFactory(xCelestial as Planet, (int)settings.Brain.AutoMine.MaxRoboticsFactory))
                        {
                            //Manage the need of robotics factory
                            xBuildable = Buildables.RoboticsFactory;
                            nLevelToReach = Helpers.GetNextLevel(xCelestial as Planet, xBuildable);
                        }
                        if (xBuildable == Buildables.Null && Helpers.ShouldBuildShipyard(xCelestial as Planet, (int)settings.Brain.AutoMine.MaxShipyard))
                        {
                            //Manage the need of shipyard
                            xBuildable = Buildables.Shipyard;
                            nLevelToReach = Helpers.GetNextLevel(xCelestial as Planet, xBuildable);
                        }
                        if (xBuildable == Buildables.Null)
                        {
                            //Manage the need of build some deposit
                            mHandleDeposit(xCelestial, ref xBuildable, ref nLevelToReach);
                        }
                        //If it isn't needed to build deposit
                        //check if it needs to build some mines 
                        if (xBuildable == Buildables.Null)
                        {
                            mHandleMines(xCelestial, ref xBuildable, ref nLevelToReach);
                        }

                        if (xBuildable != Buildables.Null && nLevelToReach > 0)
                            mHandleBuildCelestialBuild(xCelestial, xBuildable, nLevelToReach);
                    }
                    else
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping moon " + xCelestial.ToString());
                    }

                    xBuildable = Buildables.Null;
                    nLevelToReach = 0;
                }


                var time = GetDateTime();
                var interval = Helpers.CalcRandomInterval((int)settings.Brain.AutoMine.CheckIntervalMin, (int)settings.Brain.AutoMine.CheckIntervalMax);
                var newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("AutoMineTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next AutoMine check at " + newTime.ToString());
                UpdateTitle();
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Brain, "AutoMine Exception: " + e.Message);
            }
            finally
            {
                //Release its semaphore
                xaSem[(int)Feature.BrainAutoMine].Release();
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
                    //Yes, i can build it
                    ogamedService.BuildConstruction(xCelestial, xBuildableToBuild);
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building " + xBuildableToBuild.ToString() + " level " + nLevelToBuild.ToString() + " on " + xCelestial.ToString());
                }
                else
                {
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Not enough resources to build: " + xBuildableToBuild.ToString() + " level " + nLevelToBuild.ToString() + " on " + xCelestial.ToString());
                }
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Brain, "mHandleBuildCelestialMines Exception: " + e.Message);
            }
        }

        private static void AutoBuildCargo(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[(int)Feature.BrainAutobuildCargo].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Checking capacity...");
                celestials = UpdatePlanets(UpdateType.Ships);
                celestials = UpdatePlanets(UpdateType.Resources);
                celestials = UpdatePlanets(UpdateType.Productions);
                foreach (Celestial planet in celestials)
                {
                    var capacity = Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class);
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Celestial " + planet.ToString() + ": Available capacity: " + capacity.ToString("N0") + " - Resources: " + planet.Resources.TotalResources.ToString("N0"));
                    if (planet.Coordinate.Type == Celestials.Moon && settings.Brain.AutoCargo.ExcludeMoons)
                    {
                        Helpers.WriteLog(LogType.Debug, LogSender.Brain, "Celestial " + planet.ToString() + " is a moon - Skipping moon.");
                        continue;
                    }
                    if (capacity <= planet.Resources.TotalResources)
                    {
                        long difference = planet.Resources.TotalResources - capacity;
                        Buildables preferredCargoShip = Enum.Parse<Buildables>(settings.Brain.AutoCargo.CargoType.ToString() ?? "SmallCargo") ?? Buildables.SmallCargo;
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
                            ogamedService.BuildShips(planet, preferredCargoShip, neededCargos);
                        }
                        else
                        {
                            Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Celestial " + planet.ToString() + " - Not enough resources to build " + neededCargos + "x" + preferredCargoShip.ToString());
                            ogamedService.BuildShips(planet, preferredCargoShip, neededCargos);
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
                var time = GetDateTime();
                var interval = Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax);
                var newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("CapacityTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next capacity check at " + newTime.ToString());
                UpdateTitle();
            }
            catch (Exception)
            {
            }
            finally
            {
                //Release its semaphore
                xaSem[(int)Feature.BrainAutobuildCargo].Release();
            }
        }


        private static void AutoRepatriate(object state)
        {
            try
            {
                // Wait for the thread semaphore
                // to avoid the concurrency with itself
                xaSem[(int)Feature.BrainAutoRepatriate].WaitOne();
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Reaptriating resources...");
                celestials = UpdatePlanets(UpdateType.Resources);
                celestials = UpdatePlanets(UpdateType.Ships);

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
                    Buildables preferredShip = Enum.Parse<Buildables>(settings.Brain.AutoRepatriate.CargoType.ToString() ?? "SmallCargo") ?? Buildables.SmallCargo;
                    long idealShips = Helpers.CalcShipNumberForPayload(celestial.Resources, preferredShip, researches.HyperspaceTechnology, userInfo.Class);
                    Ships ships = new Ships();
                    if (idealShips <= celestial.Ships.GetAmount(preferredShip))
                    {
                        ships.Add(preferredShip, idealShips);
                    }
                    else
                    {
                        ships.Add(preferredShip, celestial.Ships.GetAmount(preferredShip));
                    }
                    Resources payload = Helpers.CalcMaxTransportableResources(ships, celestial.Resources, researches.HyperspaceTechnology, userInfo.Class);
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
                                .Where(planet => planet.Coordinate.Type == Enum.Parse<Celestials>(settings.Brain.AutoRepatriate.Target.Type.ToString()))
                                .Single();
                            destination = customDestination;
                            if (celestial.ID == customDestination.ID)
                            {
                                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping celestial: this celestial is the destination");
                                continue;
                            }
                        }
                        catch (Exception e)
                        {
                            Helpers.WriteLog(LogType.Debug, LogSender.Brain, "Exception: " + e.Message);
                            Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Unable to parse custom destination");
                        }
                    }
                    SendFleet(celestial, ships, destination.Coordinate, Missions.Transport, Speeds.HundredPercent, payload);
                }

                var time = GetDateTime();
                var interval = Helpers.CalcRandomInterval((int)settings.Brain.AutoRepatriate.CheckIntervalMin, (int)settings.Brain.AutoRepatriate.CheckIntervalMax);
                var newTime = time.AddMilliseconds(interval);
                timers.GetValueOrDefault("RepatriateTimer").Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next repatriate check at " + newTime.ToString());
                UpdateTitle();
            }
            catch (Exception)
            {
            }
            finally
            {
                //Release its semaphore
                xaSem[(int)Feature.BrainAutoRepatriate].Release();
            }
        }

        private static int SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, Speeds speed, Model.Resources payload = null, bool force = false)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Sending fleet from " + origin.Coordinate.ToString() + " to " + destination.ToString() + ". Mission: " + mission.ToString() + ". Ships: " + ships.ToString());
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
                Thread.Sleep((int)IntervalType.AFewSeconds);
                fleets = UpdateFleets();
                var recalledFleet = fleets.SingleOrDefault(f => f.ID == fleet.ID);
                if (recalledFleet.ID == 0)
                {
                    Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to recall fleet: an unknon error has occurred.");
                }
                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Fleet recalled: return time: " + fleet.ArrivalTime.ToString());
                return result;
            }
            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Error, LogSender.Tbot, "Unable to recall fleet: an exception has occurred: " + e.Message);
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
                if (settings.Defender.SpyAttacker.Active)
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
                                Ships ships = new Ships { EspionageProbe = (int)settings.Defender.SpyAttacker.Probes };
                                int fleetId = SendFleet(attackedCelestial, ships, destination, Missions.Spy, Speeds.HundredPercent);
                                Fleet fleet = fleets.Single(fleet => fleet.ID == fleetId);
                                Helpers.WriteLog(LogType.Info, LogSender.Defender, "Spying attacker from " + attackedCelestial.ToString() + " to " + destination.ToString() + " with " + settings.Defender.SpyAttacker.Probes + " probes. Arrival at " + fleet.ArrivalTime.ToString());
                            }
                            catch (Exception e)
                            {
                                Helpers.WriteLog(LogType.Error, LogSender.Defender, "Could not spy attacker: an exception has occurred: " + e.Message);
                            }
                        }

                    }
                    else
                    {
                        Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not send probes: no slots available.");
                    }
                }
                if (settings.Defender.MessageAttacker.Active)
                {
                    try
                    {
                        Random random = new Random();
                        string[] messages = settings.Defender.MessageAttacker.Messages;
                        int messageIndex = random.Next(0, messages.Length);
                        string message = messages[messageIndex];
                        ogamedService.SendMessage(attack.AttackerID, messages[messageIndex]);
                        Helpers.WriteLog(LogType.Info, LogSender.Defender, "Sent message \"" + message + "\" to attacker" + attack.AttackerName);
                    }
                    catch (Exception e)
                    {
                        Helpers.WriteLog(LogType.Error, LogSender.Defender, "Could not message attacker: an exception has occurred: " + e.Message);
                    }
                }
                if (settings.Defender.Autofleet.Active)
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
                                int fleetId = SendFleet(attackedCelestial, ships, destination.Coordinate, mission, Speeds.TenPercent, resources, true);
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
                                        var interval = (((attack.ArriveIn * 1000) + (((attack.ArriveIn * 1000) / 100) * 20)) / 2) + Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
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
                xaSem[(int)Feature.Expeditions].WaitOne();

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
                                List<Celestial> origins = new List<Celestial>();
                                if (settings.Expeditions.AutoSendExpeditions.Origin.Length > 0)
                                {
                                    try
                                    {
                                        foreach (var origin in settings.Expeditions.AutoSendExpeditions.Origin)
                                        {
                                            Coordinate customOriginCoords = new Coordinate(
                                                (int)origin.Galaxy,
                                                (int)origin.System,
                                                (int)origin.Position,
                                                Enum.Parse<Celestials>(origin.Type.ToString()));
                                                Celestial customOrigin = celestials
                                                    .Single(planet => planet.HasCoords(customOriginCoords));
                                                customOrigin = UpdatePlanet(customOrigin, UpdateType.Ships);
                                                origins.Add(customOrigin);
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Helpers.WriteLog(LogType.Debug, LogSender.Expeditions, "Exception: " + e.Message);
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
                                foreach (var origin in origins)
                                {
                                    int expsToSendFromThisOrigin = (int)Math.Round((float)expsToSend / (float)origins.Count, MidpointRounding.ToZero);
                                    if (origin == origins.Last())
                                    {
                                        expsToSendFromThisOrigin = (int)Math.Round((float)expsToSend / (float)origins.Count, MidpointRounding.ToPositiveInfinity);
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
                                            SendFleet(origin, fleet, destination, Missions.Expedition, Speeds.HundredPercent);
                                            Thread.Sleep((int)IntervalType.AFewSeconds);
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
                    if (orderedFleets.Count == 0)
                    {
                        interval = Helpers.CalcRandomInterval(IntervalType.AboutTenMinutes);
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
                                Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Skipping hervest: resources under set limit.");
                            }
                        }
                    }
                }
                */
            }

            catch (Exception e)
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "HandleExpeditions exception: " + e.Message);
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
                xaSem[(int)Feature.Expeditions].Release();
            }

        }
    }
}
