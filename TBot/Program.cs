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
        static OgamedService ogamedService;
        static Timer defenderTimer;
        static Timer capacityTimer;
        static Timer repatriateTimer;
        static Timer expeditionsTimer;
        static Timer harvestTimer;

        static TelegramMessenger telegramMessenger;

        static Server serverInfo;
        static ServerData serverData;
        static UserInfo userInfo;
        static List<Celestial> celestials;
        static List<Planet> planets;
        static List<Moon> moons;
        static List<Fleet> fleets;
        static List<AttackerFleet> attacks;
        static Slots slots;
        static Researches researches;

        static dynamic settings;

        static void Main(string[] args)
        {
            Helpers.SetTitle();

            settings = Config.Global;
            Credentials credentials = new Credentials {
                Universe = settings.Credentials.Universe.ToString(),
                Username = settings.Credentials.Email.ToString(),
                Password = settings.Credentials.Password.ToString(),
                Language = settings.Credentials.Language.ToString().ToLower()
            };
            ogamedService = new OgamedService(credentials, int.Parse(settings.General.Port));
            ogamedService.SetUserAgent(settings.General.UserAgent.ToString());
            Thread.Sleep(Helpers.CalcRandomInterval(IntervalType.LessThanASecond));
            ogamedService.Login();
            Thread.Sleep(Helpers.CalcRandomInterval(IntervalType.AFewSeconds));

            serverInfo = ogamedService.GetServerInfo();
            serverData = ogamedService.GetServerData();

            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Server time: " + ogamedService.GetServerTime().ToString());

            userInfo = UpdateUserInfo();
            Helpers.SetTitle("[" + serverInfo.Name + "." + serverInfo.Language + "]" + " " + userInfo.PlayerName + " - Rank: " + userInfo.Rank);
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
                }

                Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing data...");
                celestials = UpdatePlanets(UpdateType.Fast);
                researches = ogamedService.GetResearches();

                if (settings.Defender.Active)
                {
                    InitializeDefender();
                }

                if (settings.Brain.Active)
                {
                    InitializeBrain();
                }

                if (settings.Expeditions.Active)
                {
                    InitializeExpeditions();
                }
            }
            else
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "Account in vacation mode");
            }

            Console.ReadLine();
            ogamedService.KillOgamedExecultable();
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

        private static void InitializeDefender()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing defender...");
            
            defenderTimer = new Timer(Defender, null, 0, Helpers.CalcRandomInterval((int)settings.Defender.CheckIntervalMin, (int)settings.Defender.CheckIntervalMax));
        }
        private static void InitializeBrain()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing brain...");

            if (settings.Brain.AutoCargo.Active)
            {
                capacityTimer = new Timer(AutoBuildCargo, null, Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo), Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax));
            }
            if (settings.Brain.AutoRepatriate.Active)
            {
                repatriateTimer = new Timer(AutoRepatriate, null, Helpers.CalcRandomInterval(IntervalType.AboutFiveMinutes), Helpers.CalcRandomInterval((int)settings.Brain.AutoRepatriate.CheckIntervalMin, (int)settings.Brain.AutoRepatriate.CheckIntervalMax));
            }
        }

        private static void InitializeExpeditions()
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Initializing expeditions...");

            expeditionsTimer = new Timer(HandleExpeditions, null, Helpers.CalcRandomInterval(IntervalType.SomeSeconds), Helpers.CalcRandomInterval(IntervalType.AboutAQuarterHour));
        }

        private static void Defender(object state)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Defender, "Checking attacks...");
            bool isUnderAttack = ogamedService.IsUnderAttack();
            DateTime time = ogamedService.GetServerTime();
            if (isUnderAttack)
            {
                if ((bool)settings.Defender.Alarm.Active) 
                    Task.Factory.StartNew(() => Helpers.PlayAlarm());
                Helpers.SetTitle("UNDER ATTACK - [" + serverInfo.Name + "." + serverInfo.Language + "]" + " " + userInfo.PlayerName + " - Rank: " + userInfo.Rank);
                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "UNDER ATTACK!!!");
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
            defenderTimer.Change(interval, Timeout.Infinite);
            Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
        }

        private static void AutoBuildCargo(object state)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Brain, "Checking capacity...");
            celestials = UpdatePlanets(UpdateType.Ships);
            celestials = UpdatePlanets(UpdateType.Resources);
            celestials = UpdatePlanets(UpdateType.Productions);
            foreach (Celestial planet in celestials)
            {
                var capacity = Helpers.CalcFleetCapacity(planet.Ships, researches.HyperspaceTechnology, userInfo.Class);
                Helpers.WriteLog(LogType.Info, LogSender.Brain, "Planet " + planet.ToString() + ": Available capacity: " + capacity.ToString("N0") + " - Resources: " + planet.Resources.TotalResources.ToString("N0") );
                if (planet.Coordinate.Type == Celestials.Moon && settings.Brain.AutoCargo.ExcludeMoons)
                {
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Skipping moon.");
                    continue;
                }
                if (capacity <= planet.Resources.TotalResources)
                {
                    long difference = planet.Resources.TotalResources - capacity;
                    Buildables preferredCargoShip = Enum.Parse<Buildables>(settings.Brain.AutoCargo.CargoType.ToString() ?? "SmallCargo") ?? Buildables.SmallCargo;
                    int oneShipCapacity = Helpers.CalcShipCapacity(preferredCargoShip, researches.HyperspaceTechnology, userInfo.Class);
                    int neededCargos = (int)Math.Round((float)difference / (float)oneShipCapacity, MidpointRounding.ToPositiveInfinity);
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, difference.ToString("N0") + " more capacity is needed, " + neededCargos + " more " + preferredCargoShip.ToString() + " are needed.");
                    if (planet.HasProduction())
                    {
                        Helpers.WriteLog(LogType.Warning, LogSender.Brain, "There is already a production ongoing. Skipping planet.");
                        foreach (Production production in planet.Productions)
                        {
                            Buildables productionType = (Buildables)production.ID;
                            Helpers.WriteLog(LogType.Info, LogSender.Brain, production.Nbr + "x" + productionType.ToString() + " are in production.");
                        }
                        continue;
                    }
                    var cost = ogamedService.GetPrice(preferredCargoShip, neededCargos);
                    if (planet.Resources.IsEnoughFor(cost))
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, "Building " + neededCargos + "x" + preferredCargoShip.ToString());
                        ogamedService.BuildShips(planet, preferredCargoShip, neededCargos);
                    }
                    else
                    {
                        Helpers.WriteLog(LogType.Warning, LogSender.Brain, "Not enough resources to build " + neededCargos + "x" + preferredCargoShip.ToString());
                        ogamedService.BuildShips(planet, Buildables.SmallCargo, neededCargos);
                    }
                    planet.Productions = ogamedService.GetProductions(planet);
                    foreach (Production production in planet.Productions)
                    {
                        Buildables productionType = (Buildables)production.ID;
                        Helpers.WriteLog(LogType.Info, LogSender.Brain, production.Nbr + "x" + productionType.ToString() + " are in production.");
                    }
                }
                else
                {
                    Helpers.WriteLog(LogType.Info, LogSender.Brain, "Capacity is ok.");
                }
            }
            var time = ogamedService.GetServerTime();
            var interval = Helpers.CalcRandomInterval((int)settings.Brain.AutoCargo.CheckIntervalMin, (int)settings.Brain.AutoCargo.CheckIntervalMax);
            var newTime = time.AddMilliseconds(interval);
            capacityTimer.Change(interval, Timeout.Infinite);
            Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next capacity check at " + newTime.ToString());
        }

        private static void AutoRepatriate(object state)
        {
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
                int idealShips = Helpers.CalcShipNumberForPayload(celestial.Resources, preferredShip, researches.HyperspaceTechnology, userInfo.Class);
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

            var time = ogamedService.GetServerTime();
            var interval = Helpers.CalcRandomInterval((int)settings.Brain.AutoRepatriate.CheckIntervalMin, (int)settings.Brain.AutoRepatriate.CheckIntervalMax);
            var newTime = time.AddMilliseconds(interval);
            repatriateTimer.Change(interval, Timeout.Infinite);
            Helpers.WriteLog(LogType.Info, LogSender.Brain, "Next repatriate check at " + newTime.ToString());
        }

        private static int SendFleet(Celestial origin, Ships ships, Coordinate destination, Missions mission, Speeds speed, Model.Resources payload = null, bool force = false)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Sending fleet from " + origin.Coordinate.ToString() + " to " + destination.ToString() + ". Mission: " + mission.ToString() + ". Ships: " + ships.ToString());
            UpdateSlots();
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
                    UpdateSlots();
                    return fleet.ID;
                }
                catch (Exception e)
                {
                    Helpers.WriteLog(LogType.Error, LogSender.Defender, "Exception: " + e.Message);
                    return 0;
                }
            }
            else
            {
                Helpers.WriteLog(LogType.Warning, LogSender.Tbot, "Unable to send fleet, no slots available");
                return 0;
            }
        }

        private static void HandleAttack(object state)
        {
            if (celestials.Count == 0)
            {
                DateTime time = ogamedService.GetServerTime();
                int interval = Helpers.CalcRandomInterval(IntervalType.SomeSeconds);
                DateTime newTime = time.AddMilliseconds(interval);
                defenderTimer.Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Unable to handle attack at the moment: bot is still getting account info.");
                Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                return;
            }
            foreach (AttackerFleet attack in attacks)
            {
                if ((bool)settings.TelegramMessenger.Active && (bool)settings.Defender.TelegramMessenger.Active)
                {
                    telegramMessenger.SendMessage("Player " + attack.AttackerName + " (" + attack.AttackerID + ") is attacking your planet " + attack.Destination.ToString() + " arriving at " + attack.ArrivalTime.ToString());
                }
                Celestial attackedCelestial = celestials.SingleOrDefault(planet => planet.Coordinate.Galaxy == attack.Destination.Galaxy && planet.Coordinate.System == attack.Destination.System && planet.Coordinate.Position == attack.Destination.Position && planet.Coordinate.Type == attack.Destination.Type);
                attackedCelestial = UpdatePlanet(attackedCelestial, UpdateType.Ships);
                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Player " + attack.AttackerName + " (" +  attack.AttackerID + ") is attacking your planet " + attackedCelestial.ToString() + " arriving at " + attack.ArrivalTime.ToString());
                if (settings.Defender.SpyAttacker.Active)
                {
                    UpdateSlots();
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
                            catch(Exception e)
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
                    catch(Exception e)
                    {
                        Helpers.WriteLog(LogType.Error, LogSender.Defender, "Could not message attacker: an exception has occurred: " + e.Message);
                    }
                }
                if (settings.Defender.Autofleet.Active)
                {
                    UpdateSlots();
                    if (slots.Free > 0)
                    {
                        try
                        {
                            attackedCelestial = UpdatePlanet(attackedCelestial, UpdateType.Resources);
                            Celestial destination;
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
                                Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not fleetsave: no valid destination exists");
                                DateTime time = ogamedService.GetServerTime();
                                int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                                DateTime newTime = time.AddMilliseconds(interval);
                                defenderTimer.Change(interval, Timeout.Infinite);
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
                                int fleetId = SendFleet(attackedCelestial, ships, destination.Coordinate, Missions.Deploy, Speeds.TenPercent, resources, true);
                                if (fleetId != 0)
                                {
                                    Fleet fleet = fleets.Single(fleet => fleet.ID == fleetId);
                                    Helpers.WriteLog(LogType.Info, LogSender.Defender, "Fleetsaved to " + destination.ToString() + ". Arrival at " + fleet.ArrivalTime.ToString());
                                }
                            }
                        }
                        catch(Exception e)
                        {
                            Helpers.WriteLog(LogType.Error, LogSender.Defender, "Could not fleetsave: an exception has occurred: " + e.Message);
                            DateTime time = ogamedService.GetServerTime();
                            int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                            DateTime newTime = time.AddMilliseconds(interval);
                            defenderTimer.Change(interval, Timeout.Infinite);
                            Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                        }
                    }
                    else
                    {
                        Helpers.WriteLog(LogType.Warning, LogSender.Defender, "Could not fleetsave: no slots available.");
                        DateTime time = ogamedService.GetServerTime();
                        int interval = Helpers.CalcRandomInterval(IntervalType.AFewSeconds);
                        DateTime newTime = time.AddMilliseconds(interval);
                        defenderTimer.Change(interval, Timeout.Infinite);
                        Helpers.WriteLog(LogType.Info, LogSender.Defender, "Next check at " + newTime.ToString());
                    }
                }
            }
        }

        private static void HandleExpeditions(object state)
        {
            celestials = UpdatePlanets(UpdateType.Ships);
            if ((bool)settings.Expeditions.AutoSendExpeditions.Active)
            {
                UpdateSlots();
                UpdateFleets();
                int expsToSend;
                if (settings.Expeditions.AutoSendExpeditions.WaitForAllExpeditions)
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
                            if (origin.Ships.IsEmpty())
                            {
                                Helpers.WriteLog(LogType.Warning, LogSender.Expeditions, "Unable to send expeditions: no ships available");
                            }
                            else
                            {
                                Buildables mainShip = Enum.Parse<Buildables>(settings.Expeditions.AutoSendExpeditions.MainShip.ToString() ?? "LargeCargo") ?? Buildables.LargeCargo;
                                Ships fleet = Helpers.CalcFullExpeditionShips(origin.Ships, mainShip, expsToSend, serverData, researches, userInfo.Class);

                                Helpers.WriteLog(LogType.Info, LogSender.Expeditions, expsToSend.ToString() + " expeditions with " + fleet.ToString() + " will be sent from " + origin.ToString());
                                for (int i = 0; i < expsToSend; i++)
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

                UpdateSlots();
                UpdateFleets();
                var time = ogamedService.GetServerTime();
                List<Fleet> orderedFleets = fleets
                    .Where(fleet => fleet.Mission == Missions.Expedition)
                    .OrderByDescending(fleet => fleet.BackIn)
                    .ToList();
                int interval;
                if (orderedFleets.Count == 0)
                {
                    interval = Helpers.CalcRandomInterval(IntervalType.AboutHalfAnHour);
                }
                else
                {
                    interval = (int)((1000 * orderedFleets.First().BackIn) + Helpers.CalcRandomInterval(IntervalType.AMinuteOrTwo));
                }
                DateTime newTime = time.AddMilliseconds(interval);
                expeditionsTimer.Change(interval, Timeout.Infinite);
                Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Next check at " + newTime.ToString());
            }

            if ((bool)settings.Expeditions.AutoHarvest.Active)
            {
                UpdateSlots();
                UpdateFleets();
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
                foreach(Coordinate destination in destinations)
                {
                    var galaxyInfos = ogamedService.GetGalaxyInfo(destination);
                    if (galaxyInfos.ExpeditionDebris.Resources.TotalResources > 0)
                    {
                        Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Debris detected at " + destination.ToString());
                        if (galaxyInfos.ExpeditionDebris.Resources.TotalResources >= settings.Expeditions.AutoHarvest.MinimumResources)
                        {
                            int pathfindersToSend = Helpers.CalcShipNumberForPayload(galaxyInfos.ExpeditionDebris.Resources, Buildables.Pathfinder, researches.HyperspaceTechnology, userInfo.Class);
                            SendFleet(origin, new Ships { Pathfinder = pathfindersToSend }, destination, Missions.Harvest, Speeds.HundredPercent);
                        }
                        else
                        {
                            Helpers.WriteLog(LogType.Info, LogSender.Expeditions, "Skipping hervest: resources under set limit.");
                        }
                    }
                }
            }
        }

        private static void UpdateSlots()
        {
            slots = ogamedService.GetSlots();
        }

        private static void UpdateFleets()
        {
            fleets = ogamedService.GetFleets();
        }

    }
}
