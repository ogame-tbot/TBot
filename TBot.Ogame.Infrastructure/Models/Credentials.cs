using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Credentials {
		public string Universe { get; set; }
		public string Username { get; set; }
		public string Password { get; set; }
		public string Language { get; set; }
		public bool IsLobbyPioneers { get; set; }
		public string BasicAuthUsername { get; set; }
		public string BasicAuthPassword { get; set; }
	}
}
