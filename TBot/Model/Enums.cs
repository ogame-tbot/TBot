using System.Collections.Generic;

namespace Tbot.Model {
	public enum CharacterClass {
		NoClass = 0,
		Collector = 1,
		General = 2,
		Discoverer = 3
	}

	public enum AllianceClass {
		NoClass = 0,
		Warrior = 1,
		Trader = 2,
		Researcher = 3
	}

	public enum Celestials {
		Planet = 1,
		Debris = 2,
		Moon = 3,
		DeepSpace = 4
	}

	public enum Buildables {
		Null = 0,
		MetalMine = 1,
		CrystalMine = 2,
		DeuteriumSynthesizer = 3,
		SolarPlant = 4,
		FusionReactor = 12,
		MetalStorage = 22,
		CrystalStorage = 23,
		DeuteriumTank = 24,
		ShieldedMetalDen = 25,
		UndergroundCrystalDen = 26,
		SeabedDeuteriumDen = 27,
		AllianceDepot = 34,
		RoboticsFactory = 14,
		Shipyard = 21,
		ResearchLab = 31,
		MissileSilo = 44,
		NaniteFactory = 15,
		Terraformer = 33,
		SpaceDock = 36,
		LunarBase = 41,
		SensorPhalanx = 42,
		JumpGate = 43,
		RocketLauncher = 401,
		LightLaser = 402,
		HeavyLaser = 403,
		GaussCannon = 404,
		IonCannon = 405,
		PlasmaTurret = 406,
		SmallShieldDome = 407,
		LargeShieldDome = 408,
		AntiBallisticMissiles = 502,
		InterplanetaryMissiles = 503,
		SmallCargo = 202,
		LargeCargo = 203,
		LightFighter = 204,
		HeavyFighter = 205,
		Cruiser = 206,
		Battleship = 207,
		ColonyShip = 208,
		Recycler = 209,
		EspionageProbe = 210,
		Bomber = 211,
		SolarSatellite = 212,
		Destroyer = 213,
		Deathstar = 214,
		Battlecruiser = 215,
		Crawler = 217,
		Reaper = 218,
		Pathfinder = 219,
		EspionageTechnology = 106,
		ComputerTechnology = 108,
		WeaponsTechnology = 109,
		ShieldingTechnology = 110,
		ArmourTechnology = 111,
		EnergyTechnology = 113,
		HyperspaceTechnology = 114,
		CombustionDrive = 115,
		ImpulseDrive = 117,
		HyperspaceDrive = 118,
		LaserTechnology = 120,
		IonTechnology = 121,
		PlasmaTechnology = 122,
		IntergalacticResearchNetwork = 123,
		Astrophysics = 124,
		GravitonTechnology = 199
	}

	public enum LFTypes {
		None = 0,
		Humans = 1,
		Rocktal = 2,
		Mechas = 3,
		Kaelesh = 4
	}

	public enum LFBuildables {
		Null = 0,
		//Humans
		ResidentialSector = 11101,
		BiosphereFarm = 11102,
		ResearchCentre = 11103,
		AcademyOfSciences = 11104,
		NeuroCalibrationCentre = 11105,
		HighEnergySmelting = 11106,
		FoodSilo = 11107,
		FusionPoweredProduction = 11108,
		Skyscraper = 11109,
		BiotechLab = 11110,
		Metropolis = 11111,
		PlanetaryShield = 11112,

		//Rocktal
		MeditationEnclave = 12101,
		CrystalFarm = 12102,
		RuneTechnologium = 12103,
		RuneForge = 12104,
		Oriktorium = 12105,
		MagmaForge = 12106,
		DisruptionChamber = 12107,
		Megalith = 12108,
		CrystalRefinery = 12109,
		DeuteriumSynthesiser = 12110,
		MineralResearchCentre = 12111,
		MetalRecyclingPlant = 12112,

		//Mechas
		AssemblyLine = 13101,
		FusionCellFactory = 13102,
		RoboticsResearchCentre = 13103,
		UpdateNetwork = 12304,
		QuantumComputerCentre = 13105,
		AutomatisedAssemblyCentre = 13106,
		HighPerformanceTransformer = 13107,
		MicrochipAssemblyLine = 13108,
		ProductionAssemblyHall = 13109,
		HighPerformanceSynthesiser = 13110,
		ChipMassProduction = 13111,
		NanoRepairBots = 13112,

		//Kaelesh
		Sanctuary = 14101,
		AntimatterCondenser = 14102,
		VortexChamber = 14103,
		HallsOfRealisation = 14104,
		ForumOfTranscendence = 14105,
		AntimatterConvector = 14106,
		CloningLaboratory = 14107,
		ChrysalisAccelerator = 14108,
		BioModifier = 14109,
		PsionicModulator = 14110,
		ShipManufacturingHall = 14111,
		SupraRefractor = 14112

	}

