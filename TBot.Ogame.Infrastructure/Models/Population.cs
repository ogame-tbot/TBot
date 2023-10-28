using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Population {
		public long Available { get; set; }
		public long T2Lifeforms { get; set; }
		public long T3Lifeforms { get; set; }
		public long LivingSpace { get; set; }
		public long Satisfied { get; set; }
		public long Hungry { get; set; }
		public long GrowthRate { get; set; }
		public long BunkerSpace { get; set; }
		public bool IsFull() {
			return Available >= LivingSpace;
		}
		public bool IsStarving() {
			return Hungry > 0 && WillStarve();
		}
		public bool WillStarve() {
			return LivingSpace > Satisfied;
		}
		public bool IsThereFoodForMore() {
			return Satisfied > Available;
		}
		public bool NeedsMoreT2() {
			return T2Lifeforms < 11000000;
		}
		public bool NeedsMoreT3() {
			return T3Lifeforms < 435000000;
		}
	}
}
