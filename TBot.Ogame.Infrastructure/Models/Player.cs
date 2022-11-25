using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Player {
		public int ID { get; set; }
		public string Name { get; set; }
		public int Rank { get; set; }
		public bool IsBandit { get; set; }
		public bool IsStarlord { get; set; }
	}
}
