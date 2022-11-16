using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Tbot.Services;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using TBot.Ogame.Infrastructure.Enums;
using Tbot.Helpers;
using TBot.Model;
using Tbot.Includes;

namespace Tbot.Workers.Brain {
	internal class BuyOfferOfTheDayWorker : WorkerBase {

		public BuyOfferOfTheDayWorker(ITBotMain parentInstance, IFleetScheduler fleetScheduler, ICalculationService helpersService) :
			base(parentInstance, fleetScheduler, helpersService) {
		}
		public BuyOfferOfTheDayWorker(ITBotMain parentInstance) :
			base(parentInstance) {
		}
		protected override async Task Execute() {
			bool stop = true;

			_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Buying offer of the day...");
			OfferOfTheDayStatus sts = await _tbotInstance.OgamedInstance.BuyOfferOfTheDay();

			if (sts == OfferOfTheDayStatus.OfferOfTheDayBougth) {
				_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Offer of the day succesfully bought.");
			} else if (sts == OfferOfTheDayStatus.OfferOfTheDayAlreadyBought){
				_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Offer of the day already bought.");
			} else {
				_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Offer of the day unknown error.");
				stop = false;
			}
			
			
			if (stop) {
				_tbotInstance.log(LogLevel.Information, LogSender.Brain, $"Stopping BuyOfferOfTheDay.");
				await EndExecution();
			} else {
				var time = await TBotOgamedBridge.GetDateTime(_tbotInstance);
				var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.BuyOfferOfTheDay.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.BuyOfferOfTheDay.CheckIntervalMax);
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				var newTime = time.AddMilliseconds(interval);
				ChangeWorkerPeriod(interval);
				_tbotInstance.log(LogLevel.Information, LogSender.Brain, $"Next BuyOfferOfTheDay check at {newTime.ToString()}");
				await TBotOgamedBridge.CheckCelestials(_tbotInstance);
			}
		}
		public override bool IsWorkerEnabledBySettings() {
			try {
				return (
					(bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.BuyOfferOfTheDay.Active
				);
			} catch (Exception) {
				return false;
			}
		}
		public override string GetWorkerName() {
			return "BuyOfferOfTheDay";
		}
		public override Feature GetFeature() {
			return Feature.BrainOfferOfTheDay;
		}

		public override LogSender GetLogSender() {
			return LogSender.Brain;
		}
	}
}
