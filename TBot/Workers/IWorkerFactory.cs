using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tbot.Services;
using Tbot.Workers.Brain;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers {
	public interface IWorkerFactory {
		ITBotWorker InitializeWorker(Feature feat, ITBotMain tbotMainInstance, IAutoMineWorker worker = null);
		ITBotWorker GetWorker(Feature feat);
	}
}
