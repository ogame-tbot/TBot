using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class Auction {
		public bool HasFinished { get; set; }
		public int Endtime { get; set; }
		public int NumBids { get; set; }
		public int CurrentBid { get; set; }
		public int AlreadyBid { get; set; }
		public int MinimumBid { get; set; }
		public int DeficitBid { get; set; }
		public string HighestBidder { get; set; }
		public int HighestBidderUserID { get; set; }
		public string CurrentItem { get; set; }
		public string CurrentItemLong { get; set; }
		public int Inventory { get; set; }
		public string Token { get; set; }
		public AuctionResourceMultiplier ResourceMultiplier { get; set; }
		public Dictionary<string, AuctionResourcesValue> Resources { get; set; }

		public long TotalResourcesOffered {
			get {
				long sum = 0;
				foreach (var item in Resources) {
					sum += item.Value.output.TotalResources;
				}

				return sum;
			}
		}

		public override string ToString() {
			// TODO CurrentItemLong is too long, but can be parsed with a RegExp. Moreover it has unicode characters.
			// Just too lazy to do anything else :)
			if (HasFinished) {
				// Auctions are no longer than 50 minutes so far.
				string timeStr = GetTimeString();
				return $"Item: {CurrentItem} sold for {CurrentBid} to {HighestBidder}.\n" +
						$"Next Auction in {timeStr} \n" +
						$"Number of Bids: {NumBids}.";
			} else {
				string timeStr = GetTimeString();
				return $"Item: {CurrentItem} ending in \"{timeStr}\". \n" +
						$"CurrentBid: {CurrentBid} by \"{HighestBidder}\" (ID:{HighestBidderUserID}). \n" +
						$"AlreadyBid: {AlreadyBid} MinimumBid: {MinimumBid}. \n" +
						$"To enter we must bid \"{MinimumBid - AlreadyBid}\"\n" +
						$"Resource Multiplier: M:{ResourceMultiplier.Metal} C:{ResourceMultiplier.Crystal} D:{ResourceMultiplier.Deuterium}\n" +
						$"Number of Bids: {NumBids}.";
			}
		}

		public string GetTimeString() {
			return (Endtime > 60) ? $"{Endtime / 60}m{Endtime % 60}s" : $"{Endtime}s";
		}
	}

}
