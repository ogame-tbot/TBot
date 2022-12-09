using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class ProxySettings {
		public bool Enabled { get; set; }
		public string Address { get; set; }
		public string Type { get; set; }
		public string Username { get; set; }
		public string Password { get; set; }
		public bool LoginOnly { get; set; }
		public ProxySettings() {
			Enabled = false;
		}
	}
}
