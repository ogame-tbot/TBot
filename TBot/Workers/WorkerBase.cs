using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Timers;
using Microsoft.Extensions.Logging;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers {

	public delegate Task WorkerFunction(CancellationToken ct);

	public abstract class WorkerBase : ITBotWorker {
		protected readonly ITBotMain _tbotInstance;
		protected readonly IFleetScheduler _fleetScheduler;
		protected readonly ICalculationService _helpersService;

		protected CancellationToken _ct = CancellationToken.None;
		protected Dictionary<string, Timer> timers = new();

		private SemaphoreSlim _sem = new SemaphoreSlim(1, 1);
		private AsyncTimer _timer = null;

		public WorkerBase(ITBotMain parentInstance) : this(parentInstance, parentInstance.FleetScheduler, parentInstance.HelperService) {

		}

		public WorkerBase(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) {
			_tbotInstance = parentInstance;
			_fleetScheduler = fleetScheduler;
			_helpersService = helpersService;
		}

		protected abstract Task Execute(CancellationToken ct);
		public void DoLog(LogLevel level, string format) {
			_tbotInstance.log(level, GetLogSender(), format);
		}

		public async Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {

			DoLog(LogLevel.Information, $"Starting Worker \"{GetWorkerName()}\"..");

			await StopWorker();

			_ct = ct;

			//TimeSpan periodSpan = TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
			_timer = new AsyncTimer(ExecutionWrapper, $"{_tbotInstance.InstanceAlias.Substring(0, 6)}{GetWorkerName()}");
			await _timer.StartAsync(ct, period, dueTime);
		}
		public async Task StopWorker() {
			if (_timer != null) {
				DoLog(LogLevel.Information, $"Closing Worker \"{GetWorkerName()}\"..");
				await _timer.DisposeAsync();
				_timer = null;
			}

			// Stop also all the timers
			await WaitWorker();
			foreach (var tim in timers) {
				DoLog(LogLevel.Information, $"Deleting timer \"{tim.Key}\" for worker \"{GetWorkerName()}\"");
				tim.Value.Dispose();
			}
			timers.Clear();
			ReleaseWorker();
		}
		public void ChangeWorkerPeriod(long periodMs) {
			ChangeWorkerPeriod(TimeSpan.FromMilliseconds(periodMs));
		}
		public void ChangeWorkerPeriod(TimeSpan period) {
			_timer.ChangePeriod(period);
		}
		void ChangeWorkerDueTime(TimeSpan dueTime) {
			_timer.ChangeDueTime(dueTime);
		}
		void ChangeWorkerDueTime(long dueTimeMs) {
			_timer.ChangeDueTime(TimeSpan.FromMilliseconds(dueTimeMs));
		}
		public async void RestartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {
			DoLog(LogLevel.Information, $"Restarting Worker \"{GetWorkerName()}\"...");
			await StopWorker();
			await StartWorker(ct, period, dueTime);
		}
		public bool IsWorkerRunning() {
			return (_timer != null) && (_timer.IsRunning);
		}
		public void SetSemaphore(SemaphoreSlim sem) {
			_sem.Dispose();
			_sem = sem;
		}
		public async Task WaitWorker() {
			try {
				await _sem.WaitAsync(_ct);
			} catch (OperationCanceledException) {
			}
		}
		public void ReleaseWorker() {
			_sem.Release();
		}

		public abstract string GetWorkerName();
		public abstract Feature GetFeature();
		public abstract LogSender GetLogSender();





		private async Task ExecutionWrapper(CancellationToken ct) {

			if (_tbotInstance.UserData.isSleeping == true) {
				DoLog(LogLevel.Debug, "Sleeping...");
				return;
			}

			try {
				await _sem.WaitAsync(ct);
				await Execute(ct);
				
			} catch(OperationCanceledException) {

			} catch(Exception ex) {
				throw ex;
			} finally {
				_sem.Release();
			}
		}
	}
}
