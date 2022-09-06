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
				await Client.SendTextMessageAsync(Channel, message, ParseMode.Html);
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Tbot, $"Could not send Telegram message: an exception has occurred: {e.Message}");
			}
		}

		public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {

			List<string> commands = new List<string>()
			{
				"/ghostsleep",
				//"/ghostsleepexpe",
				"/ghostsleepall",
				"/ghost",
				"/ghostto",
				"/switch",
				"/sleep",
				"/wakeup",
				"/collect",
				"/stopautoping",
				"/startautoping",
				"stopexpe",
				"startexpe",
				"/stopautomine",
				"/startautomine",
				"/stopdefender",
				"/startdefender",
				"/msg",
				"/ping",
				"/getinfo",
				"/celestial",
				"/cancel",
				"/cancelghostsleep",
				"/editsettings",
				"/spycrash",
				"/attacked",
				"/getcelestials",
				"/recall",
				"/jumpgate",
				"/deploy",
				"/help"
			};

			if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message) {
				var message = update.Message;
				var arg = "";
				var test = "";
				decimal speed;
				long duration;
				Celestial celestial;
				Coordinate coord = new();

				if (commands.Any(x => message.Text.ToLower().Contains(x))) {
					//Handle /commands@botname in string if exist
					if (message.Text.Contains("@") && message.Text.Split(" ").Length == 1) 
						message.Text = message.Text.ToLower().Split(' ')[0].Split('@')[0];

					try {
						Tbot.Program.WaitFeature();

						switch (message.Text.ToLower().Split(' ')[0]) {

							case ("/ghost"):
								if (message.Text.Split(' ').Length != 2) {
									await botClient.SendTextMessageAsync(message.Chat, "Duration (in hours) argument required! Format: <code>/ghost 4</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								duration = Int32.Parse(arg) * 60 * 60; //second

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, false, Missions.None, true);

								return;


							case ("/ghostto"):
								if (message.Text.Split(' ').Length != 3) {
									await botClient.SendTextMessageAsync(message.Chat, "Duration (in hours) and mission arguments required! Format: <code>/ghostto 4 harvest</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								test = message.Text.Split(' ')[2];
								test = char.ToUpper(test[0]) + test.Substring(1);
								Missions mission;

								if (!Missions.TryParse(test, out mission)) {
									await botClient.SendTextMessageAsync(message.Chat, $"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}
								duration = Int32.Parse(arg) * 60 * 60; //second

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, false, mission, true);

								return;


							case ("/ghostsleep"):
								if (message.Text.Split(' ').Length != 3) {
									await botClient.SendTextMessageAsync(message.Chat, "Duration (in hours) argument required! Format: <code>/ghostsleep 5 Harvest</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								duration = Int32.Parse(arg) * 60 * 60; //seconds
								test = message.Text.Split(' ')[2];
								test = char.ToUpper(test[0]) + test.Substring(1);

								if (!Missions.TryParse(test, out mission)) {
									await botClient.SendTextMessageAsync(message.Chat, $"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.TelegramCurrentCelestialToSave = celestial;
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, true, mission, true);
								return;


							case ("/ghostsleepall"):
								if (message.Text.Split(' ').Length != 3) {
									await botClient.SendTextMessageAsync(message.Chat, "Duration (in hours) argument required! Format: <code>/ghostsleep 5 Harvest</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								duration = Int32.Parse(arg) * 60 * 60; //seconds
								test = message.Text.Split(' ')[2];
								test = char.ToUpper(test[0]) + test.Substring(1);

								if (!Missions.TryParse(test, out mission)) {
									await botClient.SendTextMessageAsync(message.Chat, $"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, true, mission, true, true);
								return;

							/*
							case ("/ghostsleepexpe"):
								if (message.Text.Split(' ').Length != 3) {
									await botClient.SendTextMessageAsync(message.Chat, "Duration (in hourd) and celestial type arguments required! Format: <code>/ghostsleepexpe 5 harvest</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								duration = Int32.Parse(arg) * 60 * 60; //seconds

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, true, Missions.None, true, true);
								return;
							*/


							case ("/switch"):
								if (message.Text.Split(' ').Length != 2) {
									await botClient.SendTextMessageAsync(message.Chat, "Speed argument required! Format: <code>5 for 50%</code>", ParseMode.Html);
									return;
								}
								test = message.Text.Split(' ')[1];
								speed = decimal.Parse(test);

								if (1 <= speed && speed <= 10) {
									Tbot.Program.TelegramSwitch(speed);
									return;
								}
								await botClient.SendTextMessageAsync(message.Chat, $"{test} error: Spped argument must be 1 or 2 or 3 for 10%, 20%, 30% etc.");
								return;


							case ("/deploy"):
								if (message.Text.Split(' ').Length != 4) {
									await botClient.SendTextMessageAsync(message.Chat, "Coordinates, celestial type and speed arguments are needed! Format: <code>/deploy 2:56:8 moon/planet 1/3/5/7/10</code>", ParseMode.Html);

									return;
								}

								try {
									coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
									coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
									coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
								} catch {
									await botClient.SendTextMessageAsync(message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>", ParseMode.Html);
									return;
								}

								Celestials type;
								arg = message.Text.ToLower().Split(' ')[2];
								if ((!arg.Equals("moon")) && (!arg.Equals("planet"))) {
									await botClient.SendTextMessageAsync(message.Chat, $"Celestial type argument is needed! Format: <code>/celestial 2:41:9 moon/planet</code>", ParseMode.Html);
									return;
								}
								arg = char.ToUpper(arg[0]) + arg.Substring(1);
								if (Celestials.TryParse(arg, out type)) {
									coord.Type = type;
								}

								test = message.Text.Split(' ')[3];
								speed = decimal.Parse(test);

								if (1 <= speed && speed <= 10) {
									celestial = Tbot.Program.TelegramGetCurrentCelestial();
									Tbot.Program.TelegramDeploy(celestial, coord, speed);
									return;
								}
								await botClient.SendTextMessageAsync(message.Chat, $"{test} error: Speed argument must be 1 or 2 or 3 for 10%, 20%, 30% etc.");
								
								return;


							case ("/jumpgate"):
								if (message.Text.Split(' ').Length != 3) {
									await botClient.SendTextMessageAsync(message.Chat, "Destination coordinates and full/auto arguments are needed (auto: keeps required cargo for resources) Format: <code>/jumpgate 2:20:8 auto</code>", ParseMode.Html);
									return;
								}

								try {
									coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
									coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
									coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
								} catch {
									await botClient.SendTextMessageAsync(message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>", ParseMode.Html);
									return;
								}

								string mode = message.Text.ToLower().Split(' ')[2];
								if (!mode.Equals("full") && !mode.Equals("auto")) {
									await botClient.SendTextMessageAsync(message.Chat, "Eerror! Format: <code>/jumpgate 2:20:8 auto/full</code>", ParseMode.Html);
									return;
								}

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.TelegramJumGate(celestial, coord, mode);
								return;


							case ("/cancel"):
								if (message.Text.Split(' ').Length != 2) {
									await botClient.SendTextMessageAsync(message.Chat, "Mission argument required!");
									return;
								}
								arg = message.Text.Split(' ')[1];
								int fleetId = Int32.Parse(arg);

								Tbot.Program.TelegramRetireFleet(fleetId);
								return;


							case ("/cancelghostsleep"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}								

								Tbot.Program.TelegramCancelGhostSleep();
								return;


							case ("/recall"):
								if (message.Text.Split(' ').Length < 2) {
									await botClient.SendTextMessageAsync(message.Chat, "Enable/disable auto fleetsave recall argument required! Format: <code>/recall true/false</code>", ParseMode.Html);
									return;
								}

								if (message.Text.Split(' ')[1] != "true" && message.Text.Split(' ')[1] != "false") {
									await botClient.SendTextMessageAsync(message.Chat, "Argument must be <code>true</code> or <code>false</code>.");
									return;
								}
								string recall = message.Text.Split(' ')[1];
							 
								if (Tbot.Program.EditSettings(null, recall))
									await botClient.SendTextMessageAsync(message.Chat, $"Recall value updated to {recall}.");
								return;


							case ("/sleep"):
								if (message.Text.Split(' ').Length != 2) {
									await botClient.SendTextMessageAsync(message.Chat, "Mission argument required!");
									return;
								}
								arg = message.Text.Split(' ')[1];
								int sleepingtime = Int32.Parse(arg);

								DateTime timeNow = Tbot.Program.GetDateTime();
								DateTime WakeUpTime = timeNow.AddHours(sleepingtime);
	
								Tbot.Program.SleepNow(WakeUpTime);
								return;

							
							case ("/wakeup"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}
								Tbot.Program.WakeUpNow(null);
								return;

							
							case ("/msg"):
								if (message.Text.Split(' ').Length < 2) {
									await botClient.SendTextMessageAsync(message.Chat, "Need message argument!");
									return;
								}
								arg = message.Text.Split(new[] { ' ' }, 2).Last();
								Tbot.Program.TelegramMesgAttacker(arg);
								return;

							
							case ("/stopexpe"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopExpeditions();
								await botClient.SendTextMessageAsync(message.Chat, "Expeditions stopped!");
								return;

							
							case ("/startexpe"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeExpeditions();
								await botClient.SendTextMessageAsync(message.Chat, "Expeditions initialized!");
								return;


							case ("/collect"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.TelegramCollect();
								return;


							case ("/stopautomine"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopBrainAutoMine();
								await botClient.SendTextMessageAsync(message.Chat, "AutoMine stopped!");
								return;


							case ("/startautomine"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeBrainAutoMine();
								await botClient.SendTextMessageAsync(message.Chat, "AutoMine started!");
								return;

							case ("/stopdefender"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopDefender();
								await botClient.SendTextMessageAsync(message.Chat, "Defender stopped!");
								return;


							case ("/startdefender"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeDefender();
								await botClient.SendTextMessageAsync(message.Chat, "Defender started!");
								return;


							case ("/stopautofarm"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopAutoFarm();
								await botClient.SendTextMessageAsync(message.Chat, "Autofarm stopped!");
								return;


							case ("/startautofarm"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeAutoFarm();
								await botClient.SendTextMessageAsync(message.Chat, "Autofarm started!");
								return;


							case ("/getinfo"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.TelegramGetInfo(celestial);
								return;


							case ("/celestial"):
								if (message.Text.Split(' ').Length != 3) {
									await botClient.SendTextMessageAsync(message.Chat, "Coordinate and celestial type arguments required! Format: <code>/celestial 2:56:8 moon/planet</code>", ParseMode.Html);

									return;
								}

								arg = message.Text.ToLower().Split(' ')[2];
								if ( (!arg.Equals("moon")) && (!arg.Equals("planet")) ) {
									await botClient.SendTextMessageAsync(message.Chat, $"Celestial type argument required! Format: <code>/celestial 2:41:9 moon/planet</code>", ParseMode.Html);
									return;
								}

								try {
									coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
									coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
									coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
								} catch {
									await botClient.SendTextMessageAsync(message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>", ParseMode.Html);
									return;
								}

								arg = char.ToUpper(arg[0]) + arg.Substring(1);
								Tbot.Program.TelegramSetCurrentCelestial(coord, arg);
								return;

							
							case ("/editsettings"):
								if (message.Text.Split(' ').Length != 3) {
									await botClient.SendTextMessageAsync(message.Chat, "Coordinate and celestial type arguments required! Format: <code>/editsettings 2:56:8 moon/planet</code>", ParseMode.Html);
									return;
								}

								arg = message.Text.ToLower().Split(' ')[2];
								if ((!arg.Equals("moon")) && (!arg.Equals("planet"))) {
									await botClient.SendTextMessageAsync(message.Chat, $"Celestial type argument needed! Format: <code>/editsettings 2:100:3 moon/planet</code>", ParseMode.Html);
									return;
								}

								try {
									coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
									coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
									coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
								} catch {
									await botClient.SendTextMessageAsync(message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>", ParseMode.Html);
									return;
								}

								arg = char.ToUpper(arg[0]) + arg.Substring(1);
								Tbot.Program.TelegramSetCurrentCelestial(coord, arg, true);
								return;


							case ("/spycrash"):
								if (message.Text.Split(' ').Length != 2) {
									await botClient.SendTextMessageAsync(message.Chat, "<code>auto</code> or coordinate argument needed! Format: <code>/spycrash auto/2:56:8</code>", ParseMode.Html);
									return;
								}

								Coordinate target;
								if (message.Text.Split(' ')[1].ToLower().Equals("auto")) {
									target = null;
								} else {
									try {
										coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
										coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
										coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
										target = new Coordinate() { Galaxy = coord.Galaxy, System = coord.System, Position = coord.Position, Type = Celestials.Planet };
									} catch {
										await botClient.SendTextMessageAsync(message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>, or <code>auto</code>", ParseMode.Html); 
										return;
									}
								}
								Celestial origin = Tbot.Program.TelegramGetCurrentCelestial();
								
								Tbot.Program.SpyCrash(origin, target);
								return;


							case ("/attacked"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}
								bool isUnderAttack = Tbot.Program.TelegramIsUnderAttack();
									
								if (isUnderAttack) {
									await botClient.SendTextMessageAsync(message.Chat, "Yes! You're still under attack!");
								} else {
									await botClient.SendTextMessageAsync(message.Chat, "Nope! Your empire is safe.");
								}
								return;


							case ("/getcelestials"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}
								List<Celestial> myCelestials = Tbot.Program.celestials.ToList();
								string listCoords = "";
								foreach (Coordinate coordinate in myCelestials.Select(p => p.Coordinate)){
									listCoords += coordinate.ToString() + "\n";
								}
									await botClient.SendTextMessageAsync(message.Chat, $"{listCoords}");

								return;


							case ("/ping"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}
								await botClient.SendTextMessageAsync(message.Chat, "Pong");
								return;


							case ("/stopautoping"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}
								Tbot.Program.StopTelegramAutoPing();
								await botClient.SendTextMessageAsync(message.Chat, "TelegramAutoPing stopped!");
								return;


							case ("/startautoping"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}
								Tbot.Program.InitializeTelegramAutoPing();
								await botClient.SendTextMessageAsync(message.Chat, "TelegramAutoPing started!");
								return;


							case ("/help"):
								if (message.Text.Split(' ').Length != 1) {
									await botClient.SendTextMessageAsync(message.Chat, "No argument accepted with this command!");
									return;
								}
								await botClient.SendTextMessageAsync(message.Chat,
									"/ghostsleep - Wait fleets return, ghost harvest for current celestial only, and sleep for 5hours <code>/ghostsleep 5 Harvest</code>\n" +
									"/ghostsleepall - Wait fleets return, ghost harvest for all celestial and sleep for 5hours <code>/ghostsleep 5 Harvest</code>\n" +
									//"/ghostsleepexpe - Wait fleets return, ghost harvest, sleep for 5hours, but keep sending expedition: <code>/ghostsleepexpe 5 Harvest</code>\n" +
									"/ghost - Ghost fleet for the specified amount of hours\n, let bot chose mission type. Format: <code>/ghost 4</code>\n" +
									"/ghostto - Ghost for the specified amount of hours on the specified mission. Format: <code>/ghostto 4 Harvest</code>\n" +
									"/switch - Switch current celestial resources and fleets to its planet or moon at the specified speed. Format: <code>/switch 5</code>\n" +
									"/deploy - Deploy to celestial with full ships and resources. Format: <code>/deploy 3:41:9 moon/planet 10</code>\n" +
									"/jumpgate - jumpgate to moon with full ships [full], or keeps needed cargo amount for resources [auto]. Format: <code>/jumpgate 2:41:9 auto/full</code>\n" +
									"/cancelghostsleep - Cancel planned /ghostsleep(expe) if not already sent\n" +
									"/spycrash - Create a debris field by crashing a probe on target or automatically selected planet. Format: <code>/spycrash 2:41:9/auto</code>\n" +
									"/recall - Enable/disable fleet auto recall. Format: <code>/recall true/false</code>\n" +
									"/collect - Collect planets resources to JSON setting celestial\n" +
									"/msg - Send a message to current attacker. Format: <code>/msg hello dude</code>\n" +
									"/sleep - Stop bot for the specified amount of hours. Format: <code>/sleep 1</code>\n" +
									"/wakeup - Wakeup bot\n" +
									"/cancel - Cancel fleet with specified ID. Format: <code>/cancel 65656</code>\n" +
									"/getcelestials - Return the list of your celestials\n" +
									"/attacked - check if you're (still) under attack\n" +
									"/celestial - Update program current celestial target. Format: <code>/celestial 2:45:8 Moon/Planet</code>\n" +
									"/getinfo - Get current celestial resources and ships\n" +
									"/editsettings - Edit JSON file to change Expeditions, Autominer's and Autoresearch Transport Origin, Repatriate and AutoReseach Target celestial. Format: <code>/editsettings 2:425:9 Moon</code>\n" +
									"/stopexpe - Stop sending expedition\n" +
									"/startexpe - Start sending expedition\n" +
									"/startdefender - start defender\n" +
									"/stopdefender - stop defender\n" +
									"/stopautomine - stop brain automine\n" +
									"/startautomine - start brain automine\n" +
									"/stopautofarm - stop autofarm\n" +
									"/startautofarm - start autofarm\n" +
									"/stopautoping - stop telegram autoping\n" +
									"/startautoping - start telegram autoping [Receive message every X hours]\n" +
									"/ping - Ping bot\n" +
									"/help - Display this help"
								, ParseMode.Html);
								return;
							default:
								return;
						}

					} catch (ApiRequestException) {
						await botClient.SendTextMessageAsync(message.Chat, $"ApiRequestException Error!\nTry /ping to check if bot still alive!");
						return;

					} catch (FormatException) {
						await botClient.SendTextMessageAsync(message.Chat, $"FormatException Error!\nYou entered an unexpected value (string instead of integer?)\nTry /ping to check if bot still alive!");
						return;

					} catch (NullReferenceException) {
						await botClient.SendTextMessageAsync(message.Chat, $"NullReferenceException Error!\n Something unknown went wrong!\nTry /ping to check if bot still alive!");
						return;

					} catch (Exception) {
						await botClient.SendTextMessageAsync(message.Chat, $"Unknown Exception Error!\nTry /ping to check if bot still alive!");
						return;

					} finally {
						Tbot.Program.releaseFeature();

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
