using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tbot.Services;
using Tbot.Workers.Brain;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;

namespace Tbot.Workers {
	public class WorkerFactory : IWorkerFactory {

		// This is going to be replaced with ServiceProvider
		private ConcurrentDictionary<Feature, ITBotWorker> _workers = new();

		private SemaphoreSlim _brain = new SemaphoreSlim(1, 1);

		public ITBotWorker InitializeWorker(Feature feat, ITBotMain tbotMainInstance) {
			if (_workers.TryGetValue(feat, out var worker)) {
				return worker;
			}

			ITBotWorker newWorker = feat switch {
				Feature.Defender => new DefenderWorker(tbotMainInstance),
				Feature.BrainAutobuildCargo => new AutoCargoWorker(tbotMainInstance),
				Feature.BrainAutoRepatriate => new AutoRepatriateWorker(tbotMainInstance),
				Feature.BrainAutoMine => new AutoMineWorker(tbotMainInstance),
				Feature.BrainOfferOfTheDay => new BuyOfferOfTheDayWorker(tbotMainInstance),
				Feature.Expeditions => new ExpeditionsWorker(tbotMainInstance),
				Feature.Harvest => new HarvestWorker(tbotMainInstance),
				Feature.BrainAutoResearch => new AutoResearchWorker(tbotMainInstance, GetAutoMineWorker()),
				Feature.Colonize => new ColonizeWorker(tbotMainInstance),
				Feature.AutoFarm => new AutoFarmWorker(tbotMainInstance),
				Feature.BrainLifeformAutoMine => new LifeformsAutoMineWorker(tbotMainInstance, GetAutoMineWorker()),
				Feature.BrainLifeformAutoResearch => new LifeformsAutoResearchWorker(tbotMainInstance, GetAutoMineWorker()),
				_ => null
			};

			if (newWorker != null) {
				if (IsBrain(feat) == true) {
					newWorker.SetSemaphore(_brain);
				}
				_workers.TryAdd(feat, newWorker);
			}

			return newWorker;
		}

		public ITBotWorker GetWorker(Feature feat) {
			if (_workers.TryGetValue(feat, out var worker)) {
				return worker;
			}
			return null;
		}

		public IAutoMineWorker GetAutoMineWorker() {
			if (_workers.TryGetValue(Feature.BrainAutoMine, out var worker)) {
				return (IAutoMineWorker)worker;
			}
			return null;
		}
		public IAutoRepatriateWorker GetAutoRepatriateWorker() {
			if (_workers.TryGetValue(Feature.BrainAutoRepatriate, out var worker)) {
				return (IAutoRepatriateWorker) worker;
			}
			return null;
		}

		private bool WantsAutoMine(Feature feat) {
			switch (feat) {
				case Feature.BrainAutoResearch:
				case Feature.BrainLifeformAutoMine:
				case Feature.BrainLifeformAutoResearch:
					return true;

				default:
					return false;
			}
		}

		private bool IsBrain(Feature feat) {
			switch (feat) {
				case Feature.BrainAutobuildCargo:
				case Feature.BrainAutoRepatriate:
				case Feature.BrainAutoMine:
				case Feature.BrainOfferOfTheDay:
				case Feature.BrainAutoResearch:
				case Feature.BrainLifeformAutoMine:
				case Feature.BrainLifeformAutoResearch:
					return true;

				default:
					return false;
			}
		}
	}
}
