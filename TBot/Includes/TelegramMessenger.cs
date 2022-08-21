using Tbot.Model;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Text;
using Telegram.Bot;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Exceptions;
using System.Linq;


namespace Tbot.Includes {

	class TelegramMessenger {
		public string Api { get; private set; }
		public string Channel { get; private set; }
		static ITelegramBotClient Client { get; set; }
		public TelegramMessenger(string api, string channel) {
			Api = api;
			Client = new TelegramBotClient(Api);
			Channel = channel;
		}
		
		
		public async void SendMessage(string message) {
			Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Sending Telegram message...");
			try {
				await Client.SendTextMessageAsync(Channel, message);
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Tbot, $"Could not send Telegram message: an exception has occurred: {e.Message}");
			}
		}

		public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {
			bool IsSomeStoppedFeatures = false;

			List<string> commands = new List<string>()
			{
				"/ghostsleep",
				"/ghost",
				"/sleep",
				"/wakeup",
				"/collect",
				"stopexpe",
				"startexpe",
				"/send",
				"/ping",
				"/stopautomine",
				"/startautomine",
				"/recall",
				"/help"
			};

			if (update.Type == UpdateType.Message) {
				var message = update.Message;

				if (commands.Any(x => message.Text.ToLower().Contains(x))) {

					try {
						Tbot.Program.WaitFeature();

						if (message.Text.ToLower().Contains("/ghost")) {
							var arg = message.Text.Split(' ')[1];
							long duration = Int32.Parse(arg) * 60 * 60; //second

							var celestialsToFleetsave = Tbot.Program.UpdateCelestials();
							celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
							if (celestialsToFleetsave.Count() == 0)
								celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Planet).ToList();

							foreach (Celestial celestial in celestialsToFleetsave)
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, false);
								
							return;

						}
						else if (message.Text.ToLower().Contains("/ghostsleep")) {
							var arg = message.Text.Split(' ')[1];
							long duration = Int32.Parse(arg) * 60 * 60; //second

							var celestialsToFleetsave = Tbot.Program.UpdateCelestials();
							celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
							if (celestialsToFleetsave.Count() == 0)
								celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Planet).ToList();

							foreach (Celestial celestial in celestialsToFleetsave)
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, true);

							IsSomeStoppedFeatures = true;
							return;
						}
						else if (message.Text.ToLower().Contains("/recall")) {
							var arg = message.Text.Split(' ')[1];
							int fleetId = Int32.Parse(arg);

							Tbot.Program.TelegramRetireFleet(fleetId);
							return;
						}
						else if (message.Text.ToLower().Contains("/sleep")) {
							var arg = message.Text.Split(' ')[1];
							int sleepingtime = Int32.Parse(arg);

							DateTime timeNow = Tbot.Program.GetDateTime();
							DateTime WakeUpTime = timeNow.AddHours(sleepingtime);
	
							Tbot.Program.SleepNow(WakeUpTime);
							return;

						}
						else if (message.Text.ToLower().Contains("/wakeup")) {
							Tbot.Program.WakeUpNow(null);
							return;

						}
						else if (message.Text.ToLower().Contains("/send")) {
							//var msg = message.Text.Split(' ')[1];
							var msg = message.Text.Split(new[] { ' ' }, 2).Last();
							Tbot.Program.TelegramMesgAttacker(msg);
							return;

						}
						else if (message.Text.ToLower().Contains("/stopexpe")) {

							Tbot.Program.StopExpeditions();
							await botClient.SendTextMessageAsync(message.Chat, "Expedition stopped!");
							return;

						}
						else if (message.Text.ToLower().Contains("/startexpe")) {

							Tbot.Program.InitializeExpeditions();
							await botClient.SendTextMessageAsync(message.Chat, "Expedition initialized!");

						}
						else if (message.Text.ToLower().Contains("/collect")) {

							Tbot.Program.InitializeBrainRepatriate();
							Tbot.Program.AutoRepatriate(null);
							Tbot.Program.StopBrainRepatriate();
							return;
						}
						else if (message.Text.ToLower().Contains("/stopautomine")) {

							Tbot.Program.StopBrainAutoMine();
							await botClient.SendTextMessageAsync(message.Chat, "AutoMine stopped!");
							return;
						}
						else if (message.Text.ToLower().Contains("/startautomine")) {

							Tbot.Program.InitializeBrainAutoMine();
							await botClient.SendTextMessageAsync(message.Chat, "AutoMine Started!");
							return;
						}
						else if (message.Text.ToLower().Contains("/ping")) {
							await botClient.SendTextMessageAsync(message.Chat, "pong");
							return;

						}
						else if (message.Text.ToLower().Contains("/help")) {
							await botClient.SendTextMessageAsync(message.Chat,
								"/ghostsleep - '/ghostsleep 5' -> Wait for fleets to return, ghost and sleep for 5hours\n" +
								"/ghost - '/ghost 4' -> Ghost fleet for 4 hours\n" +
								"/sleep - '/sleep 1' -> Stop bot, inactive for 1 hours\n" +
								"/recall - '/recall 65656' -> recall fleet id 65656\n" +
								"/wakeup - Wakeup bot\n" +
								"/send - '/send hello' -> Send 'hello' to current attacker\n" +
								"/stopexpe - Stop sending expedition\n" +
								"/startexpe - Start sending expedition\n" +
								"/collect - Collect planets resources to current moon\n" +
								"/stopautomine - stop brain automine\n" +
								"/startautomine - start brain automine\n" +
								"/ping - Ping bot"
							);
							return;

						}
					} catch (NullReferenceException ex) {
						throw ex;

					} finally {
						if (!IsSomeStoppedFeatures)
							Tbot.Program.releaseFeature();
						else {
							Tbot.Program.releaseNotStoppedFeature();
						}


					}
				}
			}
		}


		async Task HandleErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken) {
			if (exception is ApiRequestException apiRequestException) {
				await botClient.SendTextMessageAsync(Channel, apiRequestException.ToString());
			}
		}

		public async void TelegramBot() {
			
			var cts = new CancellationTokenSource();
			var cancellationToken = cts.Token;

			var receiverOptions = new ReceiverOptions {
				AllowedUpdates = Array.Empty<UpdateType>(),
				ThrowPendingUpdates = true
			};

			await Client.SendTextMessageAsync(Channel,
				"/ghostsleep - '/ghostsleep 5' -> Wait for fleets to return, ghost and sleep for 5hours\n\n" +
				"/ghost - '/ghost 4' -> Ghost fleet for 4 hours\n\n" +
				"/sleep - '/sleep 1' -> Stop bot, inactive for 1 hours\n\n" +
				"/recall - '/recall 65656' -> recall fleet id 65656\n\n" +
				"/wakeup - Wakeup bot\n\n" +
				"/send - '/send hello' -> Send 'hello' to current attacker\n\n" +
				"/stopexpe - Stop sending expedition\n\n" +
				"/startexpe - Start sending expedition\n\n" +
				"/collect - Collect planets resources to current moon\n\n" +
				"/stopautomine - stop brain automine\n\n" +
				"/startautomine - start brain automine\n\n" +
				"/ping - Ping bot"
			);

			await Client.ReceiveAsync(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cts.Token);
		}
	}
}
