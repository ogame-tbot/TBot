using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Planet : Celestial {
		public bool Administrator { get; set; }
		public bool Inactive { get; set; }
		public bool Vacation { get; set; }
		public bool StrongPlayer { get; set; }
		public bool Newbie { get; set; }
		public bool HonorableTarget { get; set; }
		public bool Banned { get; set; }
		public Player Player { get; set; }
		public Alliance Alliance { get; set; }
		public Temperature Temperature { get; set; }
		public Moon Moon { get; set; }

		public bool HasMines(Buildings buildings) {
			return Buildings.MetalMine >= buildings.MetalMine
				&& Buildings.CrystalMine >= buildings.CrystalMine
				&& Buildings.DeuteriumSynthesizer >= buildings.DeuteriumSynthesizer;
		}

		public bool HasFacilities(Facilities facilities, bool ignoreSpaceDock = true) {
			return Facilities.RoboticsFactory >= facilities.RoboticsFactory
				&& Facilities.Shipyard >= facilities.Shipyard
				&& Facilities.ResearchLab >= facilities.ResearchLab
				&& Facilities.MissileSilo >= facilities.MissileSilo
				&& Facilities.NaniteFactory >= facilities.NaniteFactory
				&& (Facilities.SpaceDock >= facilities.SpaceDock || ignoreSpaceDock);
		}
	}

}
