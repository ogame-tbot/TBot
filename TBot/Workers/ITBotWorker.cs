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

namespace Tbot.Workers {
	public abstract class ITBotWorker {
		
		protected Timer _timer;
		protected SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

		protected ILoggerService<ITBotWorker> _logger;
		protected CancellationToken _ct;
		protected UserData _userData;
		protected IOgameService _ogameService;

		public void Init(IServiceScopeFactory serviceScopeFactory, CancellationToken ct, UserData userData, IOgameService ogameService) {
			var scope = serviceScopeFactory.CreateScope();
			_logger = scope.ServiceProvider.GetRequiredService<ILoggerService<ITBotWorker>>();
			_ct = ct;
			_userData = userData;
			_ogameService = ogameService;
			_logger.WriteLog(LogLevel.Information, LogSender.Tbot, $"Initializing Worker \"{GetWorkerName()}\"..");
		}

		public void StartWorker(IServiceScopeFactory serviceScopeFactory, CancellationToken ct) {

			_logger.WriteLog(LogLevel.Information, LogSender.Tbot, $"Starting Worker \"{GetWorkerName()}\"..");
			_timer = new Timer(Execute, null, RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), Timeout.Infinite);
		}
		public async void StopWorker() {
			_logger.WriteLog(LogLevel.Information, LogSender.Tbot, $"Closing Worker \"{GetWorkerName()}\"..");
			if (_timer != null) {
				await _timer.DisposeAsync();
				_timer = null;
			}
		}

		protected abstract void Execute(object state);
		public abstract string GetWorkerName();
		public abstract Feature GetFeature();
	}
}
