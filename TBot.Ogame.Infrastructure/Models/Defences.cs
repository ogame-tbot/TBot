using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class Defences {
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

		public int GetAmount(Buildables defence) {
			int output = 0;
			foreach (PropertyInfo prop in GetType().GetProperties()) {
				if (prop.Name == defence.ToString()) {
					output = (int) prop.GetValue(this);
				}
			}
			return output;
		}
	}

}
