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
using TBot.Ogame.Infrastructure;

namespace Tbot.Workers.Brain {
	internal class BuyOfferOfTheDayWorker : WorkerBase {
		private readonly IOgameService _ogameService;
		private readonly ITBotOgamedBridge _tbotOgameBridge;
		public BuyOfferOfTheDayWorker(ITBotMain parentInstance,
			IOgameService ogameService,
			ITBotOgamedBridge tbotOgameBridge) :
			base(parentInstance) {
			_ogameService = ogameService;
			_tbotOgameBridge = tbotOgameBridge;
		}
		protected override async Task Execute() {
			_tbotInstance.log(LogLevel.Information, GetLogSender(), "Buying offer of the day...");
			OfferOfTheDayStatus sts = await _ogameService.BuyOfferOfTheDay();

			if (sts == OfferOfTheDayStatus.OfferOfTheDayBougth) {
				_tbotInstance.log(LogLevel.Information, GetLogSender(), "Offer of the day succesfully bought.");
			} else if (sts == OfferOfTheDayStatus.OfferOfTheDayAlreadyBought){
				_tbotInstance.log(LogLevel.Information, GetLogSender(), "Offer of the day already bought.");
			} else {
				_tbotInstance.log(LogLevel.Information, GetLogSender(), "Error buying Offer of the day. Already bought?");
			}
			
			var time = await _tbotOgameBridge.GetDateTime();
			var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.BuyOfferOfTheDay.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.BuyOfferOfTheDay.CheckIntervalMax);
			if (interval <= 0)
				interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
			var newTime = time.AddMilliseconds(interval);
			ChangeWorkerPeriod(interval);
			_tbotInstance.log(LogLevel.Information, GetLogSender(), $"Next BuyOfferOfTheDay check at {newTime.ToString()}");
			await _tbotOgameBridge.CheckCelestials();
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
			return LogSender.BuyOfferOfTheDay;
		}
	}
}