	//Used only with Helpers.GetLessExpensiveLFBuilding()
	public enum HumansBuildables {
		ResearchCentre = 11103,
		HighEnergySmelting = 11106,
		FoodSilo = 11107,
		FusionPoweredProduction = 11108,
		Skyscraper = 11109,
		BiotechLab = 11110,
		Metropolis = 11111,
		PlanetaryShield = 11112
	}

	public enum RocktalBuildables {
		RuneTechnologium = 12103,
		MagmaForge = 12106,
		DisruptionChamber = 12107,
		Megalith = 12108,
		CrystalRefinery = 12109,
		DeuteriumSynthesiser = 12110,
		MineralResearchCentre = 12111,
		MetalRecyclingPlant = 12112
	}

	public enum MechasBuildables {
		RoboticsResearchCentre = 13103,
		AutomatisedAssemblyCentre = 13106,
		HighPerformanceTransformer = 13107,
		MicrochipAssemblyLine = 13108,
		ProductionAssemblyHall = 13109,
		HighPerformanceSynthesiser = 13110,
		ChipMassProduction = 13111,
		NanoRepairBots = 13112
	}

	public enum KaeleshBuildables {
		VortexChamber = 14103,
		AntimatterConvector = 14106,
		CloningLaboratory = 14107,
		ChrysalisAccelerator = 14108,
		BioModifier = 14109,
		PsionicModulator = 14110,
		ShipManufacturingHall = 14111,
		SupraRefractor = 14112
	}

	public enum LFTechno {
		None = 0,
		//Humans
		IntergalacticEnvoys = 11201,
		HighPerformanceExtractors = 11202,
		FusionDrives = 11203,
		StealthFieldGenerator = 11204,
		OrbitalDen = 11205,
		ResearchAI = 11206,
		HighPerformanceTerraformer = 11207,
		EnhancedProductionTechnologies = 11208,
		LightFighterMkII = 11209,
		CruiserMkII = 11210,
		ImprovedLabTechnology = 11211,
		PlasmaTerraformer = 11212,
		LowTemperatureDrives = 11213,
		BomberMkII = 11214,
		DestroyerMkII = 11215,
		BattlecruiserMkII = 11216,
		RobotAssistants = 11217,
		Supercomputer = 11218,

		//Rocktal
		VolcanicBatteries = 12201,
		AcousticScanning = 12202,
		HighEnergyPumpSystems = 12203,
		CargoHoldExpansionCivilianShips = 12204,
		MagmaPoweredProduction = 12205,
		GeothermalPowerPlants = 12206,
		DepthSounding = 12207,
		IonCrystalEnhancementHeavyFighter = 12208,
		ImprovedStellarator = 12209,
		HardenedDiamondDrillHeads = 12210,
		SeismicMiningTechnology = 12211,
		MagmaPoweredPumpSystems = 12212,
		IonCrystalModules = 12213,
		OptimisedSiloConstructionMethod = 12214,
		DiamondEnergyTransmitter = 12215,
		ObsidianShieldReinforcement = 12216,
		RuneShields = 12217,
		RocktalCollectorEnhancement = 12218,

		//Mechas
		CatalyserTechnology = 13201,
		PlasmaDrive = 13202,
		EfficiencyModule = 13203,
		DepotAI = 13204,
		GeneralOverhaulLightFighter = 13205,
		AutomatedTransportLines = 13206,
		ImprovedDroneAI = 13207,
		ExperimentalRecyclingTechnology = 13208,
		GeneralOverhaulCruiser = 13209,
		SlingshotAutopilot = 13210,
		HighTemperatureSuperconductors = 13211,
		GeneralOverhaulBattleship = 13212,
		ArtificialSwarmIntelligence = 13213,
		GeneralOverhaulBattlecruiser = 13214,
		GeneralOverhaulBomber = 13215,
		GeneralOverhaulDestroyer = 13216,
		ExperimentalWeaponsTechnology = 13217,
		MechanGeneralEnhancement = 13218,

		//Kaelesh
		HeatRecovery = 14201,
		SulphideProcess = 14202,
		PsionicNetwork = 14203,
		TelekineticTractorBeam = 14204,
		EnhancedSensorTechnology = 14205,
		NeuromodalCompressor = 14206,
		NeuroInterface = 14207,
		InterplanetaryAnalysisNetwork = 14208,
		OverclockingHeavyFighter = 14209,
		TelekineticDrive = 14210,
		SixthSense = 14211,
		Psychoharmoniser = 14212,
		EfficientSwarmIntelligence = 14213,
		OverclockingLargeCargo = 14214,
		GravitationSensors = 14215,
		OverclockingBattleship = 14216,
		PsionicShieldMatrix = 14217,
		KaeleshDiscovererEnhancement = 14218
	}

