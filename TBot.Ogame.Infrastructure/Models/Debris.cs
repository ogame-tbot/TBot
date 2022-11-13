using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Debris {
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long RecyclersNeeded { get; set; }
		public Resources Resources {
			get {
				return new Resources {
					Metal = Metal,
					Crystal = Crystal,
					Deuterium = 0,
					Darkmatter = 0,
					Energy = 0
				};
			}
		}
	}

}
