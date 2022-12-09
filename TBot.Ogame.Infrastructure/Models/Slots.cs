using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Slots {
		public int InUse { get; set; }
		public int Total { get; set; }
		public int ExpInUse { get; set; }
		public int ExpTotal { get; set; }
		public int Free {
			get {
				return Total - InUse;
			}
		}
		public int ExpFree {
			get {
				return ExpTotal - ExpInUse;
			}
		}
	}

}
