using Tbot;
using Tbot.Model;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.Threading;

namespace Tbot.Includes
{
    static class Helpers
    {
        public static void WriteLog(LogType type, LogSender sender, string message)
        {
            Console.ForegroundColor = type switch
            {
                LogType.Error => ConsoleColor.Red,
                LogType.Warning => ConsoleColor.Yellow,
                LogType.Info => ConsoleColor.Gray,
                LogType.Debug => ConsoleColor.White,
                _ => ConsoleColor.Gray
            };
            Console.WriteLine("[" + type.ToString() + "] " + "[" + sender.ToString() + "] " + "[" + DateTime.Now.ToString() + "] - " + message);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public static void SetTitle(string content = "")
        {
            AssemblyName exeInfo = Assembly.GetExecutingAssembly().GetName();
            string info = exeInfo.Name + " v" + exeInfo.Version;
            if (content != "")
                Console.Title = content + " - " + info;
            else
                Console.Title = info;
            return;
        }

        public static void PlayAlarm()
        {
            Console.Beep(800, 1500);
            Thread.Sleep(1000);
            Console.Beep(800, 2000);
            Thread.Sleep(1000);
            Console.Beep(800, 2000);
            return;
        }

        public static int CalcRandomInterval(IntervalType type)
        {
            var rand = new Random();
            return type switch
            {
                IntervalType.LessThanASecond => rand.Next(500, 1000),
                IntervalType.AFewSeconds => rand.Next(5000, 15000),
                IntervalType.SomeSeconds => rand.Next(20000, 50000),
                IntervalType.AMinuteOrTwo => rand.Next(40000, 140000),
                IntervalType.AboutFiveMinutes => rand.Next(240000, 360000),
                IntervalType.AboutTenMinutes => rand.Next(540000, 720000),
                IntervalType.AboutAQuarterHour => rand.Next(840000, 960000),
                IntervalType.AboutHalfAnHour => rand.Next(1500000, 2100000),
                IntervalType.AboutAnHour => rand.Next(3000000, 42000000),
                _ => rand.Next(500, 1000),
            };
        }

        public static int CalcRandomInterval(int min, int max)
        {
            var rand = new Random();
            var minMillis = min * 60 * 1000;
            var maxMillis = max * 60 * 1000;
            return rand.Next(minMillis, maxMillis);
        }

        public static int CalcShipCapacity(Buildables buildable, int hyperspaceTech, Classes playerClass)
        {
            int baseCargo;
            int bonus = (hyperspaceTech * 5);
            switch (buildable)
            {
                case Buildables.SmallCargo:
                    baseCargo = 5000;
                    if (playerClass == Classes.Collector) bonus += 25;
                    break;
                case Buildables.LargeCargo:
                    baseCargo = 25000;
                    if (playerClass == Classes.Collector) bonus += 25;
                    break;
                case Buildables.LightFighter:
                    baseCargo = 50;
                    break;
                case Buildables.HeavyFighter:
                    baseCargo = 100;
                    break;
                case Buildables.Cruiser:
                    baseCargo = 800;
                    break;
                case Buildables.Battleship:
                    baseCargo = 1500;
                    break;
                case Buildables.ColonyShip:
                    baseCargo = 7500;
                    break;
                case Buildables.Recycler:
                    baseCargo = 20000;
                    if (playerClass == Classes.General) bonus += 25;
                    break;
                case Buildables.EspionageProbe:
                    baseCargo = 5;
                    break;
                case Buildables.Bomber:
                    baseCargo = 750;
                    break;
                case Buildables.Destroyer:
                    baseCargo = 2000;
                    break;
                case Buildables.Deathstar:
                    baseCargo = 1000000;
                    break;
                case Buildables.Battlecruiser:
                    baseCargo = 750;
                    break;
                case Buildables.Reaper:
                    baseCargo = 7000;
                    break;
                case Buildables.Pathfinder:
                    baseCargo = 12000;
                    if (playerClass == Classes.General) bonus += 25;
                    break;
                default:
                    return 0;
            }
            return baseCargo * (bonus + 100) / 100;
        }

        public static long CalcFleetCapacity(Ships fleet, int hyperspaceTech, Classes playerClass)
        {
            long total = 0;
            foreach (PropertyInfo prop in fleet.GetType().GetProperties())
            {
                int qty = (int)prop.GetValue(fleet, null);
                if (qty == 0) continue;
                if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable))
                {
                    int oneCargo = CalcShipCapacity(buildable, hyperspaceTech, playerClass);
                    total += oneCargo * qty;
                }
            }
            return total;
        }

