using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBonusesMoons {
		public float Fields { get; set; }
		public float Size { get; set; }
        public float Chance { get; set; }

		public LFBonusesMoons() {
			Fields = 0;
			Size = 0;
			Chance = 0;
		}
    }
}
