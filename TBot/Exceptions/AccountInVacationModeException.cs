using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Tasks;

namespace Tbot.Exceptions {
	public class AccountInVacationModeException : Exception {
		public AccountInVacationModeException() {
		}

		public AccountInVacationModeException(string message) : base(message) {
		}

		public AccountInVacationModeException(string message, Exception innerException) : base(message, innerException) {
		}

		protected AccountInVacationModeException(SerializationInfo info, StreamingContext context) : base(info, context) {
		}
	}
}
