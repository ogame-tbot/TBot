using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Tbot.Includes {
	internal interface IInstanceManager : IAsyncDisposable {
		string SettingsAbsoluteFilepath { get; set; }
		void OnSettingsChanged();
	}
}
