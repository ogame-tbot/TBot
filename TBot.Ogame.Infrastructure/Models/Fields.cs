using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Fields {
		public int Built { get; set; }
		public int Total { get; set; }
		public int Free {
			get {
				return Total - Built;
			}
		}
	}
}
