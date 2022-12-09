using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class EspionageReportSummary {
		public int ID { get; set; }
		public EspionageReportType Type { get; set; }
		public string From { get; set; }
		public Coordinate Target { get; set; }
		public float LootPercentage { get; set; }
	}
}
