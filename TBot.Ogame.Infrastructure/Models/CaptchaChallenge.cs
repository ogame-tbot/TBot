using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class CaptchaChallenge {
		public string Id { get; set; }
		public string Icons { get; set; }
		public string Question { get; set; }
	}
}
