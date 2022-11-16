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
			bool stop = false;
			try {

				if (_tbotInstance.UserData.isSleeping) {
					_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					return;
				}

				if ((bool) _tbotInstance.InstanceSettings.Brain.Active && (bool) _tbotInstance.InstanceSettings.Brain.BuyOfferOfTheDay.Active) {
					_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Buying offer of the day...");
					if (_tbotInstance.UserData.isSleeping) {
						_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
						return;
					}
					try {
						await _ogameService.BuyOfferOfTheDay();
						_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Offer of the day succesfully bought.");
					} catch {
						_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Offer of the day already bought.");
					}

				} else {
					_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Skipping: feature disabled");
					stop = true;
				}
			} catch (Exception e) {
				_tbotInstance.log(LogLevel.Error, LogSender.Brain, $"BuyOfferOfTheDay Exception: {e.Message}");
				_tbotInstance.log(LogLevel.Warning, LogSender.Brain, $"Stacktrace: {e.StackTrace}");
			} finally {
				if (!_tbotInstance.UserData.isSleeping) {
					if (stop) {
						_tbotInstance.log(LogLevel.Information, LogSender.Brain, $"Stopping feature.");
						await EndExecution();
					} else {
						var time = await _tbotOgameBridge.GetDateTime();
						var interval = RandomizeHelper.CalcRandomInterval((int) _tbotInstance.InstanceSettings.Brain.BuyOfferOfTheDay.CheckIntervalMin, (int) _tbotInstance.InstanceSettings.Brain.BuyOfferOfTheDay.CheckIntervalMax);
						if (interval <= 0)
							interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
						var newTime = time.AddMilliseconds(interval);
						ChangeWorkerPeriod(interval);
						_tbotInstance.log(LogLevel.Information, LogSender.Brain, $"Next BuyOfferOfTheDay check at {newTime.ToString()}");
						await _tbotOgameBridge.CheckCelestials();
					}
				}
			}
		}
		public override string GetWorkerName() {
			return "BuyOfferOfTheDay";
		}
		public override Feature GetFeature() {
			return Feature.BrainOfferOfTheDay;
		}

		public override LogSender GetLogSender() {
			return LogSender.Defender;
		}
	}
}
