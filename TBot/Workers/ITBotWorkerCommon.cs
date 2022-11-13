using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Tbot.Includes;
using Tbot.Services;
using TBot.Common.Logging;

namespace Tbot.Workers {

	public delegate Task WorkerFunction(CancellationToken ct);

	public abstract class ITBotWorkerCommon {
		protected readonly ITBotMain _tbotInstance;
		protected readonly IFleetScheduler _fleetScheduler;
		protected readonly ICalculationService _helpersService;

		protected CancellationToken _ct = CancellationToken.None;
		protected Dictionary<string, Timer> timers;

		public ITBotWorkerCommon(ITBotMain parentInstance) : this(parentInstance, parentInstance.FleetScheduler, parentInstance.HelperService) {

		}

		public ITBotWorkerCommon(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) {
			_tbotInstance = parentInstance;
			_fleetScheduler = fleetScheduler;
			_helpersService = helpersService;
		}

		public void DoLog(LogLevel level, LogSender sender, string format) {
			_tbotInstance.log(level, sender, format);
		}

		public abstract Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime);
		public abstract Task StopWorker();
	}
}
