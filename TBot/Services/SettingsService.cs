using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Tbot.Model;

namespace Tbot.Services {

	public class Proxy {
		public bool Enabled { get; set; }

		public string Address { get; set; }

		public string Type { get; set; }

		public string Username { get; set; }

		public string Password { get; set; }

		public bool LoginOnly { get; set; }
	}

	public class General {
		public string UserAgent { get; set; }

		public string Host { get; set; }

		public string Port { get; set; }

		public Proxy Proxy { get; set; }

		public string CaptchaAPIKey { get; set; }

		public string CustomTitle { get; set; }

		public int SlotsToLeaveFree { get; set; }
	}

	public class Activable {
		public bool Active { get; set; }
	}

	public class ActivableWithCheckInterval : Activable {
		public int CheckIntervalMin { get; set; }

		public int CheckIntervalMax { get; set; }
	}

	public class AutoFleetSave : Activable {
		public bool OnlyMoons { get; set; }

		public bool ForceUnsafe { get; set; }

		public long DeutToLeave { get; set; }

		public bool Recall { get; set; }
	}

	public class SleepMode : Activable {
		public string GoToSleep { get; set; }

		public string WakeUp { get; set; }

		public bool PreventIfThereAreFleets { get; set; }

		public Activable TelegramMessenger { get; set; }

		public AutoFleetSave AutoFleetSave { get; set; }
	}

	public class Autofleet : Activable {
		public Activable TelegramMessenger { get; set; }
	}

	public class SpyAttacker : Activable {
		public int Probes;
	}

	public class MessageAttacker : Activable {
		public List<string> Messages;
	}

	public class Defender : ActivableWithCheckInterval {
		public bool IgnoreProbes { get; set; }

		public bool IgnoreMissiles { get; set; }

		public bool IgnoreWeakAttack { get; set; }

		public int WeakAttackRatio { get; set; }

		public Autofleet Autofleet { get; set; }

		public List<long> WhiteList { get; set; }

		public SpyAttacker SpyAttacker { get; set; }

		public MessageAttacker MessageAttacker { get; set; }

		public Activable TelegramMessenger { get; set; }

		public Activable Alarm { get; set; }
	}

	public class CelestialPosition {
		public int Galaxy { get; set; }

		public int System { get; set; }

		public int Position { get; set; }
	}

	public class SettingsCelestial : CelestialPosition {
		public string Type { get; set; }
	}

	public class Transports : Activable {
		public string CargoType { get; set; }

		public long DeutToLeave { get; set; }

		public bool RoundResources { get; set; }

		public SettingsCelestial Origin { get; set; }
	}

	public class AutoMine : ActivableWithCheckInterval {
		public int MaxMetalMine { get; set; }

		public int MaxCrystalMine { get; set; }

		public int MaxDeuteriumSynthetizer { get; set; }

		public int MaxSolarPlant { get; set; }

		public int MaxFusionReactor { get; set; }

		public int MaxMetalStorage { get; set; }

		public int MaxCrystalStorage { get; set; }

		public int MaxDeuteriumTank { get; set; }

		public int MaxRoboticsFactory { get; set; }

		public int MaxShipyard { get; set; }

		public int MaxResearchLab { get; set; }

		public int MaxMissileSilo { get; set; }

		public int MaxNaniteFactory { get; set; }

		public int MaxTerraformer { get; set; }

		public int MaxSpaceDock { get; set; }

		public int MaxLunarBase { get; set; }

		public int MaxLunarShipyard { get; set; }

		public int MaxLunarRoboticsFactory { get; set; }

		public int MaxSensorPhalanx { get; set; }

		public int MaxJumpGate { get; set; }

		public Transports Transports { get; set; }

		public bool RandomOrder { get; set; }

		public List<SettingsCelestial> Exclude { get; set; }

		public bool OptimizeForStart { get; set; }

		public bool PrioritizeRobotsAndNanites { get; set; }

		public bool PrioritizeRobotsAndNanitesOnNewPlanets { get; set; }

		public bool BuildDepositIfFull { get; set; }

		public int DepositHours { get; set; }

		public float MaxDaysOfInvestmentReturn { get; set; }

		public int DeutToLeaveOnMoons { get; set; }
	}

	public class AutoResearch : ActivableWithCheckInterval {
		public int MaxEnergyTechnology { get; set; }

		public int MaxLaserTechnology { get; set; }

		public int MaxIonTechnology { get; set; }

		public int MaxHyperspaceTechnology { get; set; }

		public int MaxPlasmaTechnology { get; set; }

		public int MaxCombustionDrive { get; set; }

		public int MaxImpulseDrive { get; set; }

		public int MaxHyperspaceDrive { get; set; }

		public int MaxEspionageTechnology { get; set; }

		public int MaxComputerTechnology { get; set; }

		public int MaxAstrophysics { get; set; }

		public int MaxIntergalacticResearchNetwork { get; set; }

		public int MaxWeaponsTechnology { get; set; }

		public int MaxShieldingTechnology { get; set; }

		public int MaxArmourTechnology { get; set; }

		public SettingsCelestial Target { get; set; }

		public Transports Transports { get; set; }

		public bool OptimizeForStart { get; set; }

		public bool EnsureExpoSlots { get; set; }

		public bool PrioritizeAstrophysics { get; set; }

		public bool PrioritizePlasmaTechnology { get; set; }

		public bool PrioritizeEnergyTechnology { get; set; }

