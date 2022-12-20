using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers {
	public interface ITBotWorker {

		TimeSpan DueTime { get; }
		TimeSpan Period { get;  }

		void DoLog(LogLevel level, string format);

		Task StartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime);
		Task StartWorker(CancellationToken ct, TimeSpan dueTime);
		Task StopWorker();
		void ChangeWorkerPeriod(TimeSpan period);
		void ChangeWorkerPeriod(long periodMs);
		void ChangeWorkerDueTime(TimeSpan dueTime);
		void ChangeWorkerDueTime(long dueTime);
		void RestartWorker(CancellationToken ct, TimeSpan period, TimeSpan dueTime);
		bool IsWorkerRunning();
		bool IsWorkerEnabledBySettings();
		void SetSemaphore(SemaphoreSlim sem);
		SemaphoreSlim GetSemaphore();
		Task WaitWorker();
		void ReleaseWorker();
		string GetWorkerName();
		Feature GetFeature();
		LogSender GetLogSender();
	}
}
