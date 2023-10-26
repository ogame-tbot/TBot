using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Resources {
		public Resources(long metal = 0, long crystal = 0, long deuterium = 0, long energy = 0, long food = 0, long population = 0, long darkmatter = 0) {
			Metal = metal;
			Crystal = crystal;
			Deuterium = deuterium;
			Energy = energy;
			Food = food;
			Population = population;
			Darkmatter = darkmatter;
		}
		public long Metal { get; set; }
		public long Crystal { get; set; }
		public long Deuterium { get; set; }
		public long Population { get; set; }
		public long Food { get; set; }
		public long Energy { get; set; }
		public long Darkmatter { get; set; }

		public long ConvertedDeuterium {
			get {
				return GetConvertedDeuterium();
			}
		}

		public long GetConvertedDeuterium(double metalRatio = 2.5, double crystalRatio = 1.5) {
			return (long) Math.Round((Metal / metalRatio) + (Crystal / crystalRatio) + Deuterium, 0, MidpointRounding.ToPositiveInfinity);
		}

		public long TotalResources {
			get {
				return Metal + Crystal + Deuterium;
			}
		}

		public long StructuralIntegrity {
			get {
				return Metal + Crystal;
			}
		}

		public override string ToString() {
			return $"M: {Metal.ToString("N0")} C: {Crystal.ToString("N0")} D: {Deuterium.ToString("N0")} E: {Energy.ToString("N0")} DM: {Darkmatter.ToString("N0")}";
		}

		public string TransportableResources {
			get {
				return $"M: {Metal.ToString("N0")} C: {Crystal.ToString("N0")} D: {Deuterium.ToString("N0")}";
			}
		}
		public string LFBuildingCostResources {
			get {
				return $"M: {Metal.ToString("N0")} C: {Crystal.ToString("N0")} D: {Deuterium.ToString("N0")} P: {Population.ToString("N0")}";
			}
		}

		public bool IsEnoughFor(Resources cost, Resources resToLeave = null) {
			var tempMet = Metal;
			var tempCry = Crystal;
			var tempDeut = Deuterium;
			if (resToLeave != null) {
				tempMet -= resToLeave.Metal;
				tempCry -= resToLeave.Crystal;
				tempDeut -= resToLeave.Deuterium;
			}
			return (cost.Metal == 0 || cost.Metal <= tempMet) && (cost.Crystal == 0 || cost.Crystal <= tempCry) && (cost.Deuterium == 0 || cost.Deuterium <= tempDeut);
		}

		public bool IsEmpty() {
			return Metal == 0 && Crystal == 0 && Deuterium == 0;
		}

		public Resources Sum(Resources resourcesToSum) {
			Resources output = new();
			output.Metal = Metal + resourcesToSum.Metal;
			output.Crystal = Crystal + resourcesToSum.Crystal;
			output.Deuterium = Deuterium + resourcesToSum.Deuterium;

			return output;
		}

		public Resources Difference(Resources resourcesToSubtract) {
			Resources output = new();
			output.Metal = Metal - resourcesToSubtract.Metal;
			if (output.Metal < 0)
				output.Metal = 0;
			output.Crystal = Crystal - resourcesToSubtract.Crystal;
			if (output.Crystal < 0)
				output.Crystal = 0;
			output.Deuterium = Deuterium - resourcesToSubtract.Deuterium;
			if (output.Deuterium < 0)
				output.Deuterium = 0;

			return output;
		}

		public Resources Round(int roundTo = 1000) {
			Resources output = new();
			output.Metal = (long) Math.Round((double) ((double) Metal / (double) roundTo), 0, MidpointRounding.ToPositiveInfinity) * (long) 1000;
			output.Crystal = (long) Math.Round((double) ((double) Crystal / (double) roundTo), 0, MidpointRounding.ToPositiveInfinity) * (long) 1000;
			output.Deuterium = (long) Math.Round((double) ((double) Deuterium / (double) roundTo), 0, MidpointRounding.ToPositiveInfinity) * (long) 1000;
			return output;
		}

		public bool IsBuildable(Resources cost) {
			if (Metal >= cost.Metal && Crystal >= cost.Crystal && Deuterium >= cost.Deuterium && Population >= cost.Population)
				return true;
			else
				return false;
		}

		static public Resources FromString(String arg) {
			Resources output = new();

			Regex re = new Regex("([M|m|C|c|D|d]):(\\d*)");
			MatchCollection ms = re.Matches(arg);
			foreach (Match m in ms) {
				if (m.Success == false) {
				} else if (m.Groups[1].Value.ToLower().Contains('m')) {
					output.Metal = Int32.Parse(m.Groups[2].Value);
				} else if (m.Groups[1].Value.ToLower().Contains('c')) {
					output.Crystal = Int32.Parse(m.Groups[2].Value);
				} else if (m.Groups[1].Value.ToLower().Contains('d')) {
					output.Deuterium = Int32.Parse(m.Groups[2].Value);
				}
			}

			return output;
		}
	}

}
