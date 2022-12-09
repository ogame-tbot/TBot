using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class Buildings {
		public int MetalMine { get; set; }
		public int CrystalMine { get; set; }
		public int DeuteriumSynthesizer { get; set; }
		public int SolarPlant { get; set; }
		public int FusionReactor { get; set; }
		public int SolarSatellite { get; set; }
		public int MetalStorage { get; set; }
		public int CrystalStorage { get; set; }
		public int DeuteriumTank { get; set; }

		public override string ToString() {
			return $"M: {MetalMine.ToString()} C: {CrystalMine.ToString()} D: {DeuteriumSynthesizer.ToString()} S: {SolarPlant.ToString("")} F: {FusionReactor.ToString("")}";
		}

		public int GetLevel(Buildables building) {
			int output = 0;
			foreach (PropertyInfo prop in GetType().GetProperties()) {
				if (prop.Name == building.ToString()) {
					output = (int) prop.GetValue(this);
				}
			}
			return output;
		}

		public Buildings SetLevel(Buildables buildable, int level) {
			foreach (PropertyInfo prop in this.GetType().GetProperties()) {
				if (prop.Name == buildable.ToString()) {
					prop.SetValue(this, level);
				}
			}
			return this;
		}
	}

}
