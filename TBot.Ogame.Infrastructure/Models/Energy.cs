using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Energy {
		public long Available { get; set; }
		public long CurrentProduction { get; set; }
		public long Consumption { get; set; }
	}
}
