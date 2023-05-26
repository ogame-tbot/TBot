using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Device {
		public string Name { get; set; }
		public string System { get; set; }
		public string Browser { get; set; }
		public string UserAgent { get; set; }
		public Int32 Memory { get; set; }
		public Int32 Concurrency { get; set; }
		public Int32 Color { get; set; }
		public Int32 Width { get; set; }
		public Int32 Height { get; set; }
		public string Timezone { get; set; }
		public string Lang { get; set; }
	}
}
