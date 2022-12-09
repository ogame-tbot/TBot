using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Models {
	public class OgamedResponse {
		public string Status { get; set; }
		public int Code { get; set; }
		public string Message { get; set; }
		public dynamic Result { get; set; }
	}
}
