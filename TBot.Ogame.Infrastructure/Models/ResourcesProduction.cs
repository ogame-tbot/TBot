using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class ResourcesProduction {
		public Resource Metal { get; set; }
		public Resource Crystal { get; set; }
		public Food Food { get; set; }
		public Population Population { get; set; }
		public Resource Deuterium { get; set; }
		public Energy Energy { get; set; }
		public Darkmatter Darkmatter { get; set; }
	}
}
