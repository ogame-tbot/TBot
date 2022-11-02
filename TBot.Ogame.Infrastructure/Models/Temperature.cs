using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Temperature {
		public int Min { get; set; }
		public int Max { get; set; }
		public float Average {
			get {
				return (float) (Min + Max) / 2;
			}
		}
	}
}
