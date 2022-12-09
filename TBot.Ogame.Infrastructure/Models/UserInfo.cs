using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class UserInfo {
		public int PlayerID { get; set; }
		public string PlayerName { get; set; }
		public long Points { get; set; }
		public long Rank { get; set; }
		public long Total { get; set; }
		public long HonourPoints { get; set; }
		public CharacterClass Class { get; set; }
	}
}
