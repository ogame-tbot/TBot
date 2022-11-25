using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Server {
		public string Language { get; set; }
		public int Number { get; set; }
		public string Name { get; set; }
		public int PlayerCount { get; set; }
		public int PlayersOnline { get; set; }
		public DateTime Opened { get; set; }
		public DateTime StartDate { get; set; }
		public DateTime? EndDate { get; set; }
		public bool ServerClosed { get; set; }
		public bool Prefered { get; set; }
		public bool SignupClosed { get; set; }
		public Settings Settings { get; set; }
	}

}
