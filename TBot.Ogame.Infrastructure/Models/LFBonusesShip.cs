using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBonusesShip {
		public float Armour { get; set; }
		public float Shield { get; set; }
        public float Weapon { get; set; }
        public float Cargo { get; set; }
        public float Speed { get; set; }
        public float Consumption { get; set; }
        public float Duration { get; set; }

		public LFBonusesShip() {
			Armour = 0;
			Shield = 0;
			Weapon = 0;
			Cargo = 0;
			Speed = 0;
			Consumption = 0;
			Duration = 0;
		}
    }
}
