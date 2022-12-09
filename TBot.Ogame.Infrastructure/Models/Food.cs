using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Food {
		public long Available { get; set; }
		public long StorageCapacity { get; set; }
		public long Overproduction { get; set; }
		public long ConsumedIn { get; set; }
		public long TimeTillFoodRunsOut { get; set; }
	}
}
