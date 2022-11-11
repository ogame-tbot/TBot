using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using Tbot.Helpers;
using TBot.Model;
using Tbot.Services;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Workers {
	internal class TBotDefenderWorker : ITBotWorker {

		private async void Execute(object state) {
			try {
				// Wait for the thread semaphore to avoid the concurrency with itself
				await _semaphore.WaitAsync(_ct);
				_logger.WriteLog(LogLevel.Information, LogSender.Defender, "Checking attacks...");

				if (_userData.isSleeping) {
					_logger.WriteLog(LogLevel.Information, LogSender.Defender, "Skipping: Sleep Mode Active!");
					xaSem[Feature.Defender].Release();
					return;
				}

				await FakeActivity();
				_userData.fleets = await UpdateFleets();
				bool isUnderAttack = await _ogameService.IsUnderAttack();
				DateTime time = await GetDateTime();
				if (isUnderAttack) {
					if ((bool) settings.Defender.Alarm.Active)
						await Task.Factory.StartNew(() => ConsoleHelpers.PlayAlarm());
					// UpdateTitle(false, true);
					_logger.WriteLog(LogLevel.Warning, LogSender.Defender, "ENEMY ACTIVITY!!!");
					_userData.attacks = await _ogameService.GetAttacks();
					foreach (AttackerFleet attack in _userData.attacks) {
						HandleAttack(attack);
					}
				} else {
					_logger.WriteLog(LogLevel.Information, LogSender.Defender, "Your empire is safe");
				}
				long interval = RandomizeHelper.CalcRandomInterval((int) settings.Defender.CheckIntervalMin, (int) settings.Defender.CheckIntervalMax);
				if (interval <= 0)
					interval = RandomizeHelper.CalcRandomInterval(IntervalType.SomeSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				_logger.WriteLog(LogLevel.Information, LogSender.Defender, $"Next check at {newTime.ToString()}");
				await CheckCelestials();
			} catch (Exception e) {
				_logger.WriteLog(LogLevel.Warning, LogSender.Defender, $"An error has occurred while checking for attacks: {e.Message}");
				_logger.WriteLog(LogLevel.Warning, LogSender.Defender, $"Stacktrace: {e.StackTrace}");
				DateTime time = await GetDateTime();
				long interval = RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds);
				DateTime newTime = time.AddMilliseconds(interval);
				timers.GetValueOrDefault("DefenderTimer").Change(interval, Timeout.Infinite);
				_logger.WriteLog(LogLevel.Information, LogSender.Defender, $"Next check at {newTime.ToString()}");
				await CheckCelestials();
			} finally {
				if (!_userData.isSleeping)
					xaSem[Feature.Defender].Release();
			}
		}
		public override string GetWorkerName() {
			return "Defender";
		}
		public override Feature GetFeature() {
			return Feature.Defender;
		}


		private async Task FakeActivity() {
			//checking if under attack by making activity on planet/moon configured in settings (otherwise make acti on latest activated planet)
			// And make activity on one more random planet to fake real player
			Celestial celestial;
			Celestial randomCelestial;
			celestial = _userData.celestials
				.Unique()
				.Where(c => c.Coordinate.Galaxy == (int) settings.Brain.AutoMine.Transports.Origin.Galaxy)
				.Where(c => c.Coordinate.System == (int) settings.Brain.AutoMine.Transports.Origin.System)
				.Where(c => c.Coordinate.Position == (int) settings.Brain.AutoMine.Transports.Origin.Position)
				.Where(c => c.Coordinate.Type == Enum.Parse<Celestials>((string) settings.Brain.AutoMine.Transports.Origin.Type))
				.SingleOrDefault() ?? new() { ID = 0 };

			if (celestial.ID != 0) {
				celestial = await ITBotHelper.UpdatePlanet(_ogameService, celestial, UpdateTypes.Defences);
			}
			randomCelestial = _userData.celestials.Shuffle().FirstOrDefault() ?? new() { ID = 0 };
			if (randomCelestial.ID != 0) {
				randomCelestial = await ITBotHelper.UpdatePlanet(_ogameService, randomCelestial, UpdateTypes.Defences);
			}

			return;
		}
	}
}
