using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class ExpeditionDebris {
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long Deuterium { get; set; }
		public long PathfindersNeeded { get; set; }
		public Resources Resources {
			get {
				return new Resources {
					Metal = Metal,
					Crystal = Crystal,
					Deuterium = Deuterium,
					Darkmatter = 0,
					Energy = 0
				};
			}
		}
	}
}
