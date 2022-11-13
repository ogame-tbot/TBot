using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
	public abstract class ITBotMultiFeatureWorker : ITBotWorkerCommon {

		private ConcurrentDictionary<Feature, AsyncTimer> _featuresTimer;
		private SemaphoreSlim _featureSem = new SemaphoreSlim(1, 1);

		public ITBotMultiFeatureWorker(ITBotMain parentInstance) :
			base(parentInstance, parentInstance.FleetScheduler, parentInstance.HelperService) {

		}

		public ITBotMultiFeatureWorker(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) :
			base(parentInstance, fleetScheduler, helpersService) {
		}

		public async Task InitializeFeature(Feature feat, WorkerFunction func, TimeSpan dueTime, TimeSpan period, CancellationToken ct) {
			await StopFeature(feat);

			DoLog(LogLevel.Information, GetLogSenderFromFeature(feat), $"Starting feature {feat.ToString()}");

			var aTimer = new AsyncTimer(async (ct) => {

				// Allow one feature at the time
				await _featureSem.WaitAsync(ct);

				DoLog(LogLevel.Debug, GetLogSenderFromFeature(feat), $"Executing {GetWorkerName()}, {feat.ToString()}");
				await func(ct);
				DoLog(LogLevel.Debug, GetLogSenderFromFeature(feat), $"Done executing {GetWorkerName()}, {feat.ToString()}");

				_featureSem.Release();

			}, $"{GetWorkerName()}_{feat.ToString()}");

			if (_featuresTimer.TryAdd(feat, aTimer) == false ) {
				DoLog(LogLevel.Warning, GetLogSenderFromFeature(feat), $"Failed to start feature {feat.ToString()}");
			} else {
				await aTimer.StartAsync(period, dueTime, ct);
			}
		}

		public async Task StopFeature(Feature feat) {
			DoLog(LogLevel.Information, GetLogSenderFromFeature(feat), $"Stopping feature {feat.ToString()}");
			if (_featuresTimer.Remove(feat, out AsyncTimer timer)) {
				await timer.DisposeAsync();
			}

			// Stop also all the timers
			var remainingTimers = timers.Where(c => c.Key.StartsWith(GetFeatureTimersSuffix(feat)));
			foreach(var tim in remainingTimers) {
				DoLog(LogLevel.Information, GetLogSenderFromFeature(feat), $"Deleting feature {feat.ToString()} timer {tim.Key}...");
				tim.Value.Dispose();
				timers.Remove(tim.Key);
			}
		}

		public void ChangeFeaturePeriod(Feature feat, TimeSpan dueTime, TimeSpan period) {
			if (_featuresTimer.TryGetValue(feat, out AsyncTimer timer)) {
				timer.ChangeTimings(period, dueTime);
			}
		}

		public override async Task StopWorker() {
			var features = _featuresTimer.Keys;
			foreach (var ft in features) {
				await StopFeature(ft);
			}
		}

		public abstract string GetWorkerName();
		public abstract LogSender GetLogSenderFromFeature(Feature feat);
		public abstract string GetFeatureTimersSuffix(Feature feat);
	}
}