		public bool PrioritizeIntergalacticResearchNetwork { get; set; }
	}

	public class AutoCargo : ActivableWithCheckInterval {
		public bool ExcludeMoons { get; set; }

		public string CargoType { get; set; }

		public bool RandomOrder { get; set; }

		public int MaxCargosToBuild { get; set; }

		public int MaxCargosToKeep { get; set; }

		public bool LimitToCapacity { get; set; }

		public bool SkipIfIncomingTransport { get; set; }

		public List<SettingsCelestial> Exclude { get; set; }
	}

	public class LeaveDeut {
		public bool OnlyOnMoons { get; set; }

		public long DeutToLeave { get; set; }
	}

	public class AutoRepatriate : ActivableWithCheckInterval {
		public bool ExcludeMoons { get; set; }

		public long MinimumResources { get; set; }

		public LeaveDeut LeaveDeut { get; set; }

		public SettingsCelestial Target { get; set; }

		public string CargoType { get; set; }

		public bool RandomOrder { get; set; }

		public bool SkipIfIncomingTransport { get; set; }

		public List<SettingsCelestial> Exclude { get; set; }
	}

	public class Brain : Activable {
		public AutoMine AutoMine { get; set; }

		public AutoResearch AutoResearch { get; set; }

		public AutoCargo AutoCargo { get; set; }

		public AutoRepatriate AutoRepatriate { get; set; }

		public ActivableWithCheckInterval BuyOfferOfTheDay { get; set; }
	}

	public class ManualShips : Activable {
		public Ships Ships { get; set; }
	}

	public class SplitExpeditionsBetweenSystems : Activable {
		public int Range { get; set; }
	}

	public class Expeditions : Activable {
		public string PrimaryShip { get; set; }

		public int MinPrimaryToSend { get; set; }

		public int PrimaryToKeep { get; set; }

		public string SecondaryShip { get; set; }

		public int MinSecondaryToKeep { get; set; }

		public int MinSecondaryToSend { get; set; }

		public float SecondaryToPrimaryRatio { get; set; }

		public ManualShips ManualShips { get; set; }

		public bool WaitForAllExpeditions { get; set; }

		public bool WaitForMajorityOfExpeditions { get; set; }

		public SplitExpeditionsBetweenSystems SplitExpeditionsBetweenSystems { get; set; }

		public bool RandomizeOrder { get; set; }

		public int FuelToCarry { get; set; }

		public List<SettingsCelestial> Origin { get; set; }
	}

	public class ScanRange {
		public int Galaxy { get; set; }

		public int StartSystem { get; set; }

		public int EndSystem { get; set; }
	}

	public class AutoFarm : ActivableWithCheckInterval {
		public bool ExcludeMoons { get; set; }

		public List<ScanRange> ScanRange { get; set; }

		public List<CelestialPosition> Exclude { get; set; }

		public int KeepReportFor { get; set; }

		public int NumProbes { get; set; }

		public List<SettingsCelestial> Origin { get; set; }

		public int TargetsProbedBeforeAttack { get; set; }

		public string CargoType { get; set; }

		public int FleetSpeed { get; set; }

		public int MinCargosToKeep { get; set; }

		public int MinCargosToSend { get; set; }

		public double CargoSurplusPercentage { get; set; }

		public bool BuildCargos { get; set; }

		public bool BuildProbes { get; set; }

		public long MinimumResources { get; set; }

		public int MinimumPlayerRank { get; set; }

		public long MaxFlightTime { get; set; }

		public long MaxWaitTime { get; set; }

		public double MinLootFuelRatio { get; set; }

		public string PreferedResource { get; set; }

		public int SlotsToLeaveFree { get; set; }
	}

	public class AutoHarvest : ActivableWithCheckInterval {
		public bool HarvestOwnDF { get; set; }

		public bool HarvestDeepSpace { get; set; }

		public long MinimumResourcesOwnDF { get; set; }

		public long MinimumResourcesDeepSpace { get; set; }
	}

	public class AutoColonize : ActivableWithCheckInterval {
		public SettingsCelestial Origin { get; set; }

		public int SlotsToLeaveFree { get; set; }

		public List<CelestialPosition> Targets { get; set; }
	}

	public class SettingsTelegramMessenger : Activable {
		public string API { get; set; }

		public string ChatId { get; set; }
	}

	public class BasicAuth {
		public string Username { get; set; }

		public string Password { get; set; }
	}

	public class SettingsCredentials {
		public string Universe { get; set; }

		public string Email { get; set; }

		public string Password { get; set; }

		public string Language { get; set; }

		public bool LobbyPioneers { get; set; }

		public BasicAuth BasicAuth { get; set; }
	}

	public class ConfigSettings {

		public SettingsCredentials Credentials { get; set; }

		public General General { get; set; }

		public SleepMode SleepMode { get; set; }

		public Defender Defender { get; set; }

		public Brain Brain { get; set; }

		public Expeditions Expeditions { get; set; }

		public AutoFarm AutoFarm { get; set; }

		public AutoHarvest AutoHarvest { get; set; }

		public AutoColonize AutoColonize { get; set; }

		public SettingsTelegramMessenger TelegramMessenger { get; set; }
	}

	public static class SettingsService {
		public static ConfigSettings GetSettings() {
			string file = File.ReadAllText($"{Path.GetFullPath(AppContext.BaseDirectory)}/settings.json");
			return JsonSerializer.Deserialize<ConfigSettings>(file);
		}
	}
}
