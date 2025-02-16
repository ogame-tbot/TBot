using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using TBot.Ogame.Infrastructure.Enums;

namespace TBot.Ogame.Infrastructure.Models {
	public class RankSlotsPriority {
		public RankSlotsPriority(Feature feature = Feature.Null, int rank = int.MaxValue, bool active = false, int maxSlots = 0, int slotsUsed = 0) {
            Rank = rank < 1 ? int.MaxValue : rank;
            Active = active;
            MaxSlots = maxSlots;
            SlotsUsed = slotsUsed;
            Feature = feature;
        }
		public int Rank { get; set; }
		public bool Active { get; set; }
		public int MaxSlots { get; set; }
		public int SlotsUsed { get; set; }
		public Feature Feature { get; set; }
        public bool HasPriorityOn(RankSlotsPriority feature) {
			return this.Rank < feature.Rank;
		}
		public override string ToString() {
			return $"{Rank} -- {Feature.ToString()} is {Active}, using {SlotsUsed}/{MaxSlots}";
		}
	}

}