using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tbot.Includes;
using Tbot.Services;
using Tbot.Workers.Brain;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	public class WorkerFactory : IWorkerFactory {
		private readonly ICalculationService _calculationService;
		private readonly IFleetScheduler _fleetScheduler;
		private readonly IOgameService _ogameService;

		public WorkerFactory(ICalculationService calculationService,
			IFleetScheduler fleetScheduler,
			IOgameService ogameService) {
			_calculationService = calculationService;
			_fleetScheduler = fleetScheduler;
			_ogameService = ogameService;
		}


		// This is going to be replaced with ServiceProvider
		private ConcurrentDictionary<Feature, ITBotWorker> _workers = new();
		private ConcurrentDictionary<Dictionary<Feature, Celestial>, ITBotCelestialWorker> _celestialWorkers = new();

		private SemaphoreSlim _brain = new SemaphoreSlim(1, 1);

		public ITBotWorker InitializeWorker(Feature feat, ITBotMain tbotMainInstance, ITBotOgamedBridge tbotOgameBridge) {
			if (GetWorker(feat) != null) {
				return GetWorker(feat);
			}
			
			ITBotWorker newWorker = feat switch {
				Feature.Defender => new DefenderWorker(tbotMainInstance, _ogameService, _fleetScheduler, tbotOgameBridge),
				Feature.BrainAutobuildCargo => new AutoCargoWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge),
				Feature.BrainAutoRepatriate => new AutoRepatriateWorker(tbotMainInstance, _fleetScheduler, _calculationService, tbotOgameBridge),
				Feature.BrainAutoMine => new AutoMineWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge, this),
				Feature.BrainOfferOfTheDay => new BuyOfferOfTheDayWorker(tbotMainInstance, _ogameService, tbotOgameBridge),
				Feature.Expeditions => new ExpeditionsWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge),
				Feature.Harvest => new HarvestWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge),
				Feature.BrainAutoResearch => new AutoResearchWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge),
				Feature.Colonize => new ColonizeWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge),
				Feature.AutoFarm => new AutoFarmWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge),
				Feature.BrainLifeformAutoMine => new LifeformsAutoMineWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge, this),
				Feature.BrainLifeformAutoResearch => new LifeformsAutoResearchWorker(tbotMainInstance, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge, this),
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

		public ITBotCelestialWorker InitializeCelestialWorker(ITBotWorker parentWorker, Feature feat, ITBotMain tbotMainInstance, ITBotOgamedBridge tbotOgameBridge, Celestial celestial) {
			if (GetCelestialWorker(parentWorker, celestial) != null) {
				return GetCelestialWorker(parentWorker, celestial);
			}

			ITBotCelestialWorker newWorker = feat switch {
				Feature.BrainCelestialAutoMine => new AutoMineCelestialWorker(tbotMainInstance, parentWorker, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge, celestial),
				Feature.BrainCelestialLifeformAutoMine => new LifeformsAutoMineCelestialWorker(tbotMainInstance, parentWorker, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge, celestial),
				Feature.BrainCelestialLifeformAutoResearch => new LifeformsAutoResearchCelestialWorker(tbotMainInstance, parentWorker, _ogameService, _fleetScheduler, _calculationService, tbotOgameBridge, celestial),
				_ => null
			};

			if (newWorker != null) {
				if (IsBrain(feat) == true) {
					newWorker.SetSemaphore(_brain);
				}
				parentWorker.celestialWorkers.TryAdd(celestial, newWorker);
			}

			return newWorker;
		}

		public ITBotWorker GetWorker(Feature feat) {
			if (_workers.TryGetValue(feat, out var worker)) {
				return worker;
			}
			return null;
		}
		
		public ITBotCelestialWorker GetCelestialWorker(ITBotWorker parentWorker, Celestial celestial) {
			if (parentWorker.celestialWorkers.Any(e => e.Key.ID == celestial.ID)) {
				return parentWorker.celestialWorkers.First(e => e.Key.ID == celestial.ID).Value;
			}
			return null;
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
				case Feature.BrainCelestialAutoMine:
				case Feature.BrainCelestialLifeformAutoMine:
				case Feature.BrainCelestialLifeformAutoResearch:
					return true;

				default:
					return false;
			}
		}
	}
}
