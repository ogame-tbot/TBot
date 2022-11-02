using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Staff {
		public Staff() {
			Commander = false;
			Admiral = false;
			Engineer = false;
			Geologist = false;
			Technocrat = false;
		}
		public bool Commander { get; set; }
		public bool Admiral { get; set; }
		public bool Engineer { get; set; }
		public bool Geologist { get; set; }
		public bool Technocrat { get; set; }
		public bool IsFull {
			get {
				return Commander && Admiral && Engineer && Geologist && Technocrat;
			}
		}
	}

}
