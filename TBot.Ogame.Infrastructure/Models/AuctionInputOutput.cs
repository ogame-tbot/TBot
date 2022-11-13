using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace TBot.Ogame.Infrastructure.Models {
	public class AuctionInputOutput {
		[JsonProperty("metal")]
		public long Metal { get; set; } = 10;

		[JsonProperty("crystal")]
		public long Crystal { get; set; } = 10;

		[JsonProperty("deuterium")]
		public long Deuterium { get; set; } = 10;

		public long TotalResources {
			get {
				return Metal + Crystal + Deuterium;
			}
		}

		public override string ToString() {
			return $"M:{Metal} C:{Crystal} D:{Deuterium}";
		}
	}

}
