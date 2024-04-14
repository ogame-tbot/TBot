using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBonusesProduction {
		public float Metal { get; set; }
		public float Crystal { get; set; }
        public float Deuterium { get; set; }
        public float Energy { get; set; }
        public float Food { get; set; }
        public float Population { get; set; }

		public LFBonusesProduction() {
			Metal = 0;
			Crystal = 0;
			Deuterium = 0;
			Energy = 0;
			Food = 0;
			Population = 0;
		}
    }
}
