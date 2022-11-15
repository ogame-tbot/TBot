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
			bool stop = false;
			try {

				_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Buying offer of the day...");
				if (_tbotInstance.UserData.isSleeping) {
					_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Skipping: Sleep Mode Active!");
					return;
				}
				try {
					await _tbotInstance.OgamedInstance.BuyOfferOfTheDay();
					_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Offer of the day succesfully bought.");
					stop = true;
				} catch {
					_tbotInstance.log(LogLevel.Information, LogSender.Brain, "Offer of the day already bought.");
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
			return LogSender.Defender;
		}
	}
}
