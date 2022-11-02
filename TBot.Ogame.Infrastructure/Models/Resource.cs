using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Resource {
		public long Available { get; set; }
		public long StorageCapacity { get; set; }
		public long CurrentProduction { get; set; }
	}
}
