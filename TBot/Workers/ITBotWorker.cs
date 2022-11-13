using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Helpers;
using TBot.Model;
using Microsoft.Extensions.DependencyInjection;
using static System.Formats.Asn1.AsnWriter;
using TBot.Ogame.Infrastructure;
using Tbot.Includes;
using System.Security.Cryptography.X509Certificates;

namespace Tbot.Workers {

	public abstract class ITBotWorker : ITBotWorkerCommon {

		private AsyncTimer _timer;

		public ITBotWorker(ITBotMain parentInstance) :
			base(parentInstance, parentInstance.FleetScheduler, parentInstance.HelperService) {

		}

		public ITBotWorker(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) :
			base(parentInstance, fleetScheduler, helpersService) {

		}

		public async Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime) {

			_tbotInstance.log(LogLevel.Information, LogSender.Tbot, $"Starting Worker \"{GetWorkerName()}\"..");

			_ct = ct;

			//TimeSpan periodSpan = TimeSpan.FromMilliseconds(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds));
			_timer = new AsyncTimer(Execute, $"{_tbotInstance.InstanceAlias.Substring(0, 6)}{GetWorkerName()}");
			await _timer.StartAsync(period, dueTime, ct);
		}

		public async Task StopWorker() {
			_tbotInstance.log(LogLevel.Information, LogSender.Tbot, $"Closing Worker \"{GetWorkerName()}\"..");
			if (_timer != null) {
				await _timer.DisposeAsync();
				_timer = null;
			}
		}

		public void ChangeWorkerPeriod(TimeSpan period) {
			_timer.ChangeTimings(period, TimeSpan.Zero);
		}

		public void DoLog(LogLevel level , string format) {
			_tbotInstance.log(level, GetLogSender(), format);
		}

		protected abstract Task Execute(CancellationToken ct);
		public abstract string GetWorkerName();
		public abstract Feature GetFeature();
		public abstract LogSender GetLogSender();
	}
}
