using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;
using System.Linq;
using System.Timers;
using Timer = System.Threading.Timer;
using TBot.Ogame.Infrastructure.Models;
using TBot.Ogame.Infrastructure.Enums;
using Microsoft.Extensions.Logging;
using TBot.Common.Logging;
using TBot.Model;
using Tbot.Includes;
using Tbot.Helpers;
using Serilog.Events;
using Telegram.Bot.Types.ReplyMarkups;
using Tbot.Workers;

namespace Tbot.Services {

	internal class TelegramMessenger : ITelegramMessenger {
		public string Api { get; private set; }
		public string Channel { get; private set; }
		public ITelegramBotClient Client { get; private set; }

		private readonly ILoggerService<TelegramMessenger> _logger;
		private readonly ICalculationService _helpersService;
		private readonly MessengerCommandHandler _commands;

		CancellationTokenSource cts;
		CancellationToken ct;
		Task receivingTask = null;

		private SemaphoreSlim instanceSem = new SemaphoreSlim(1, 1);

		private Timer autoPingTimer;
		private SemaphoreSlim pingSem;
		private long pingHours;
		private DateTime startTime = DateTime.UtcNow;

		public TelegramMessenger(ILoggerService<TelegramMessenger> logger,
			ICalculationService helpersService,
			string api,
			string chatId,
			bool enableLogging) {
			_logger = logger;
			_helpersService = helpersService;
			Api = api;
			Client = new TelegramBotClient(Api);
			Channel = chatId;

			// Initialize Logger
			if (enableLogging == true) {
				_logger.WriteLog(LogLevel.Information, LogSender.Telegram, "Initializing Logging over Telegram");
				logger.AddTelegramLogger(api, chatId);
			}

			_commands = new MessengerCommandHandler(
				() => StartAutoPing(pingHours),
				StopAutoPing,
				_logger,
				Api,
				Channel);
		}

		public void StartAutoPing(long everyHours) {

			DateTime now = DateTime.UtcNow;
			DateTime roundedNextHour = now.AddHours(everyHours).AddMinutes(-now.Minute).AddSeconds(-now.Second);
			long nextping = (long) roundedNextHour.Subtract(now).TotalMilliseconds;

			_logger.WriteLog(LogLevel.Information, LogSender.Telegram, $"Initializing AutoPing (Hours {everyHours})...");
			StopAutoPing();

			pingHours = everyHours;
			pingSem = new SemaphoreSlim(1, 1);
			autoPingTimer = new Timer(AutoPing, null, nextping, Timeout.Infinite);
		}

		public void StopAutoPing() {
			if (autoPingTimer != null) {
				autoPingTimer.Dispose();
				autoPingTimer = null;
			}

			if (pingSem != null) {
				pingSem.Dispose();
				pingSem = null;
			}
		}
		private async void AutoPing(object state) {
			await pingSem.WaitAsync();
			try {
				DateTime now = DateTime.UtcNow;
				TimeSpan upTime = now - startTime;
				DateTime roundedNextHour = now.AddHours(pingHours).AddMinutes(-now.Minute).AddSeconds(-now.Second);
				long nextping = (long) roundedNextHour.Subtract(now).TotalMilliseconds;

				DateTime newTime = now.AddMilliseconds(nextping);
				autoPingTimer.Change(nextping, Timeout.Infinite);

				string pingStr = $"TBot is running since {FormattingHelper.TimeSpanToString(upTime)}\n";
				int instanceIndex = 0;
				foreach (var instance in _commands.GetInstances()) {
					TimeSpan instanceUpTime = now - instance.startTime;
					pingStr += $"#{instanceIndex} " +
						$"<code>[{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}]</code> " +
						$"since {FormattingHelper.TimeSpanToString(instanceUpTime)}\n";
					instanceIndex++;
				}
				await SendMessage(pingStr);

				_logger.WriteLog(LogLevel.Information, LogSender.Telegram, $"AutoPing sent, Next ping at {newTime.ToString()}");
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Error, LogSender.Telegram, $"Error in AutoPing: {ex.Message}");
			} finally {
				pingSem.Release();
			}
			return;
		}

		public async Task AddTbotInstance(TBotMain instance, ITBotOgamedBridge tbotOgamedBridge) {
			_logger.WriteLog(LogLevel.Information, LogSender.Telegram, "Adding instance.....");
			_logger.WriteLog(LogLevel.Information, LogSender.Telegram, $"[{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}]");

			await instanceSem.WaitAsync(ct);

			int instanceIndex = _commands.AddTbotInstance(instance, tbotOgamedBridge);
			await SendMessage($"<code>[{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}]</code> Instance added! (Index:{instanceIndex})");

			instanceSem.Release();
		}

