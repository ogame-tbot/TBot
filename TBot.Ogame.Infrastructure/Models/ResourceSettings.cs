using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class ResourceSettings {
		public float MetalMine { get; set; }
		public float CrystalMine { get; set; }
		public float DeuteriumSynthesizer { get; set; }
		public float SolarPlant { get; set; }
		public float FusionReactor { get; set; }
		public float SolarSatellite { get; set; }
		public float Crawler { get; set; }
		public float Ratio {
			get {
				var ratios = new float[] { MetalMine / 100, CrystalMine / 100, DeuteriumSynthesizer / 100 };
				return ratios.AsQueryable().Average();
			}
		}
		public float CrawlerRatio {
			get {
				return Crawler / 100;
			}
		}
	}

}
