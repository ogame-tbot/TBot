using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBonusesExpeditions {
		public float Ships { get; set; }
		public float Resources { get; set; }
        public float Speed { get; set; }
        public float DarkMatter { get; set; }
        public float FleetLoss { get; set; }
        public float Slots { get; set; }
        public float LessEnemies {get; set;}

		public LFBonusesExpeditions() {
			Ships = 0;
			Resources = 0;
			Speed = 0;
			DarkMatter = 0;
			FleetLoss = 0;
			Slots = 0;
			LessEnemies = 0;
		}
	}
}
