using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TBot.Common.Logging;

namespace Tbot.Includes {
	public delegate Task AsyncTimerCallback(CancellationToken ct);
	internal class AsyncTimer : IAsyncDisposable {
		private readonly AsyncTimerCallback _callback;
		private readonly string _name;
		private readonly object _changeLock = new();

		private TimeSpan _dueTime;
		private TimeSpan _period;

		public TimeSpan Period {
			get {
				lock (_changeLock) {
					return _period;
				}
			}
			private set {
				lock (_changeLock) {
					_period = value;
				}
			}
		}

		public TimeSpan DueTime {
			get {
				lock (_changeLock) {
					return _dueTime;
				}
			}
			private set {
				lock (_changeLock) {
					_dueTime = value;
				}
			}
		}

		public bool _IsRunning { get; private set; }
		public bool IsRunning {
			get {
				return _IsRunning;
			}
			private set {
				_IsRunning = value;
			}
		}

		private Task _scheduledAction = null;
		private CancellationTokenSource _cts = null;

		public AsyncTimer(AsyncTimerCallback callback, string name) {
			_callback = callback;
			_name = name;
		}

		public async Task StartAsync(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {

			await StopAsync();

			if (dueTime < TimeSpan.Zero)
				throw new ArgumentOutOfRangeException(nameof(dueTime), "due time must be equal or greater than zero");
			DueTime = dueTime;

			if ((period < TimeSpan.Zero) && (period != Timeout.InfiniteTimeSpan))
				throw new ArgumentOutOfRangeException(nameof(period), "period must be equal or greater than zero");
			Period = period;

			_cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

			_scheduledAction = Task.Run(async () => {
				// Change thread name so TaskManager will show it
				Thread.CurrentThread.Name = $"AT_{_name}";
				IsRunning = true;
				CancellationToken ct = _cts.Token;

				try {
					while (true) {
						if (DueTime != TimeSpan.Zero) {
							await Task.Delay(DueTime, ct);
						}

						// USER CALLBACK START HERE
						await _callback(ct);
						// USER CALLBACK END HERE

						if (Period == Timeout.InfiniteTimeSpan) {
							// Exit
							break;
						}
						await Task.Delay(Period, ct);

						ct.ThrowIfCancellationRequested();
					}
				} catch (OperationCanceledException) {
					// OK!
				} finally {
					IsRunning = false;
				}

			}, _cts.Token);
		}

		public async Task StopAsync() {
			if ((_cts != null) && (_scheduledAction != null)) {
				_cts.Cancel();
				_cts = null;
			}

			if (_scheduledAction != null) {
				await _scheduledAction;
				_scheduledAction = null;
			}
		}

		public async Task RestartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {
			await StopAsync();
			await StartAsync(ct, period, dueTime);
		}

		public void ChangeTimings(TimeSpan period, TimeSpan dueTime) {
			lock (_changeLock) {
				_dueTime = dueTime;
				_period = period;
			}
		}

		public void ChangePeriod(TimeSpan period) {
			lock (_changeLock) {
				_period = period;
			}
		}

		public void ChangeDueTime(TimeSpan dueTime) {
			lock (_changeLock) {
				_dueTime = dueTime;
			}
		}

		public async ValueTask DisposeAsync() {
			await StopAsync();
		}
	}
}
