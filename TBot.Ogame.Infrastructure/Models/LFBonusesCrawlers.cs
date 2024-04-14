using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBonusesCrawlers {
		public float EnergyReduction { get; set; }
		public float Production { get; set; }
        public float Number { get; set; }

		public LFBonusesCrawlers() {
			EnergyReduction = 0;
			Production = 0;
			Number = 0;
		}
    }
}