	public enum Missions {
		None = 0,
		Attack = 1,
		FederalAttack = 2,
		Transport = 3,
		Deploy = 4,
		FederalDefense = 5,
		Spy = 6,
		Colonize = 7,
		Harvest = 8,
		Destroy = 9,
		MissileAttack = 10,
		Expedition = 15,
		Trade = 16
	}

	public static class Speeds {
		public const decimal FivePercent = 0.5M;
		public const decimal TenPercent = 1;
		public const decimal FifteenPercent = 1.5M;
		public const decimal TwentyPercent = 2;
		public const decimal TwentyfivePercent = 2.5M;
		public const decimal ThirtyPercent = 3;
		public const decimal ThirtyfivePercent = 3.5M;
		public const decimal FourtyPercent = 4;
		public const decimal FourtyfivePercent = 4.5M;
		public const decimal FiftyPercent = 5;
		public const decimal FiftyfivePercent = 5.5M;
		public const decimal SixtyPercent = 6;
		public const decimal SixtyfivePercent = 6.5M;
		public const decimal SeventyPercent = 7;
		public const decimal SeventyfivePercent = 7.5M;
		public const decimal EightyPercent = 8;
		public const decimal EightyfivePercent = 8.5M;
		public const decimal NinetyPercent = 9;
		public const decimal NinetyfivePercent = 9.5M;
		public const decimal HundredPercent = 10;

		public static List<decimal> GetGeneralSpeedsList() {
			/* TODO: fix this
			return new()
			{
				10,
				9.5M,
				9,
				8.5M,
				8,
				7.5M,
				7,
				6.5M,
				6,
				5.5M,
				5,
				4.5M,
				4,
				3.5M,
				3,
				2.5M,
				2,
				1.5M,
				1,
				0.5M
			};
			*/
			return new()
			{
				10,
				9,
				8,
				7,
				6,
				5,
				4,
				3,
				2,
				1
			};
		}

		public static List<decimal> GetNonGeneralSpeedsList() {
			return new()
			{
				10,
				9,
				8,
				7,
				6,
				5,
				4,
				3,
				2,
				1
			};
		}
	}

	public enum IntervalType {
		LessThanASecond,
		LessThanFiveSeconds,
		AFewSeconds,
		SomeSeconds,
		AMinuteOrTwo,
		AboutFiveMinutes,
		AboutTenMinutes,
		AboutAQuarterHour,
		AboutHalfAnHour,
		AboutAnHour
	}

	public enum UpdateTypes {
		Fast,
		Techs,
		Full,
		Resources,
		Buildings,
		LFBuildings,
		LFTechs,
		Ships,
		Facilities,
		Defences,
		Productions,
		Constructions,
		ResourceSettings,
		ResourcesProduction,
		Debris
	}

	public enum EspionageReportType {
		Action = 0,
		Report = 1
	}

	public enum FarmState {
		/// Target listed, no action taken or to be taken.
		Idle,
		/// Espionage probes are to be sent to this target.
		ProbesPending,
		/// Espionage probes are sent, no report received yet.
		ProbesSent,
		/// Additional espionage probes are required for more info.
		ProbesRequired,
		/// Additional espionage probes were sent, but insufficient, more required.
		FailedProbesRequired,
		/// Suitable target detected, attack is pending.
		AttackPending,
		/// Suitable target detected, attack is ongoing.
		AttackSent,
		/// Target not suitable (insufficient resources / too much defense / insufficicent information available).
		NotSuitable
	}

	public enum LogType {
		Info,
		Debug,
		Warning,
		Error
	}

	public enum LogSender {
		Tbot,
		OGameD,
		Defender,
		Brain,
		Expeditions,
		Harvest,
		FleetScheduler,
		SleepMode,
		Colonize,
		AutoFarm,
		Telegram
	}

	public enum Feature {
		Null = 0,
		Defender = 1,
		Brain = 2,
		BrainAutobuildCargo = 3,
		BrainAutoRepatriate = 4,
		BrainAutoMine = 5,
		BrainOfferOfTheDay = 6,
		Expeditions = 7,
		Harvest = 8,
		FleetScheduler = 9,
		SleepMode = 10,
		BrainAutoResearch = 11,
		Colonize = 12,
		AutoFarm = 13,
		TelegramAutoPing = 14,
		TelegramAuction = 15,
		BrainLifeformAutoMine = 16
	}

	public enum SendFleetCode : int {
		GenericError = 0,
		AfterSleepTime = -1,
		NotEnoughSlots = -2
	}
}
