using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Tbot.Exceptions {
	public class UnableToLoginException : Exception {
		public UnableToLoginException() {
		}

		public UnableToLoginException(string message) : base(message) {
		}

		public UnableToLoginException(string message, Exception innerException) : base(message, innerException) {
		}

		protected UnableToLoginException(SerializationInfo info, StreamingContext context) : base(info, context) {
		}
	}
}
