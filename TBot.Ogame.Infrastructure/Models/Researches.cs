using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class Researches {
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

		public int GetLevel(Buildables research) {
			int output = 0;
			foreach (PropertyInfo prop in GetType().GetProperties()) {
				if (prop.Name == research.ToString()) {
					output = (int) prop.GetValue(this);
				}
			}
			return output;
		}
	}

}
