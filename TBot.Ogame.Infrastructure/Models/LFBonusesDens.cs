using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class LFBonusesDens {
		public float Metal { get; set; }
		public float Crystal { get; set; }
        public float Deuterium { get; set; }

		public LFBonusesDens() {
			Metal = 0;
			Crystal = 0;
			Deuterium = 0;
		}
	}
}
