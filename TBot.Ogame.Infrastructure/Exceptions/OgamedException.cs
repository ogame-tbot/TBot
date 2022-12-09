using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace TBot.Ogame.Infrastructure.Exceptions {
	[Serializable]
	public class OgamedException : Exception {
		public OgamedException() {
		}

		public OgamedException(string? message) : base(message) {
		}

		public OgamedException(string? message, Exception? innerException) : base(message, innerException) {
		}

		protected OgamedException(SerializationInfo info, StreamingContext context) : base(info, context) {
		}
	}
}