        public static int CalcShipSpeed(Buildables buildable, Researches researches, Classes playerClass)
        {
            return CalcShipSpeed(buildable, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, playerClass);
        }

        public static int CalcShipSpeed(Buildables buildable, int combustionDrive, int impulseDrive, int hyperspaceDrive, Classes playerClass)
        {
            int baseSpeed;
            int bonus = combustionDrive;
            switch (buildable)
            {
                case Buildables.SmallCargo:
                    baseSpeed = 5000;
                    if (impulseDrive >= 5)
                    {
                        baseSpeed = 10000;
                        bonus = impulseDrive * 2;
                    }
                    if (playerClass == Classes.Collector) bonus += 10;
                    break;
                case Buildables.LargeCargo:
                    baseSpeed = 7500;
                    if (playerClass == Classes.Collector) bonus += 10;
                    break;
                case Buildables.LightFighter:
                    baseSpeed = 12500;
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.HeavyFighter:
                    baseSpeed = 10000;
                    bonus = impulseDrive * 2;
                    if (playerClass == Classes.General)bonus += 10;
                    break;
                case Buildables.Cruiser:
                    baseSpeed = 15000;
                    bonus = impulseDrive * 2;
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.Battleship:
                    baseSpeed = 10000;
                    bonus = hyperspaceDrive * 3;
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.ColonyShip:
                    bonus = impulseDrive * 2;
                    baseSpeed = 2500;
                    break;
                case Buildables.Recycler:
                    baseSpeed = 2000;
                    if (impulseDrive >= 17)
                    {
                        baseSpeed = 4000;
                        bonus = impulseDrive * 2;
                    }
                    if (hyperspaceDrive >= 15)
                    {
                        baseSpeed = 6000;
                        bonus = hyperspaceDrive * 3;
                    }
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.EspionageProbe:
                    baseSpeed = 100000000;
                    break;
                case Buildables.Bomber:
                    baseSpeed = 4000;
                    bonus = impulseDrive * 2;
                    if (hyperspaceDrive >= 8)
                    {
                        baseSpeed = 5000;
                        bonus = hyperspaceDrive * 3;
                    }
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.Destroyer:
                    baseSpeed = 5000;
                    bonus = hyperspaceDrive * 3;
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.Deathstar:
                    baseSpeed = 100;
                    bonus = hyperspaceDrive * 3;
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.Battlecruiser:
                    baseSpeed = 10000;
                    bonus = hyperspaceDrive * 3;
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.Reaper:
                    baseSpeed = 10000;
                    bonus = hyperspaceDrive * 3;
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                case Buildables.Pathfinder:
                    baseSpeed = 10000;
                    bonus = hyperspaceDrive * 3;
                    if (playerClass == Classes.General) bonus += 10;
                    break;
                default:
                    return 0;
            }
            return (int)Math.Round(((float)baseSpeed * ((float)bonus + 10) / 10), MidpointRounding.ToZero);
        }

        public static int CalcFleetSpeed(Ships fleet, Researches researches, Classes playerClass)
        {
            return CalcFleetSpeed(fleet, researches.CombustionDrive, researches.ImpulseDrive, researches.HyperspaceDrive, playerClass);
        }

        public static int CalcFleetSpeed(Ships fleet, int combustionDrive, int impulseDrive, int hyperspaceDrive, Classes playerClass)
        {
            int minSpeed = 0;
            foreach (PropertyInfo prop in fleet.GetType().GetProperties())
            {
                int qty = (int)prop.GetValue(fleet, null);
                if (qty == 0) continue;
                if (Enum.TryParse<Buildables>(prop.Name, out Buildables buildable))
                {
                    int thisSpeed = CalcShipSpeed(buildable, combustionDrive, impulseDrive, hyperspaceDrive, playerClass);
                    if (thisSpeed < minSpeed) minSpeed = thisSpeed;
                }
            }
            return minSpeed;
        }

