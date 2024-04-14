using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBonusesBase {
		public float Cost { get; set; }
		public float Duration { get; set; }

		public LFBonusesBase() {
			Cost = 0;
			Duration = 0;
		}
    }
}
