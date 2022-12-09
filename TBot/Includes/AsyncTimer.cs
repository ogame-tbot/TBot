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



		private bool _canSetBiggerPeriod = true;

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

				try {
					while (ct.IsCancellationRequested == false) {

						if (DueTime != TimeSpan.Zero) {
							await Task.Delay(DueTime, _cts.Token);
						}

						// USER CALLBACK START HERE
						await _callback(_cts.Token);
						// USER CALLBACK END HERE

						lock (_changeLock) {
							_canSetBiggerPeriod = true;
						}

						if (Period == Timeout.InfiniteTimeSpan) {
							// Exit
							break;
						}
						await Task.Delay(Period, _cts.Token);
					}
				} catch (OperationCanceledException) {
					// OK!
				} finally {
					IsRunning = false;
				}

			}, _cts.Token);
		}

		public async Task StopAsync() {
			if (_scheduledAction != null) {
				if (_cts != null) {
					_cts.Cancel();
				}
				await _scheduledAction;
				_cts = null;
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

		private bool IsLessThan(TimeSpan period, TimeSpan period2) {
			if (period2 == Timeout.InfiniteTimeSpan)
				return true;
			if (period == Timeout.InfiniteTimeSpan)
				return false;
			return period.TotalMilliseconds < period2.TotalMilliseconds;
		}

		public bool ChangePeriod(TimeSpan period) {
			lock (_changeLock) {
				if (IsLessThan(period, _period) || _canSetBiggerPeriod) {
					if (period != Timeout.InfiniteTimeSpan && period.TotalMilliseconds < 0)
						period = TimeSpan.FromMilliseconds(1000);
					_period = period;
					_canSetBiggerPeriod = false;
					return true;
				}
				return false;
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