        public static Resources CalcMaxTransportableResources(Ships ships, Resources resources, int hyperspaceTech, Classes playerClass)
        {
            var capacity = CalcFleetCapacity(ships, hyperspaceTech, playerClass);
            if (resources.TotalResources <= capacity)
            {
                return resources;
            }
            else
            {
                if (resources.Deuterium > capacity)
                {
                    return new Resources { Deuterium = capacity };
                }
                else if (capacity >= resources.Deuterium && capacity < (resources.Deuterium + resources.Crystal))
                {
                    return new Resources { Deuterium = resources.Deuterium, Crystal = (capacity - resources.Deuterium) };
                }
                else if (capacity >= (resources.Deuterium + resources.Crystal) && capacity < resources.TotalResources)
                {
                    return new Resources { Deuterium = resources.Deuterium, Crystal = resources.Crystal, Metal = (capacity - resources.Deuterium - resources.Crystal) };
                }
                else return resources;
            }
        }

        public static int CalcShipNumberForPayload(Resources payload, Buildables buildable, int hyperspaceTech, Classes playerClass)
        {
            return (int)Math.Round(((float)payload.TotalResources / (float)CalcShipCapacity(buildable, hyperspaceTech, playerClass)), MidpointRounding.ToPositiveInfinity);
        }

        public static Ships CalcIdealExpeditionShips(Buildables buildable, int ecoSpeed, int topOnePoints, int hyperspaceTech, Classes playerClass)
        {
            var fleet = new Ships();

            int freightCap;
            if (topOnePoints < 10000)
                freightCap = 40000;
            else if (topOnePoints < 100000)
                freightCap = 500000;
            else if (topOnePoints < 1000000)
                freightCap = 1200000;
            else if (topOnePoints < 5000000)
                freightCap = 1800000;
            else if (topOnePoints < 25000000)
                freightCap = 2400000;
            else if (topOnePoints < 50000000)
                freightCap = 3000000;
            else if (topOnePoints < 75000000)
                freightCap = 3600000;
            else if (topOnePoints < 100000000)
                freightCap = 4200000;
            else
                freightCap = 5000000;

            if (playerClass == Classes.Discoverer)
                freightCap = freightCap * ecoSpeed * 3;
            else
                freightCap *= 2;

            int oneCargoCapacity = CalcShipCapacity(buildable, hyperspaceTech, playerClass);
            int cargoNumber = (int)Math.Round((float)freightCap / (float)oneCargoCapacity, MidpointRounding.ToPositiveInfinity);

            fleet = fleet.Add(buildable, cargoNumber);

            return fleet;
        }

        public static Buildables CalcMilitaryShipForExpedition(Ships fleet, int expeditionsNumber)
        {
            if (fleet.Reaper >= expeditionsNumber)
                return Buildables.Reaper;
            else if (fleet.Destroyer >= expeditionsNumber)
                return Buildables.Destroyer;
            else if (fleet.Bomber >= expeditionsNumber)
                return Buildables.Bomber;
            else if (fleet.Battlecruiser >= expeditionsNumber)
                return Buildables.Battlecruiser;
            else if (fleet.Battleship >= expeditionsNumber)
                return Buildables.Battleship;
            else if (fleet.Pathfinder >= expeditionsNumber)
                return Buildables.Pathfinder;
            else if (fleet.Cruiser >= expeditionsNumber)
                return Buildables.Cruiser;
            else if (fleet.HeavyFighter >= expeditionsNumber)
                return Buildables.HeavyFighter;
            else if (fleet.LightFighter >= expeditionsNumber)
                return Buildables.LightFighter;
            else
                return Buildables.Null;
        }

        public static Ships CalcExpeditionShips(Ships fleet, Buildables buildable, int expeditionsNumber, int ecoSpeed, int topOnePoints, int hyperspaceTech, Classes playerClass)
        {
            Ships ideal = CalcIdealExpeditionShips(buildable, ecoSpeed, topOnePoints, hyperspaceTech, playerClass);
            foreach (PropertyInfo prop in fleet.GetType().GetProperties())
            {
                if (prop.Name == buildable.ToString())
                {
                    int availableVal = (int)prop.GetValue(fleet);
                    int idealVal = (int)prop.GetValue(ideal);
                    if (availableVal < idealVal * expeditionsNumber)
                    {
                        int realVal = (int)Math.Round(((float)availableVal / (float)expeditionsNumber), MidpointRounding.ToZero);
                        prop.SetValue(ideal, realVal);
                    }                        
                }
            }
            return ideal;
        }

        public static Ships CalcExpeditionShips(Ships fleet, Buildables buildable, int expeditionsNumber, ServerData serverdata, Researches researches, Classes playerClass)
        {
            return CalcExpeditionShips(fleet, buildable, expeditionsNumber, serverdata.Speed, serverdata.TopScore, researches.HyperspaceTechnology, playerClass);
        }

