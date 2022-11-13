using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Tbot.Exceptions {
	public class MissingConfigurationException : Exception {
		public MissingConfigurationException() {
		}

		public MissingConfigurationException(string message) : base(message) {
		}

		public MissingConfigurationException(string message, Exception innerException) : base(message, innerException) {
		}

		protected MissingConfigurationException(SerializationInfo info, StreamingContext context) : base(info, context) {
		}
	}
}