		public async Task RemoveTBotInstance(TBotMain instance) {
			_logger.WriteLog(LogLevel.Information, LogSender.Telegram, "Removing instance.....");
			_logger.WriteLog(LogLevel.Information, LogSender.Telegram, $"[{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}]");

			await instanceSem.WaitAsync(ct);
			_commands.RemoveTBotInstance(instance);
			instanceSem.Release();
		}

		public async Task SendMessage(string message, ParseMode parseMode = ParseMode.Html, CancellationToken cancellationToken = default) {
			try {
				isTyping = false;
				//await Client.SendTextMessageAsync(
				await Client.SendMessage(
					chatId: Channel,
					text: message,
					parseMode: parseMode,
					cancellationToken: cancellationToken);
			} catch (Exception e) {
				_logger.WriteLog(LogLevel.Error, LogSender.Tbot, $"Could not send Telegram message: an exception has occurred: {e.Message}");
			}
		}

		bool isTyping = false;
		public Task SendTyping(CancellationToken cancellationToken) {
			isTyping = true;
			Task.Run(async () => {
				while (isTyping) {
					//await Client.SendChatActionAsync(
					await Client.SendChatAction(
						chatId: Channel,
						action: ChatAction.Typing,//ChatAction.Typing,
						cancellationToken: cancellationToken);
					await Task.Delay(3000);
				}
			});
			return Task.CompletedTask;
		}

		public async Task SendReplyMarkup(string text, IEnumerable<IEnumerable<InlineKeyboardButton>> buttons, CancellationToken ct) {
			isTyping = false;
			var inlineKeyboard = new InlineKeyboardMarkup(buttons);
			//await Client.SendTextMessageAsync(
			await Client.SendMessage(
				chatId: Channel,
				text: text,
				replyMarkup: inlineKeyboard,
				cancellationToken: ct
			);
		}

		public async Task SendMessage(ITelegramBotClient client, Chat chat, string message, ParseMode parseMode = ParseMode.Html) {
			try {
				isTyping = false;
				//await client.SendTextMessageAsync(chat, message, parseMode);
				//await client.SendTextMessageAsync(chat, message, null, parseMode);
				await client.SendMessage(chat, message, parseMode);
			} catch (Exception e) {
				_logger.WriteLog(LogLevel.Error, LogSender.Tbot, $"Could not send Telegram message: an exception has occurred: {e.Message}");
			}
		}

		public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {
			if (update.Type != UpdateType.Message || string.IsNullOrWhiteSpace(update.Message?.Text)) {
				return;
			}
			await SendTyping(ct);
			await _commands.HandleAsync(update.Message.Text, async (msg) => {
				await SendMessage(botClient, update.Message.Chat, msg);
			}, cancellationToken);
		}
		async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) {
			try {
				if (exception is ApiRequestException apiRequestException) {
					//await botClient.SendTextMessageAsync(Channel, apiRequestException.ToString());
					await botClient.SendMessage(Channel, apiRequestException.ToString());
				}
			} catch { }
		}

		public async Task TelegramBot() {
			try {
				await instanceSem.WaitAsync();
				cts = new CancellationTokenSource();
				ct = cts.Token;

				var receiverOptions = new ReceiverOptions {
					AllowedUpdates = Array.Empty<UpdateType>(),
					DropPendingUpdates = false,
					//ThrowPendingUpdates = true
				};

				receivingTask = Client.ReceiveAsync(HandleUpdateAsync, HandleErrorAsync, receiverOptions, ct);
			} finally {
				instanceSem.Release();
			}
		}

		public async Task TelegramBotDisable() {
			try {

				await instanceSem.WaitAsync(ct);
				foreach (var instance in _commands.GetInstances().ToList()) {
					instance.RemoveTelegramMessenger();
				}
			} finally {
				instanceSem.Release();
			}

			// First of all, remove from any instance
			if (Client != null) {
				cts.Cancel();
				try {
					await receivingTask;
				} catch (OperationCanceledException) {

				} finally {
					cts.Dispose();
					receivingTask = null;
				}

			}
			// We should wait for ReceiveAsync to be dismissed
			_logger.RemoveTelegramLogger();
		}
	}
}
