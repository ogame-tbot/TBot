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
				"/ghostto",
				"/switch",
				"/sleep",
				"/wakeup",
				"/collect",
				"stopexpe",
				"startexpe",
				"/send",
				"/ping",
				"/stopautomine",
				"/startautomine",
				"/getinfo",
				"/celestial",
				"/recall",
				"/editsettings",
				"/help"
			};

			if (update.Type == UpdateType.Message) {
				var message = update.Message;

				if (commands.Any(x => message.Text.ToLower().Contains(x))) {

					try {
						Tbot.Program.WaitFeature();

						if (message.Text.ToLower().Split(' ')[0] == "/ghost") {
							if (message.Text.Split(' ').Length != 2) {
								await botClient.SendTextMessageAsync(message.Chat, "Need mission value!");
								return;
							}
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
						else if (message.Text.ToLower().Split(' ')[0] == "/ghostto") {
							if (message.Text.Split(' ').Length != 3) {
								await botClient.SendTextMessageAsync(message.Chat, "Need mission value!");
								return;
							}
							var arg = message.Text.Split(' ')[1];
							var test = message.Text.Split(' ')[2];
							test = char.ToUpper(test[0]) + test.Substring(1);
							Missions mission;

							if (!Missions.TryParse(test, out mission)) {
								await botClient.SendTextMessageAsync(message.Chat, $"{test} error: Value must be 'Harvest','Deploy','Transport','Spy','Colonize'");
								return;
							}
							long duration = Int32.Parse(arg) * 60 * 60; //second

							var celestialsToFleetsave = Tbot.Program.UpdateCelestials();
							celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Moon).ToList();
							if (celestialsToFleetsave.Count() == 0)
								celestialsToFleetsave = celestialsToFleetsave.Where(c => c.Coordinate.Type == Celestials.Planet).ToList();

							foreach (Celestial celestial in celestialsToFleetsave)
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, false, mission);

							return;

						}
						else if (message.Text.ToLower().Split(' ')[0] == "/switch") {
							if (message.Text.Split(' ').Length != 2) {
								await botClient.SendTextMessageAsync(message.Chat, "Need speed value (eg: 5 for 50%)");
								return;
							}
							var test = message.Text.Split(' ')[1];
							decimal speed = decimal.Parse(test);

							if (1 <= speed && speed <= 10) {
								Tbot.Program.TelegramSwitch(speed);
								return;
							}
							await botClient.SendTextMessageAsync(message.Chat, $"{test} error: Value must be 1 or 2 or 3 for 10%,20%,30% etc.");
							return;
						}
						else if (message.Text.ToLower().Split(' ')[0] == "/ghostsleep") {
							if (message.Text.Split(' ').Length != 2) {
								await botClient.SendTextMessageAsync(message.Chat, "Need mission value!");
								return;
							}
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
						else if (message.Text.ToLower().Split(' ')[0] == ("/recall")) {
							if (message.Text.Split(' ').Length != 2) {
								await botClient.SendTextMessageAsync(message.Chat, "Need mission value!");
								return;
							}
							var arg = message.Text.Split(' ')[1];
							int fleetId = Int32.Parse(arg);

							Tbot.Program.TelegramRetireFleet(fleetId);
							return;
						}
						else if (message.Text.ToLower().Split(' ')[0] == "/sleep") {
							if (message.Text.Split(' ').Length != 2) {
								await botClient.SendTextMessageAsync(message.Chat, "Need mission value!");
								return;
							}
							var arg = message.Text.Split(' ')[1];
							int sleepingtime = Int32.Parse(arg);

							DateTime timeNow = Tbot.Program.GetDateTime();
							DateTime WakeUpTime = timeNow.AddHours(sleepingtime);
	
							Tbot.Program.SleepNow(WakeUpTime);
							return;

						}
						else if (message.Text.ToLower().Split(' ')[0] == "/wakeup") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No value needed!");
								return;
							}
							Tbot.Program.WakeUpNow(null);
							return;

						}
						else if (message.Text.ToLower().Split(' ')[0] == "/send") {
							if (message.Text.Split(' ').Length < 2) {
								await botClient.SendTextMessageAsync(message.Chat, "Need message value!");
								return;
							}
							var msg = message.Text.Split(new[] { ' ' }, 2).Last();
							Tbot.Program.TelegramMesgAttacker(msg);
							return;

						}
						else if (message.Text.ToLower().Split(' ')[0] == "/stopexpe") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No need value!");
								return;
							}

							Tbot.Program.StopExpeditions();
							await botClient.SendTextMessageAsync(message.Chat, "Expedition stopped!");
							return;

						}
						else if (message.Text.ToLower().Split(' ')[0] == "/startexpe") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No need value!");
								return;
							}

							Tbot.Program.InitializeExpeditions();
							await botClient.SendTextMessageAsync(message.Chat, "Expedition initialized!");

						}
						else if (message.Text.ToLower().Split(' ')[0] == "/collect") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No need value!");
								return;
							}

							Tbot.Program.InitializeBrainRepatriate();
							Tbot.Program.AutoRepatriate(null);
							Tbot.Program.StopBrainRepatriate();
							return;
						}
						else if (message.Text.ToLower().Split(' ')[0] == "/stopautomine") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No need value!");
								return;
							}

							Tbot.Program.StopBrainAutoMine();
							await botClient.SendTextMessageAsync(message.Chat, "AutoMine stopped!");
							return;
						}
						else if (message.Text.ToLower().Split(' ')[0] == "/startautomine") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No need value!");
								return;
							}

							Tbot.Program.InitializeBrainAutoMine();
							await botClient.SendTextMessageAsync(message.Chat, "AutoMine Started!");
							return;
						}
						else if (message.Text.ToLower().Split(' ')[0] == "/getinfo") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No need value!");
								return;
							}

							Tbot.Program.TelegramGetInfo();
							return;
						} else if (message.Text.ToLower().Split(' ')[0] == "/celestial") {
							if (message.Text.Split(' ').Length != 3) {
								await botClient.SendTextMessageAsync(message.Chat, "Need coordinate and type! (/celestial 2:56:8 moon/planet)");
								return;
							}

							Coordinate coord = new();
							string type = message.Text.ToLower().Split(' ')[2];
							if ( (!type.Equals("moon")) && (!type.Equals("planet")) ) {
								await botClient.SendTextMessageAsync(message.Chat, $"Need value moon or planet (got {type})");
								return;
							}

							try {
								coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
								coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
								coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
							} catch {
								await botClient.SendTextMessageAsync(message.Chat, "Error while parsing coordinate! (must be like 3:125:9)");
								return;
							}

							type = char.ToUpper(type[0]) + type.Substring(1);
							Tbot.Program.TelegramCelestial(coord, type);
							return;

						} else if (message.Text.ToLower().Split(' ')[0] == "/editsettings") {
							if (message.Text.Split(' ').Length != 3) {
								await botClient.SendTextMessageAsync(message.Chat, "Need coordinate and type! (/editsettings 2:56:8 moon/planet)");
								return;
							}

							Coordinate coord = new();
							string type = message.Text.ToLower().Split(' ')[2];
							if ((!type.Equals("moon")) && (!type.Equals("planet"))) {
								await botClient.SendTextMessageAsync(message.Chat, $"Need value moon or planet (got {type})");
								return;
							}

							try {
								coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
								coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
								coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
							} catch {
								await botClient.SendTextMessageAsync(message.Chat, "Error while parsing coordinate! (must be like 3:125:9)");
								return;
							}

							type = char.ToUpper(type[0]) + type.Substring(1);
							Tbot.Program.TelegramCelestial(coord, type, true);
							return;

						}
						else if (message.Text.ToLower().Split(' ')[0] == "/ping") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No need value!");
								return;
							}
							await botClient.SendTextMessageAsync(message.Chat, "pong");
							return;

						}
						else if (message.Text.ToLower().Split(' ')[0] == "/help") {
							if (message.Text.Split(' ').Length != 1) {
								await botClient.SendTextMessageAsync(message.Chat, "No need value!");
								return;
							}
							await botClient.SendTextMessageAsync(message.Chat,
								"/ghostsleep - '/ghostsleep 5' -> Wait for fleets to return, ghost and sleep for 5hours\n" +
								"/ghost - '/ghost 4' -> Ghost fleet for 4 hours\n" +
								"/ghostto - '/ghostto 4 Harvest'\n" +
								"/sleep - '/sleep 1' -> Stop bot, inactive for 1 hours\n" +
								"/recall - '/recall 65656' -> recall fleet id 65656\n" +
								"/wakeup - Wakeup bot\n" +
								"/send - '/send hello dude' -> Send 'hello dude' to current attacker\n" +
								"/stopexpe - Stop sending expedition\n" +
								"/startexpe - Start sending expedition\n" +
								"/collect - Collect planets resources to current moon\n" +
								"/stopautomine - stop brain automine\n" +
								"/startautomine - start brain automine\n" +
								"/celestial - '/celestial 2:45:8 Moon' (Moon/Planet) Update program current celestial target\n" +
								"/getinfo - Get current celestial resources and ships\n" +
								"/switch - '/switch 5' -> switch current celestial resources and fleets to its planet or moon at 50% speed\n" +
								"/editsettings - '/editsettings 2:425:9 moon -> Edit JSON file to change: Expedition, Transport, Repatriate and AutoReseach (Origin/Target) celestial\n" + 
								"/ping - Ping bot\n" +
								"/help - Display this help"
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
			
			await Client.ReceiveAsync(HandleUpdateAsync, HandleErrorAsync, receiverOptions, cts.Token);
		}
	}
}
