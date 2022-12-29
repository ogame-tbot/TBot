using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Tbot.Services;
using Tbot.Workers.Brain;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public interface IWorkerFactory {
		ITBotWorker InitializeWorker(Feature feat, ITBotMain tbotMainInstance, ITBotOgamedBridge tbotOgameBridge);
		ITBotCelestialWorker InitializeCelestialWorker(ITBotWorker parentWorker, Feature feat, ITBotMain tbotMainInstance, ITBotOgamedBridge tbotOgameBridge, Celestial celestial);
		ITBotWorker GetWorker(Feature feat);
		ITBotCelestialWorker GetCelestialWorker(ITBotWorker parentWorker, Celestial celestial);
	}
}
