using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;

namespace Tbot.Services {
	public class SettingsFileWatcher {

		private Action _watchFunc;
		private string _absFpToWatch;
		private SemaphoreSlim _changedSem = new SemaphoreSlim(1, 1);
		private PhysicalFileProvider p;
		private IChangeToken changeToken;
		private IDisposable changeCallback;

		public SettingsFileWatcher(Action func, string filePathToWatch) {
			_watchFunc = func;
			_absFpToWatch = filePathToWatch;

			initWatch();
		}

		private void initWatch() {
			p = new PhysicalFileProvider(Path.GetDirectoryName(_absFpToWatch));
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
				p.UseActivePolling = true;
			}
			changeToken = p.Watch(Path.GetFileName(_absFpToWatch));
			changeCallback = changeToken.RegisterChangeCallback(onChanged, default);
		}

		public void deinitWatch() {
			if (changeCallback != null) {
				changeCallback.Dispose();
				changeCallback = null;
			}
		}

		private async void onChanged(object state) {

			await _changedSem.WaitAsync();
			_watchFunc();
			_changedSem.Release();

			initWatch();
		}
	}
}
