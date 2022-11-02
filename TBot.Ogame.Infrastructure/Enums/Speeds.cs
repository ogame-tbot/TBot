using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Enums {
	public static class Speeds {
		public const decimal FivePercent = 0.5M;
		public const decimal TenPercent = 1;
		public const decimal FifteenPercent = 1.5M;
		public const decimal TwentyPercent = 2;
		public const decimal TwentyfivePercent = 2.5M;
		public const decimal ThirtyPercent = 3;
		public const decimal ThirtyfivePercent = 3.5M;
		public const decimal FourtyPercent = 4;
		public const decimal FourtyfivePercent = 4.5M;
		public const decimal FiftyPercent = 5;
		public const decimal FiftyfivePercent = 5.5M;
		public const decimal SixtyPercent = 6;
		public const decimal SixtyfivePercent = 6.5M;
		public const decimal SeventyPercent = 7;
		public const decimal SeventyfivePercent = 7.5M;
		public const decimal EightyPercent = 8;
		public const decimal EightyfivePercent = 8.5M;
		public const decimal NinetyPercent = 9;
		public const decimal NinetyfivePercent = 9.5M;
		public const decimal HundredPercent = 10;

		public static List<decimal> GetGeneralSpeedsList() {
			/* TODO: fix this
			return new()
			{
				10,
				9.5M,
				9,
				8.5M,
				8,
				7.5M,
				7,
				6.5M,
				6,
				5.5M,
				5,
				4.5M,
				4,
				3.5M,
				3,
				2.5M,
				2,
				1.5M,
				1,
				0.5M
			};
			*/
			return new()
			{
				10,
				9,
				8,
				7,
				6,
				5,
				4,
				3,
				2,
				1
			};
		}

		public static List<decimal> GetNonGeneralSpeedsList() {
			return new()
			{
				10,
				9,
				8,
				7,
				6,
				5,
				4,
				3,
				2,
				1
			};
		}
	}

}
