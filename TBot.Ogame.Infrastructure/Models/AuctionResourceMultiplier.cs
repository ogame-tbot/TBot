using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class AuctionResourceMultiplier {
		public float Metal { get; set; } = 1.0f;
		public float Crystal { get; set; } = 1.5f;
		public float Deuterium { get; set; } = 3.0f;
		public int Honor { get; set; } = 100;
	}
}