        public static bool MayAddShipToExpedition(Ships fleet, Buildables buildable, int expeditionsNumber)
        {
            foreach (PropertyInfo prop in fleet.GetType().GetProperties())
            {
                if (prop.Name == buildable.ToString())
                {
                    int availableVal = (int)prop.GetValue(fleet);
                    if (availableVal >= expeditionsNumber)
                        return true;
                }
            }
            return false;
        }

        public static Ships CalcFullExpeditionShips(Ships fleet, Buildables buildable, int expeditionsNumber, ServerData serverdata, Researches researches, Classes playerClass)
        {
            Ships oneExpeditionFleet = CalcExpeditionShips(fleet, buildable, expeditionsNumber, serverdata, researches, playerClass);
            if (MayAddShipToExpedition(fleet, Buildables.EspionageProbe, expeditionsNumber))
                oneExpeditionFleet.Add(Buildables.EspionageProbe, 1);
            if (MayAddShipToExpedition(fleet, Buildables.Pathfinder, expeditionsNumber))
                oneExpeditionFleet.Add(Buildables.Pathfinder, 1);
            Buildables militaryShip = CalcMilitaryShipForExpedition(fleet, expeditionsNumber);
            if (MayAddShipToExpedition(fleet, militaryShip, expeditionsNumber))
                oneExpeditionFleet.Add(militaryShip, 1);

            return oneExpeditionFleet;
        }

        public static int CalcDistance(Coordinate origin, Coordinate destination, int galaxiesNumber, int systemsNumber = 499, bool donutGalaxy = true, bool donutSystem = true )
        {
            if (origin.Galaxy != destination.Galaxy)
            {
                return CalcGalaxyDistance(origin, destination, galaxiesNumber, donutGalaxy);
            }
            if (origin.System != destination.System)
            {
                return CalcSystemDistance(origin, destination, systemsNumber, donutSystem);
            }
            if (origin.Position != destination.Position)
            {
                return CalcPlanetDistance(origin, destination);
            }
            return 5;
        }

        public static int CalcDistance(Coordinate origin, Coordinate destination, ServerData serverData)
        {
            return CalcDistance(origin, destination, serverData.Galaxies, serverData.Systems, serverData.DonutGalaxy, serverData.DonutSystem);
        }

        private static int CalcGalaxyDistance(Coordinate origin, Coordinate destination, int galaxiesNumber, bool donutGalaxy = true)
        {
            if (!donutGalaxy)
            {
                return 2000 * Math.Abs(origin.Galaxy - destination.Galaxy);
            }
            if (origin.Galaxy > destination.Galaxy)
            {
                return 2000 * Math.Min((origin.Galaxy - destination.Galaxy),((destination.Galaxy + galaxiesNumber) - origin.Galaxy));
            }
            else
            {
                return 2000 * Math.Min((destination.Galaxy - origin.Galaxy), ((origin.Galaxy + galaxiesNumber) - destination.Galaxy));
            }
        }

        private static int CalcSystemDistance(Coordinate origin, Coordinate destination, int systemsNumber, bool donutSystem = true)
        {
            if (!donutSystem)
            {
                return 2700 + 95 * Math.Abs(origin.System - destination.System);
            }
            if (origin.System > destination.System)
            {
                return 2700 + 95 * Math.Min((origin.System - destination.System), ((destination.System + systemsNumber) - origin.System));
            }
            else
            {
                return 2700 + 95 * Math.Min((destination.System - origin.System), ((origin.System + systemsNumber) - destination.System));
            }
        }

        private static int CalcPlanetDistance(Coordinate origin, Coordinate destination)
        {
            return 1000 + 5 * Math.Abs(destination.Position - origin.Position);
        }

