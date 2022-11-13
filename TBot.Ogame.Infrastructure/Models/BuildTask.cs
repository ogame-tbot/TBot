using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class BuildTask {
		public Celestial Celestial { get; set; }
		public Buildables Buildable { get; set; }
		public int Level { get; set; }
		public Resources Price { get; set; }
	}
}
