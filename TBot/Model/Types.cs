using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace Tbot.Model
{

    public class Credentials
    {
        public string Universe { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string Language { get; set; }
        public bool IsLobbyPioneers { get; set; }
        public string BasicAuthUsername { get; set; }
        public string BasicAuthPassword { get; set; }
    }

    public class OgamedResponse
    {
        public string Status { get; set; }
        public int Code { get; set; }
        public string Message { get; set; }
        public dynamic Result { get; set; }
    }

    public class Coordinate
    {
        public Coordinate(int galaxy = 1, int system = 1, int position = 1, Celestials type = Celestials.Planet)
        {
            Galaxy = galaxy;
            System = system;
            Position = position;
            Type = type;
        }
        public int Galaxy { get; set; }
        public int System { get; set; }
        public int Position { get; set; }
        public Celestials Type { get; set; }
        public override string ToString()
        {
            return "[" + GetCelestialCode() + ":" + Galaxy + ":" + System + ":" + Position + "]";
        }
        private string GetCelestialCode()
        {
            return Type switch
            {
                Celestials.Planet => "P",
                Celestials.Debris => "DF",
                Celestials.Moon => "M",
                Celestials.DeepSpace => "DS",
                _ => "",
            };
        }

        public bool IsSame(Coordinate otherCoord)
        {
            if (Galaxy == otherCoord.Galaxy && System == otherCoord.System && Position == otherCoord.Position && Type == otherCoord.Type)
                return true;
            else
                return false;
        }
    }

    public class Fields
    {
        public int Built { get; set; }
        public int Total { get; set; }
        public int Free
        {
            get
            {
                return Total - Built;
            }
        }
    }

    public class Temperature
    {
        public int Min { get; set; }
        public int Max { get; set; }
        public float Average
        {
            get
            {
                return (float)(Min + Max) / 2;
            }
        }
    }

    public class ResourceSettings
    {
        public float MetalMine { get; set; }
        public float CrystalMine { get; set; }
        public float DeuteriumSynthesizer { get; set; }
        public float SolarPlant { get; set; }
        public float FusionReactor { get; set; }
        public float SolarSatellite { get; set; }
        public float Crawler { get; set; }
    }

    public class Celestial
    {
        public int ID { get; set; }
        public string Img { get; set; }
        public string Name { get; set; }
        public int Diameter { get; set; }
        public int Activity { get; set; }
        public Coordinate Coordinate { get; set; }
        public Fields Fields { get; set; }
        public Resources Resources { get; set; }
        public Ships Ships { get; set; }
        public Defences Defences { get; set; }
        public Buildings Buildings { get; set; }
        public Facilities Facilities { get; set; }
        public List<Production> Productions { get; set; }
        public Constructions Constructions { get; set; }
        public ResourceSettings ResourceSettings { get; set; }
        public ResourcesProduction ResourcesProduction { get; set; }
        public Debris Debris { get; set; }
        public override string ToString()
        {
            return Name + " " + Coordinate.ToString();
        }
        public bool HasProduction()
        {
            try
            {
                if (Productions.Count == 0)
                    return false;
                else
                    return true;
            }
            catch
            {
                return false;
            }
        }
        internal bool HasConstruction()
        {
            try
            {
                if (Constructions.BuildingID != (int)Buildables.Null)
                    return true;
                else
                    return false;
            }
            catch
            {
                return false;
            }
        }
        public bool HasCoords(Coordinate coords)
        {
            if (coords.Galaxy == Coordinate.Galaxy && coords.System == Coordinate.System && coords.Position == Coordinate.Position && coords.Type == Coordinate.Type)
                return true;
            else
                return false;
        }
        public int GetLevel(Buildables building)
        {
            int output = 0;
            foreach (PropertyInfo prop in Buildings.GetType().GetProperties())
            {
                if (prop.Name == building.ToString())
                {
                    output = (int)prop.GetValue(Buildings);
                }
            }
            if (output == 0)
            {
                foreach (PropertyInfo prop in Facilities.GetType().GetProperties())
                {
                    if (prop.Name == building.ToString())
                    {
                        output = (int)prop.GetValue(Facilities);
                    }
                }
            }
            return output;
        }
        public bool HasMines(Buildings buildings)
        {
            if (
                Buildings.MetalMine >= buildings.MetalMine &&
                Buildings.CrystalMine >= buildings.CrystalMine &&
                Buildings.DeuteriumSynthesizer >= buildings.DeuteriumSynthesizer 

            )
                return true;
            else
                return false;
        }
    }

    public class Moon : Celestial { }

    public class Planet : Celestial
    {
        public bool Administrator { get; set; }
        public bool Inactive { get; set; }
        public bool Vacation { get; set; }
        public bool StrongPlayer { get; set; }
        public bool Newbie { get; set; }
        public bool HonorableTarget { get; set; }
        public bool Banned { get; set; }
        public Player Player { get; set; }
        public Alliance Alliance { get; set; }
        public Temperature Temperature { get; set; }
        public Moon Moon { get; set; }
    }

    public class Settings
    {
        public bool AKS { get; set; }
        public int FleetSpeed { get; set; }
        public bool WreckField { get; set; }
        public string ServerLabel { get; set; }
        public int EconomySpeed { get; set; }
        public int PlanetFields { get; set; }
        public int UniverseSize { get; set; }
        public string ServerCategory { get; set; }
        public bool EspionageProbeRaids { get; set; }
        public int PremiumValidationGift { get; set; }
        public int DebrisFieldFactorShips { get; set; }
        public int DebrisFieldFactorDefence { get; set; }
    }

    public class Server
    {
        public string Language { get; set; }
        public int Number { get; set; }
        public string Name { get; set; }
        public int PlayerCount { get; set; }
        public int PlayersOnline { get; set; }
        public DateTime Opened { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public bool ServerClosed { get; set; }
        public bool Prefered { get; set; }
        public bool SignupClosed { get; set; }
        public Settings Settings { get; set; }
    }

    public class ServerData
    {
        public string Name { get; set; }
        public int Number { get; set; }
        public string Language { get; set; }
        public string Timezone { get; set; }
        public string TimezoneOffset { get; set; }
        public string Domain { get; set; }
        public string Version { get; set; }
        public int Speed { get; set; }
        public int SpeedFleet { get; set; }
        public int SpeedFleetPeaceful { get; set; }
        public int SpeedFleetWar { get; set; }
        public int SpeedFleetHolding { get; set; }
        public int Galaxies { get; set; }
        public int Systems { get; set; }
        public bool ACS { get; set; }
        public bool RapidFire { get; set; }
        public bool DefToTF { get; set; }
        public float DebrisFactor { get; set; }
        public float DebrisFactorDef { get; set; }
        public float RepairFactor { get; set; }
        public int NewbieProtectionLimit { get; set; }
        public int NewbieProtectionHigh { get; set; }
        public long TopScore { get; set; }
        public int BonusFields { get; set; }
        public bool DonutGalaxy { get; set; }
        public bool DonutSystem { get; set; }
        public bool WfEnabled { get; set; }
        public int WfMinimumRessLost { get; set; }
        public int WfMinimumLossPercentage { get; set; }
        public int WfBasicPercentageRepairable { get; set; }
        public float GlobalDeuteriumSaveFactor { get; set; }
        public int Bashlimit { get; set; }
        public int ProbeCargo { get; set; }
        public int ResearchDurationDivisor { get; set; }
        public int DarkMatterNewAcount { get; set; }
        public int CargoHyperspaceTechMultiplier { get; set; }
    }

    public class UserInfo
    {
        public int PlayerID { get; set; }
        public string PlayerName { get; set; }
        public long Points { get; set; }
        public long Rank { get; set; }
        public long Total { get; set; }
        public long HonourPoints { get; set; }
        public Classes Class { get; set; }
    }

    public class Resources
    {
        public Resources(long metal = 0, long crystal = 0, long deuterium = 0, long energy = 0, long darkmatter = 0)
        {
            Metal = metal;
            Crystal = crystal;
            Deuterium = deuterium;
            Energy = energy;
            Darkmatter = darkmatter;
        }
        public long Metal { get; set; }
        public long Crystal { get; set; }
        public long Deuterium { get; set; }
        public long Energy { get; set; }
        public long Darkmatter { get; set; }
        public long ConvertedDeuterium
        {
            get
            {
                return (long)Math.Round((Metal / 2.5) + (Crystal / 1.5) + Deuterium, 0, MidpointRounding.ToPositiveInfinity);
            }
        }
        public long TotalResources
        {
            get
            {
                return Metal + Crystal + Deuterium;
            }
        }
        public override string ToString()
        {
            return "M:" + Metal.ToString("N0") + " C:" + Crystal.ToString("N0") + " D:" + Deuterium.ToString("N0") + " E:" + Energy.ToString("N0") + " DM:" + Darkmatter.ToString("N0");
        }

        public bool IsEnoughFor(Resources cost, Resources resToLeave = null)
        {
            var tempMet = Metal;
            var tempCry = Crystal;
            var tempDeut = Deuterium;
            if (resToLeave != null)
            {
                tempMet -= resToLeave.Metal;
                tempCry -= resToLeave.Crystal;
                tempDeut -= resToLeave.Deuterium;
            }
            if (cost.Metal <= tempMet && cost.Crystal <= tempCry && cost.Deuterium <= tempDeut)
                return true;
            else
                return false;
        }

        public bool IsEmpty()
        {
            if (Metal == 0 && Crystal == 0 && Deuterium == 0)
            {
                return true;
            }
            else return false;
        }

        public Resources Sum(Resources resourcesToSum)
        {
            Resources output = new();
            output.Metal = Metal + resourcesToSum.Metal;
            output.Crystal = Crystal + resourcesToSum.Crystal;
            output.Deuterium = Deuterium + resourcesToSum.Deuterium;

            return output;
        }

        public Resources Difference(Resources resourcesToSubtract)
        {
            Resources output = new();
            output.Metal = Metal - resourcesToSubtract.Metal;
            if (output.Metal < 0)
                output.Metal = 0;
            output.Crystal = Crystal - resourcesToSubtract.Crystal;
            if (output.Crystal < 0)
                output.Crystal = 0;
            output.Deuterium = Deuterium - resourcesToSubtract.Deuterium;
            if (output.Deuterium < 0)
                output.Deuterium = 0;

            return output;
        }
    }

    public class Buildings
    {
        public int MetalMine { get; set; }
        public int CrystalMine { get; set; }
        public int DeuteriumSynthesizer { get; set; }
        public int SolarPlant { get; set; }
        public int FusionReactor { get; set; }
        public int SolarSatellite { get; set; }
        public int MetalStorage { get; set; }
        public int CrystalStorage { get; set; }
        public int DeuteriumTank { get; set; }
        public override string ToString()
        {
            return "M:" + MetalMine.ToString() + " C:" + CrystalMine.ToString() + " D:" + DeuteriumSynthesizer.ToString() + " S:" + SolarPlant.ToString("") + " F:" + FusionReactor.ToString("");
        }
    }

    public class Supplies : Buildings { }

    public class Facilities
    {
        public int RoboticsFactory { get; set; }
        public int Shipyard { get; set; }
        public int ResearchLab { get; set; }
        public int AllianceDepot { get; set; }
        public int MissileSilo { get; set; }
        public int NaniteFactory { get; set; }
        public int Terraformer { get; set; }
        public int SpaceDock { get; set; }
        public int LunarBase { get; set; }
        public int SensorPhalanx { get; set; }
        public int JumpGate { get; set; }
        public override string ToString()
        {
            return "R:" + RoboticsFactory.ToString() + " S:" + Shipyard.ToString() + " L:" + ResearchLab.ToString() + " M:" + MissileSilo.ToString("") + " N:" + NaniteFactory.ToString("");
        }
    }

    public class Defences
    {
        public long RocketLauncher { get; set; }
        public long LightLaser { get; set; }
        public long HeavyLaser { get; set; }
        public long GaussCannon { get; set; }
        public long IonCannon { get; set; }
        public long PlasmaTurret { get; set; }
        public long SmallShieldDome { get; set; }
        public long LargeShieldDome { get; set; }
        public long AntiBallisticMissiles { get; set; }
        public long InterplanetaryMissiles { get; set; }
        public int GetAmount(Buildables defence)
        {
            int output = 0;
            foreach (PropertyInfo prop in GetType().GetProperties())
            {
                if (prop.Name == defence.ToString())
                {
                    output = (int)prop.GetValue(this);
                }
            }
            return output;
        }
    }

    public class Defenses : Defences { }

    public class Ships
    {
        public long LightFighter { get; set; }
        public long HeavyFighter { get; set; }
        public long Cruiser { get; set; }
        public long Battleship { get; set; }
        public long Battlecruiser { get; set; }
        public long Bomber { get; set; }
        public long Destroyer { get; set; }
        public long Deathstar { get; set; }
        public long SmallCargo { get; set; }
        public long LargeCargo { get; set; }
        public long ColonyShip { get; set; }
        public long Recycler { get; set; }
        public long EspionageProbe { get; set; }
        public long SolarSatellite { get; set; }
        public long Crawler { get; set; }
        public long Reaper { get; set; }
        public long Pathfinder { get; set; }

        public Ships(
            long lightFighter = 0,
            long heavyFighter = 0,
            long cruiser = 0,
            long battleship = 0,
            long battlecruiser = 0,
            long bomber = 0,
            long destroyer = 0,
            long deathstar = 0,
            long smallCargo = 0,
            long largeCargo = 0,
            long colonyShip = 0,
            long recycler = 0,
            long espionageProbe = 0,
            long solarSatellite = 0,
            long crawler = 0,
            long reaper = 0,
            long pathfinder = 0
        )
        {
            LightFighter = lightFighter;
            HeavyFighter = heavyFighter;
            Cruiser = cruiser;
            Battleship = battleship;
            Battlecruiser = battlecruiser;
            Bomber = bomber;
            Destroyer = destroyer;
            Deathstar = deathstar;
            SmallCargo = smallCargo;
            LargeCargo = largeCargo;
            ColonyShip = colonyShip;
            Recycler = recycler;
            EspionageProbe = espionageProbe;
            SolarSatellite = solarSatellite;
            Crawler = crawler;
            Reaper = reaper;
            Pathfinder = pathfinder;
        }
        public bool IsEmpty()
        {
            if
            (
                LightFighter == 0 &&
                HeavyFighter == 0 &&
                Cruiser == 0 &&
                Battleship == 0 &&
                Battlecruiser == 0 &&
                Bomber == 0 &&
                Destroyer == 0 &&
                Deathstar == 0 &&
                SmallCargo == 0 &&
                LargeCargo == 0 &&
                ColonyShip == 0 &&
                Recycler == 0 &&
                EspionageProbe == 0 &&
                SolarSatellite == 0 &&
                Crawler == 0 &&
                Reaper == 0 &&
                Pathfinder == 0
            )
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public long GetFleetPoints()
        {
            long output = 0;
            output += LightFighter * 4;
            output += HeavyFighter * 10;
            output += Cruiser * 29;
            output += Battleship * 60;
            output += Battlecruiser * 85;
            output += Bomber * 90;
            output += Destroyer * 125;
            output += Deathstar * 10000;
            output += SmallCargo * 4;
            output += LargeCargo * 12;
            output += ColonyShip * 40;
            output += Recycler * 18;
            output += EspionageProbe * 1;
            output += Reaper * 160;
            output += Pathfinder * 31;
            return output;
        }

        public bool HasMovableFleet()
        {
            if
            (
                LightFighter == 0 &&
                HeavyFighter == 0 &&
                Cruiser == 0 &&
                Battleship == 0 &&
                Battlecruiser == 0 &&
                Bomber == 0 &&
                Destroyer == 0 &&
                Deathstar == 0 &&
                SmallCargo == 0 &&
                LargeCargo == 0 &&
                ColonyShip == 0 &&
                Recycler == 0 &&
                EspionageProbe == 0 &&
                Reaper == 0 &&
                Pathfinder == 0
            )
            {
                return false;
            }
            else
            {
                return true;
            }
        }

        public Ships GetMovableShips()
        {
            Ships tempShips = this;
            tempShips.SolarSatellite = 0;
            tempShips.Crawler = 0;
            return tempShips;
        }

        public Ships Add(Buildables buildable, long quantity)
        {
            foreach (PropertyInfo prop in this.GetType().GetProperties())
            {
                if (prop.Name == buildable.ToString())
                {
                    prop.SetValue(this, (long)prop.GetValue(this) + quantity);
                }
            }
            return this;
        }

        public Ships Remove(Buildables buildable, int quantity)
        {
            foreach (PropertyInfo prop in this.GetType().GetProperties())
            {
                if (prop.Name == buildable.ToString())
                {
                    long val = (long)prop.GetValue(this);
                    if (val >= quantity)
                        prop.SetValue(this, val);
                    else
                        prop.SetValue(this, 0);
                }
            }
            return this;
        }

        public long GetAmount(Buildables buildable)
        {
            foreach (PropertyInfo prop in this.GetType().GetProperties())
            {
                if (prop.Name == buildable.ToString())
                {
                    return (long)prop.GetValue(this);
                }
            }
            return 0;
        }

        public void SetAmount(Buildables buildable, long number)
        {
            foreach (PropertyInfo prop in this.GetType().GetProperties())
            {
                if (prop.Name == buildable.ToString())
                {
                    prop.SetValue(this, number);
                    return;
                }
            }
        }

        public bool HasAtLeast(Ships ships, long times = 1)
        {
            foreach (PropertyInfo prop in this.GetType().GetProperties())
            {
                if ((long)prop.GetValue(this) * times < (long)prop.GetValue(ships))
                {
                    return false;
                }
            }
            return true;
        }

        public override string ToString()
        {
            string output = "";
            foreach (PropertyInfo prop in this.GetType().GetProperties())
            {
                if ((long)prop.GetValue(this) == 0)
                    continue;
                output += prop.Name + ": " + prop.GetValue(this) + "; ";
            }
            return output;
        }
    }

    public class FleetPrediction
    {
        public long Time { get; set; }
        public long Fuel { get; set; }
    }

    public class Fleet
    {
        public Missions Mission { get; set; }
        public bool ReturnFlight { get; set; }
        public bool InDeepSpace { get; set; }
        public int ID { get; set; }
        public Resources Resources { get; set; }
        public Coordinate Origin { get; set; }
        public Coordinate Destination { get; set; }
        public Ships Ships { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime ArrivalTime { get; set; }
        public DateTime? BackTime { get; set; }
        public int ArriveIn { get; set; }
        public int? BackIn { get; set; }
        public int? UnionID { get; set; }
        public int TargetPlanetID { get; set; }
    }
    public class AttackerFleet
    {
        public int ID { get; set; }
        public Missions MissionType { get; set; }
        public Coordinate Origin { get; set; }
        public Coordinate Destination { get; set; }
        public string DestinationName { get; set; }
        public DateTime ArrivalTime { get; set; }
        public int ArriveIn { get; set; }
        public string AttackerName { get; set; }
        public int AttackerID { get; set; }
        public int UnionID { get; set; }
        public int Missiles { get; set; }
        public Ships Ships { get; set; }

        public bool IsOnlyProbes()
        {
            if (Ships.EspionageProbe != 0)
            {
                if
                (
                    Ships.Battlecruiser == 0 &&
                    Ships.Battleship == 0 &&
                    Ships.Bomber == 0 &&
                    Ships.ColonyShip == 0 &&
                    Ships.Cruiser == 0 &&
                    Ships.Deathstar == 0 &&
                    Ships.Destroyer == 0 &&
                    Ships.HeavyFighter == 0 &&
                    Ships.LargeCargo == 0 &&
                    Ships.LightFighter == 0 &&
                    Ships.Pathfinder == 0 &&
                    Ships.Reaper == 0 &&
                    Ships.Recycler == 0 &&
                    Ships.SmallCargo == 0 &&
                    Ships.SolarSatellite == 0
                )
                    return true;
                else
                    return false;
            }
            else
                return false;
        }
    }
    public class Slots
    {
        public int InUse { get; set; }
        public int Total { get; set; }
        public int ExpInUse { get; set; }
        public int ExpTotal { get; set; }
        public int Free
        {
            get
            {
                return Total - InUse;
            }
        }
        public int ExpFree
        {
            get
            {
                return ExpTotal - ExpInUse;
            }
        }
    }

    public class Researches
    {
        public int EnergyTechnology { get; set; }
        public int LaserTechnology { get; set; }
        public int IonTechnology { get; set; }
        public int HyperspaceTechnology { get; set; }
        public int PlasmaTechnology { get; set; }
        public int CombustionDrive { get; set; }
        public int ImpulseDrive { get; set; }
        public int HyperspaceDrive { get; set; }
        public int EspionageTechnology { get; set; }
        public int ComputerTechnology { get; set; }
        public int Astrophysics { get; set; }
        public int IntergalacticResearchNetwork { get; set; }
        public int GravitonTechnology { get; set; }
        public int WeaponsTechnology { get; set; }
        public int ShieldingTechnology { get; set; }
        public int ArmourTechnology { get; set; }
        public int GetLevel(Buildables research)
        {
            int output = 0;
            foreach (PropertyInfo prop in GetType().GetProperties())
            {
                if (prop.Name == research.ToString())
                {
                    output = (int)prop.GetValue(this);
                }
            }
            return output;
        }
    }

    public class Production
    {
        public int ID { get; set; }
        public int Nbr { get; set; }
    }

    public class Constructions
    {
        public int BuildingID { get; set; }
        public int BuildingCountdown { get; set; }
        public int ResearchID { get; set; }
        public int ResearchCountdown { get; set; }
    }

    public class Techs
    {
        public Defences defenses { get; set; }
        public Facilities facilities { get; set; }
        public Researches researches { get; set; }
        public Ships ships { get; set; }
        public Buildings supplies { get; set; }
    }

    public class Debris
    {
        public long Metal { get; set; }
        public long Crystal { get; set; }
        public long RecyclersNeeded { get; set; }
        public Resources Resources
        {
            get
            {
                return new Resources
                {
                    Metal = Metal,
                    Crystal = Crystal,
                    Deuterium = 0,
                    Darkmatter = 0,
                    Energy = 0
                };
            }
        }
    }

    public class ExpeditionDebris
    {
        public long Metal { get; set; }
        public long Crystal { get; set; }
        public long PathfindersNeeded { get; set; }
        public Resources Resources
        {
            get
            {
                return new Resources
                {
                    Metal = Metal,
                    Crystal = Crystal,
                    Deuterium = 0,
                    Darkmatter = 0,
                    Energy = 0
                };
            }
        }
    }

    public class Player
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public int Rank { get; set; }
        public bool IsBandit { get; set; }
        public bool IsStarlord { get; set; }
    }

    public class Alliance
    {
        public int ID { get; set; }
        public string Name { get; set; }
        public int Rank { get; set; }
        public int Member { get; set; }
    }

    public class GalaxyInfo
    {
        public int Galaxy { get; set; }
        public int System { get; set; }
        public List<Planet> Planets { get; set; }
        public ExpeditionDebris ExpeditionDebris { get; set; }
    }

    public class BuildTask
    {
        public Celestial Celestial { get; set; }
        public Buildables Buildable { get; set; }
        public int Level { get; set; }
        public Resources Price { get; set; }
    }

    public class FleetSchedule: FleetHypotesis
    {
        public Resources Payload { get; set; }
        public DateTime Departure { get; set; }
        public DateTime Arrival { get; set; }
        public DateTime Comeback { get; set; }
        public DateTime SendAt { get; set; }
        public DateTime RecallAt { get; set; }
        public DateTime ReturnAt { get; set; }
    }

    public class FleetHypotesis
    {
        public Celestial Origin { get; set; }
        public Coordinate Destination { get; set; }
        public Ships Ships { get; set; }
        public Missions Mission { get; set; }
        public decimal Speed { get; set; }
        public long Duration { get; set; }
        public long Fuel { get; set; }
    }

    public class ProxySettings
    {
        public bool Enabled { get; set; }
        public string Address { get; set; }
        public string Type { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
		public bool LoginOnly { get; set; }
    }

    public class Staff
    {
        public Staff()
        {
            Commander = false;
            Admiral = false;
            Engineer = false;
            Geologist = false;
            Technocrat = false;
        }
        public bool Commander { get; set; }
        public bool Admiral { get; set; }
        public bool Engineer { get; set; }
        public bool Geologist { get; set; }
        public bool Technocrat { get; set; }
        public bool IsFull {
            get {
                if (Commander && Admiral && Engineer && Geologist && Technocrat)
                    return true;
                else
                    return false;
            }
        }
    }

    public class Resource
    {
        public long Available { get; set; }
        public long StorageCapacity { get; set; }
        public long CurrentProduction { get; set; }
    }

    public class Energy
    {
        public long Available { get; set; }
        public long CurrentProduction { get; set; }
        public long Consumption { get; set; }
    }

    public class Darkmatter
    {
        public long Available { get; set; }
        public long Purchased { get; set; }
        public long Found { get; set; }
    }    

    public class ResourcesProduction{
        public Resource Metal { get; set; }
        public Resource Crystal { get; set; }
        public Resource Deuterium { get; set; }
        public Energy Energy { get; set; }
        public Darkmatter Darkmatter { get; set; }
    }

    public class AutoMinerSettings
    {
        public bool OptimizeForStart { get; set; }
        public bool PrioritizeRobotsAndNanites { get; set; }
        public int MaxDaysOfInvestmentReturn { get; set; }
        public int DepositHours { get; set; }
        public bool BuildDepositIfFull { get; set; }
        public AutoMinerSettings()
        {
            OptimizeForStart = true;
            PrioritizeRobotsAndNanites = false;
            MaxDaysOfInvestmentReturn = 36500;
            DepositHours = 6;
            BuildDepositIfFull = false;
        }
    }

}
