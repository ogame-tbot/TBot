using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Techs {
		public Defences defenses { get; set; }
		public Facilities facilities { get; set; }
		public Researches researches { get; set; }
		public Ships ships { get; set; }
		public Buildings supplies { get; set; }
	}
}
