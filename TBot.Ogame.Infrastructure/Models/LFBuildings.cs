using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBuildings {
		public int LifeformType { get; set; }

		//humans
		public int ResidentialSector { get; set; }
		public int BiosphereFarm { get; set; }
		public int ResearchCentre { get; set; }
		public int AcademyOfSciences { get; set; }
		public int NeuroCalibrationCentre { get; set; }
		public int HighEnergySmelting { get; set; }
		public int FoodSilo { get; set; }
		public int FusionPoweredProduction { get; set; }
		public int Skyscraper { get; set; }
		public int BiotechLab { get; set; }
		public int Metropolis { get; set; }
		public int PlanetaryShield { get; set; }

		//Rocktal
		public int MeditationEnclave { get; set; }
		public int CrystalFarm { get; set; }
		public int RuneTechnologium { get; set; }
		public int RuneForge { get; set; }
		public int Oriktorium { get; set; }
		public int MagmaForge { get; set; }
		public int DisruptionChamber { get; set; }
		public int Megalith { get; set; }
		public int CrystalRefinery { get; set; }
		public int DeuteriumSynthesiser { get; set; }
		public int MineralResearchCentre { get; set; }
		public int AdvancedRecyclingPlant { get; set; }

		//Mechas
		public int AssemblyLine { get; set; }
		public int FusionCellFactory { get; set; }
		public int RoboticsResearchCentre { get; set; }
		public int UpdateNetwork { get; set; }
		public int QuantumComputerCentre { get; set; }
		public int AutomatisedAssemblyCentre { get; set; }
		public int HighPerformanceTransformer { get; set; }
		public int MicrochipAssemblyLine { get; set; }
		public int ProductionAssemblyHall { get; set; }
		public int HighPerformanceSynthesiser { get; set; }
		public int ChipMassProduction { get; set; }
		public int NanoRepairBots { get; set; }

		//Kaelesh
		public int Sanctuary { get; set; }
		public int AntimatterCondenser { get; set; }
		public int VortexChamber { get; set; }
		public int HallsOfRealisation { get; set; }
		public int ForumOfTranscendence { get; set; }
		public int AntimatterConvector { get; set; }
		public int CloningLaboratory { get; set; }
		public int ChrysalisAccelerator { get; set; }
		public int BioModifier { get; set; }
		public int PsionicModulator { get; set; }
		public int ShipManufacturingHall { get; set; }
		public int SupraRefractor { get; set; }

		public int GetLevel(LFBuildables building) {
			int output = 0;
			foreach (PropertyInfo prop in GetType().GetProperties()) {
				if (prop.Name == building.ToString()) {
					output = (int) prop.GetValue(this);
				}
			}
			return output;
		}

		public LFBuildings SetLevel(LFBuildables buildable, int level) {
			foreach (PropertyInfo prop in this.GetType().GetProperties()) {
				if (prop.Name == buildable.ToString()) {
					prop.SetValue(this, level);
				}
			}

			return this;
		}
	}

}
