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
	}
}
