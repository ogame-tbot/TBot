using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class GalaxyInfo {
		public int Galaxy { get; set; }
		public int System { get; set; }
		public List<Planet> Planets { get; set; }
		public ExpeditionDebris ExpeditionDebris { get; set; }
	}
}
