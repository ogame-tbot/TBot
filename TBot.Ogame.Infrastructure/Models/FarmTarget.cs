using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	/// <summary>
	/// Celestial under consideration to be targetted for farming.
	/// </summary>
	public class FarmTarget {
		public FarmTarget(Celestial target, FarmState farmState = FarmState.Idle, EspionageReport report = null) {
			Celestial = target;
			State = farmState;
			Report = report;
		}
		public Celestial Celestial { get; set; }
		public FarmState State { get; set; }
		public EspionageReport Report { get; set; }
		public bool HasCoords(Coordinate coords) {
			return coords.Galaxy == Celestial.Coordinate.Galaxy
				&& coords.System == Celestial.Coordinate.System
				&& coords.Position == Celestial.Coordinate.Position
				&& coords.Type == Celestial.Coordinate.Type;
		}
		private string GetCelestialCode() {
			return Celestial.Coordinate.Type switch {
				Celestials.Planet => "P",
				Celestials.Debris => "DF",
				Celestials.Moon => "M",
				Celestials.DeepSpace => "DS",
				_ => "",
			};
		}
		public override string ToString() {
			return $"[{GetCelestialCode()}:{Celestial.Coordinate.Galaxy}:{Celestial.Coordinate.System}:{Celestial.Coordinate.Position}]";
		}
	}

}