        public static int CalcMetalProduction(int level, int position, int speedFactor, float ratio = 1, int plasma = 0, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            int baseProd = position switch
            {
                6 => (int)Math.Round(30 + (30 * 0.17)),
                7 => (int)Math.Round(30 + (30 * 0.23)),
                8 => (int)Math.Round(30 + (30 * 0.35)),
                9 => (int)Math.Round(30 + (30 * 0.23)),                
                10 => (int)Math.Round(30 + (30 * 0.17)),                
                _ => 30,
            };
            baseProd *= speedFactor;
            int prod = (int)Math.Round((float)(baseProd * level * Math.Pow(1.1, level)));
            int plasmaProd = (int)Math.Round(prod * 0.01 * plasma);
            int geologistProd = 0;
            if (hasGeologist)
            {
                geologistProd = (int)Math.Round(prod * 0.1);
            }
            int staffProd = 0;
            if (hasStaff)
            {
                staffProd = (int)Math.Round(prod * 0.02);
            }
            int classProd = 0;
            if (playerClass == Classes.Collector)
            {
                classProd = (int)Math.Round(prod * 0.25);
            }            
            return (int)Math.Round((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio);
        }

        public static int CalcMetalProduction(Buildings buildings, int position, int speedFactor, float ratio = 1, Researches researches = null, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            if (researches == null) researches = new Researches() { PlasmaTechnology = 0 };
            return CalcMetalProduction(buildings.MetalMine, position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
        }

        public static int CalcMetalProduction(Planet planet,int speedFactor, float ratio = 1, Researches researches = null, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            if (researches == null) researches = new Researches() { PlasmaTechnology = 0 };
            return CalcMetalProduction(planet.Buildings.MetalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
        }

        public static int CalcCrystalProduction(int level, int position, int speedFactor, float ratio = 1, int plasma = 0, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            int baseProd = position switch
            {
                1 => (int)Math.Round(20 + (20 * 0.3)),
                2 => (int)Math.Round(20 + (20 * 0.2)),
                3 => (int)Math.Round(20 + (20 * 0.1)),
                _ => 20,
            };
            baseProd *= speedFactor;
            int prod = (int)Math.Round((float)(baseProd * level * Math.Pow(1.1, level)));
            int plasmaProd = (int)Math.Round(prod * 0.0066 * plasma);
            int geologistProd = 0;
            if (hasGeologist)
            {
                geologistProd = (int)Math.Round(prod * 0.1);
            }
            int staffProd = 0;
            if (hasStaff)
            {
                staffProd = (int)Math.Round(prod * 0.02);
            }
            int classProd = 0;
            if (playerClass == Classes.Collector)
            {
                classProd = (int)Math.Round(prod * 0.25);
            }            
            return (int)Math.Round((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio);
        }

        public static int CalcCrystalProduction(Buildings buildings, int position, int speedFactor, float ratio = 1, Researches researches = null, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            if (researches == null) researches = new Researches() { PlasmaTechnology = 0 };
            return CalcCrystalProduction(buildings.CrystalMine, position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
        }

        public static int CalcCrystalProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            if (researches == null) researches = new Researches() { PlasmaTechnology = 0 };
            return CalcCrystalProduction(planet.Buildings.CrystalMine, planet.Coordinate.Position, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
        }

        public static int CalcDeuteriumProduction(int level, float temp, int speedFactor, float ratio = 1, int plasma = 0, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            int baseProd = 10 * speedFactor;
            int prod = (int)Math.Round((float)(baseProd * level * Math.Pow(1.1, level) * (1.05 + (0.01 * temp))));
            int plasmaProd = (int)Math.Round(prod * 0.0033 * plasma);
            int geologistProd = 0;
            if (hasGeologist)
            {
                geologistProd = (int)Math.Round(prod * 0.1);
            }
            int staffProd = 0;
            if (hasStaff)
            {
                staffProd = (int)Math.Round(prod * 0.02);
            }
            int classProd = 0;
            if (playerClass == Classes.Collector)
            {
                classProd = (int)Math.Round(prod * 0.25);
            }
            return (int)Math.Round((prod + plasmaProd + geologistProd + staffProd + classProd) * ratio);
        }

        public static int CalcDeuteriumProduction(Buildings buildings, Temperature temp, int speedFactor, float ratio = 1, Researches researches = null, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            if (researches == null) researches = new Researches() { PlasmaTechnology = 0 };
            return CalcDeuteriumProduction(buildings.CrystalMine, temp.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
        }

        public static int CalcDeuteriumProduction(Planet planet, int speedFactor, float ratio = 1, Researches researches = null, Classes playerClass = Classes.NoClass, bool hasGeologist = false, bool hasStaff = false)
        {
            if (researches == null) researches = new Researches() { PlasmaTechnology = 0 };
            return CalcDeuteriumProduction(planet.Buildings.CrystalMine, planet.Temperature.Average, speedFactor, ratio, researches.PlasmaTechnology, playerClass, hasGeologist, hasStaff);
        }
    }
}
