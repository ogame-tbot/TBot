using Tbot.Model;
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


		public async void SendMessage(string message, ParseMode parseMode = ParseMode.Html) {
			Helpers.WriteLog(LogType.Info, LogSender.Telegram, "Sending Telegram message...");
			try {
				await Client.SendTextMessageAsync(Channel, message, parseMode);
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Tbot, $"Could not send Telegram message: an exception has occurred: {e.Message}");
			}
		}

		public async void SendMessage(ITelegramBotClient client, Chat chat, string message, ParseMode parseMode = ParseMode.Html) {
			Helpers.WriteLog(LogType.Info, LogSender.Telegram, "Sending Telegram message...");
			try {
				await client.SendTextMessageAsync(chat, message, parseMode);
			} catch (Exception e) {
				Helpers.WriteLog(LogType.Error, LogSender.Tbot, $"Could not send Telegram message: an exception has occurred: {e.Message}");
			}
		}

		public async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken) {

			List<string> commands = new List<string>()
			{
				"/ghostsleep",
				"/ghostsleepall",
				"/ghost",
				"/ghostmoons",
				"/switch",
				"/sleep",
				"/wakeup",
				"/collect",
				"/collectdeut",
				"/minexpecargo",
				"/stopautoping",
				"/startautoping",
				"/stopexpe",
				"/startexpe",
				"/stopautoresearch",
				"/startautoresearch",
				"/stopautomine",
				"/startautomine",
				"/stoplifeformautomine",
				"/startlifeformautomine",
				"/stoplifeformautoresearch",
				"/startlifeformautoresearch",
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
				"/getfleets",
				"/getcurrentauction",
				"/bidauction",
				"/subscribeauction",
				"/help"
			};

			if (update.Type == Telegram.Bot.Types.Enums.UpdateType.Message) {
				var message = update.Message;
				var arg = "";
				var test = "";
				decimal speed;
				long duration;
				Celestial celestial;
				int celestialID = 0;
				List<Celestial> myCelestials;
				Resources resources;
				Coordinate coord = new();
				String[] args;

				if (commands.Any(x => message.Text.ToLower().Contains(x))) {
					//Handle /commands@botname in string if exist
					if (message.Text.Contains("@") && message.Text.Split(" ").Length == 1)
						message.Text = message.Text.ToLower().Split(' ')[0].Split('@')[0];

					try {
						Tbot.Program.WaitFeature();

						switch (message.Text.ToLower().Split(' ')[0]) {

							case ("/getfleets"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}
								Tbot.Program.TelegramGetFleets();

								return;

							case ("/getcurrentauction"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.TelegramGetCurrentAuction();

								return;

							case ("/subscribeauction"):
								// If there is no auction in progress, then we will trigger a timer when next auction will be in place
								Tbot.Program.TelegramSubscribeToNextAuction();

								return;

							case ("/bidauction"):
								args = message.Text.Split(' ');
								if(args.Length == 1) {
									// Bid minimum amount
									Tbot.Program.TelegramBidAuctionMinimum();
								}
								else if (args.Length < 3) {
									SendMessage(botClient, message.Chat,
										"To bid auction must format: <code>/bidauction 33651579 M:1000 C:1000 D:1000 </code> \n" +
										"Or <code>/bidauction</code> to bid minimum amount to take auction", ParseMode.Html);
									return;
								} else {
									// First string has to be a valid celestialID
									try {
										myCelestials = Tbot.Program.celestials.ToList();
										celestial = myCelestials.Single(celestial => celestial.ID == Int32.Parse(args[1]));
										// If above has not thrown InvalidOperationException, then remaining can be any resource
										resources = Resources.FromString(String.Join(' ', args.Skip(2)));
										if (resources.TotalResources > 0)
											Tbot.Program.TelegramBidAuction(celestial, resources);
										else
											SendMessage(botClient, message.Chat, "Cannot bid to auction with 0 resources set!");
									} catch (Exception e) {
										SendMessage(botClient, message.Chat, $"Error parsing bid auction command \"{e.Message}\"");
									}
								}
	
								return;


							case ("/ghost"):
								if (message.Text.Split(' ').Length != 2) {
									SendMessage(botClient, message.Chat, "Duration (in hours) argument required! Format: <code>/ghost 4h3m or 3m50s or 1h</code>", ParseMode.Html);
									SendMessage(botClient, message.Chat, "Duration (in hours) argument required! Format: <code>/ghost 4h3m or 3m50s or 1h</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								duration = Helpers.ParseDurationFromString(arg);

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, false, Missions.None, true);

								return;


							case ("/ghostto"):
								if (message.Text.Split(' ').Length != 3) {
									SendMessage(botClient, message.Chat, "Duration (in hours) and mission arguments required! Format: <code>/ghostto 4h3m or 3m50s or 1h Harvest</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								test = message.Text.Split(' ')[2];
								test = char.ToUpper(test[0]) + test.Substring(1);
								Missions mission;

								if (!Missions.TryParse(test, out mission)) {
									SendMessage(botClient, message.Chat, $"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}
								duration = Helpers.ParseDurationFromString(arg);

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, false, mission, true);

								return;

							case ("/ghostmoons"):
								if (message.Text.Split(' ').Length != 3) {
									SendMessage(botClient, message.Chat, "Duration (in hours) argument required! Format: <code>/ghostmoons 4h3m or 3m50s or 1h <mission></code>!");
									return;
								}

								arg = message.Text.Split(' ')[1];
								test = message.Text.Split(' ')[2];
								Missions mission_to_do;

								if (!Missions.TryParse(test, out mission_to_do)) {
									SendMessage(botClient, message.Chat, $"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}
								duration = Helpers.ParseDurationFromString(arg);

								List<Celestial> myMoons = Tbot.Program.celestials.Where(p => p.Coordinate.Type == Celestials.Moon).ToList();
								if (myMoons.Count > 0) {
									int fleetSaved = 0;
									foreach (Celestial moon in myMoons) {
										SendMessage(botClient, message.Chat, $"Enqueueign FleetSave for {moon.ToString()}...");
										Tbot.Program.AutoFleetSave(moon, false, duration, false, false, mission_to_do, true);
										// Let's sleep a bit :)
										fleetSaved++;
										if (fleetSaved != myMoons.Count)
											Thread.Sleep(Helpers.CalcRandomInterval(IntervalType.AFewSeconds));
									}
									SendMessage(botClient, message.Chat, "Moons FleetSave done!");
								} else {
									SendMessage(botClient, message.Chat, "No moons found");
								}

								return;


							case ("/ghostsleep"):
								if (message.Text.Split(' ').Length != 3) {
									SendMessage(botClient, message.Chat, "Duration (in hours) argument required! Format: <code>/ghostsleep 4h3m or 3m50s or 1h Harvest</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								duration = Helpers.ParseDurationFromString(arg);
								test = message.Text.Split(' ')[2];
								test = char.ToUpper(test[0]) + test.Substring(1);

								if (!Missions.TryParse(test, out mission)) {
									SendMessage(botClient, message.Chat, $"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.TelegramCurrentCelestialToSave = celestial;
								Tbot.Program.telegramMission = mission;
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, true, Tbot.Program.telegramMission, true);
								return;


							case ("/ghostsleepall"):
								if (message.Text.Split(' ').Length != 3) {
									SendMessage(botClient, message.Chat, "Duration (in hours) argument required! Format: <code>/ghostsleep 4h3m or 3m50s or 1h Harvest</code>", ParseMode.Html);
									return;
								}
								arg = message.Text.Split(' ')[1];
								duration = Helpers.ParseDurationFromString(arg);
								test = message.Text.Split(' ')[2];
								test = char.ToUpper(test[0]) + test.Substring(1);

								if (!Missions.TryParse(test, out mission)) {
									SendMessage(botClient, message.Chat, $"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.AutoFleetSave(celestial, false, duration, false, true, mission, true, true);
								return;


							case ("/switch"):
								if (message.Text.Split(' ').Length != 2) {
									SendMessage(botClient, message.Chat, "Speed argument required! Format: <code>5 for 50%</code>", ParseMode.Html);
									return;
								}
								test = message.Text.Split(' ')[1];
								speed = decimal.Parse(test);

								if (1 <= speed && speed <= 10) {
									Tbot.Program.TelegramSwitch(speed);
									return;
								}
								SendMessage(botClient, message.Chat, $"{test} error: Speed argument must be 1 or 2 or 3 for 10%, 20%, 30% etc.");
								return;


							case ("/deploy"):
								if (message.Text.Split(' ').Length != 4) {
									SendMessage(botClient, message.Chat, "Coordinates, celestial type and speed arguments are needed! Format: <code>/deploy 2:56:8 moon/planet 1/3/5/7/10</code>", ParseMode.Html);

									return;
								}

								try {
									coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
									coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
									coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
								} catch {
									SendMessage(botClient, message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>", ParseMode.Html);
									return;
								}

								Celestials type;
								arg = message.Text.ToLower().Split(' ')[2];
								if ((!arg.Equals("moon")) && (!arg.Equals("planet"))) {
									SendMessage(botClient, message.Chat, $"Celestial type argument is needed! Format: <code>/celestial 2:41:9 moon/planet</code>", ParseMode.Html);
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
								SendMessage(botClient, message.Chat, $"{test} error: Speed argument must be 1 or 2 or 3 for 10%, 20%, 30% etc.");

								return;


							case ("/jumpgate"):
								if (message.Text.Split(' ').Length != 3) {
									SendMessage(botClient, message.Chat, "Destination coordinates and full/auto arguments are needed (auto: keeps required cargo for resources) Format: <code>/jumpgate 2:20:8 auto</code>", ParseMode.Html);
									return;
								}

								try {
									coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
									coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
									coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
								} catch {
									SendMessage(botClient, message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>", ParseMode.Html);
									return;
								}

								string mode = message.Text.ToLower().Split(' ')[2];
								if (!mode.Equals("full") && !mode.Equals("auto")) {
									SendMessage(botClient, message.Chat, "Eerror! Format: <code>/jumpgate 2:20:8 auto/full</code>", ParseMode.Html);
									return;
								}

								celestial = Tbot.Program.TelegramGetCurrentCelestial();
								Tbot.Program.TelegramJumGate(celestial, coord, mode);
								return;


							case ("/cancel"):
								if (message.Text.Split(' ').Length != 2) {
									SendMessage(botClient, message.Chat, "Mission argument required!");
									return;
								}
								arg = message.Text.Split(' ')[1];
								int fleetId = Int32.Parse(arg);

								Tbot.Program.TelegramRetireFleet(fleetId);
								return;


							case ("/cancelghostsleep"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.TelegramCancelGhostSleep();
								return;


							case ("/recall"):
								if (message.Text.Split(' ').Length < 2) {
									SendMessage(botClient, message.Chat, "Enable/disable auto fleetsave recall argument required! Format: <code>/recall true/false</code>", ParseMode.Html);
									return;
								}

								if (message.Text.Split(' ')[1] != "true" && message.Text.Split(' ')[1] != "false") {
									SendMessage(botClient, message.Chat, "Argument must be <code>true</code> or <code>false</code>.");
									return;
								}
								string recall = message.Text.Split(' ')[1];

								if (Tbot.Program.EditSettings(null, Feature.Null, recall))
									SendMessage(botClient, message.Chat, $"Recall value updated to {recall}.");
								return;


							case ("/sleep"):
								if (message.Text.Split(' ').Length != 2) {
									SendMessage(botClient, message.Chat, "Time argument required!");
									return;
								}
								arg = message.Text.Split(' ')[1];
								duration = Helpers.ParseDurationFromString(arg);

								DateTime timeNow = Tbot.Program.GetDateTime();
								DateTime WakeUpTime = timeNow.AddSeconds(duration);

								Tbot.Program.SleepNow(WakeUpTime);
								return;


							case ("/wakeup"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}
								Tbot.Program.WakeUpNow(null);
								return;


							case ("/msg"):
								if (message.Text.Split(' ').Length < 2) {
									SendMessage(botClient, message.Chat, "Need message argument!");
									return;
								}
								arg = message.Text.Split(new[] { ' ' }, 2).Last();
								Tbot.Program.TelegramMesgAttacker(arg);
								return;


							case ("/minexpecargo"):
								if (message.Text.Split(' ').Length < 2) {
									SendMessage(botClient, message.Chat, "Need minimum cargo number argument!");
									return;
								}
								if (!Int32.TryParse(message.Text.Split(' ')[1], out int value)) {
									SendMessage(botClient, message.Chat, "argument must be an integer!");
									return;
								}

								arg = message.Text.Split(' ')[1];
								int cargo = Int32.Parse(arg);
								if (Tbot.Program.EditSettings(null, Feature.Null, string.Empty, cargo))
									SendMessage(botClient, message.Chat, $"MinPrimaryToSend value updated to {cargo}.");
								return;


							case ("/stopexpe"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopExpeditions();
								SendMessage(botClient, message.Chat, "Expeditions stopped!");
								return;


							case ("/startexpe"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeExpeditions();
								SendMessage(botClient, message.Chat, "Expeditions initialized!");
								return;


							case ("/collect"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.TelegramCollect();
								return;


							case ("/collectdeut"):
								if (message.Text.Split(' ').Length != 2) {
									SendMessage(botClient, message.Chat, "Need minimum deut amount argument <code>/collectdeut 500000</code>");
									return;
								}
								if (!Int32.TryParse(message.Text.Split(' ')[1], out int val)) {
									SendMessage(botClient, message.Chat, "argument must be an integer!");
									return;
								}

								long MinAmount = Int32.Parse(message.Text.Split(' ')[1]);
								Tbot.Program.TelegramCollectDeut(MinAmount);
								return;


							case ("/stopautoresearch"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopBrainAutoResearch();
								SendMessage(botClient, message.Chat, "AutoResearch stopped!");
								return;


							case ("/startautoresearch"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeBrainAutoResearch();
								SendMessage(botClient, message.Chat, "AutoResearch started!");
								return;


							case ("/stopautomine"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopBrainAutoMine();
								SendMessage(botClient, message.Chat, "AutoMine stopped!");
								return;


							case ("/startautomine"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeBrainAutoMine();
								SendMessage(botClient, message.Chat, "AutoMine started!");
								return;


							case ("/stoplifeformautomine"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopBrainLifeformAutoMine();
								SendMessage(botClient, message.Chat, "Lifeform AutoMine stopped!");
								return;


							case ("/startlifeformautomine"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeBrainLifeformAutoMine();
								SendMessage(botClient, message.Chat, "Lifeform AutoMine started!");
								return;


							case ("/stoplifeformautoresearch"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopBrainLifeformAutoResearch();
								SendMessage(botClient, message.Chat, "Lifeform AutoResearch stopped!");
								return;


							case ("/startlifeformautoresearch"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeBrainLifeformAutoResearch();
								SendMessage(botClient, message.Chat, "Lifeform AutoResearch started!");
								return;


							case ("/stopdefender"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopDefender();
								SendMessage(botClient, message.Chat, "Defender stopped!");
								return;


							case ("/startdefender"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeDefender();
								SendMessage(botClient, message.Chat, "Defender started!");
								return;


							case ("/stopautofarm"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.StopAutoFarm();
								SendMessage(botClient, message.Chat, "Autofarm stopped!");
								return;


							case ("/startautofarm"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}

								Tbot.Program.InitializeAutoFarm();
								SendMessage(botClient, message.Chat, "Autofarm started!");
								return;


							case ("/getinfo"):
								args = message.Text.Split(' ');
								if (args.Length == 1) {
									celestial = Tbot.Program.TelegramGetCurrentCelestial();
									Tbot.Program.TelegramGetInfo(celestial);
									
									return;
								} else if((args.Length == 2)) {
									myCelestials = Tbot.Program.celestials.ToList();
									// Try celestial ID first
									try {
										celestialID = Int32.Parse(args[1]);
										celestial = myCelestials.Single(c => c.ID == celestialID);
										Tbot.Program.TelegramGetInfo(celestial);

									} catch (Exception e) {
										SendMessage(botClient, message.Chat,
											$"Invalid arguments specified for Format <code>/getinfo 321312132</code>\n" +
											$"Error:{e.Message}");
									}
									return;
								} else if((args.Length == 3)) {
									myCelestials = Tbot.Program.celestials.ToList();
									// Try format Galaxy:System:Position (Moon|Planet)
									try {
										coord = Coordinate.FromString(String.Join(' ', args.Skip(1)));
										celestial = myCelestials.Single(c => c.Coordinate.IsSame(coord));
										Tbot.Program.TelegramGetInfo(celestial);
										
									} catch (Exception e) {
										SendMessage(botClient, message.Chat,
											$"Invalid arguments specified for Format <code>/getinfo 2:48:5 Moon/Planet</code>\n" +
											$"Error:{e.Message}");
									}									
								}
								else {
									SendMessage(botClient, message.Chat, "Invalid number of argument specified for current command");
								}

								
								return;


							case ("/celestial"):
								if (message.Text.Split(' ').Length != 3) {
									SendMessage(botClient, message.Chat, "Coordinate and celestial type arguments required! Format: <code>/celestial 2:56:8 moon/planet</code>", ParseMode.Html);

									return;
								}

								arg = message.Text.ToLower().Split(' ')[2];
								if ((!arg.Equals("moon")) && (!arg.Equals("planet"))) {
									SendMessage(botClient, message.Chat, $"Celestial type argument required! Format: <code>/celestial 2:41:9 moon/planet</code>", ParseMode.Html);
									return;
								}

								try {
									coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
									coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
									coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
								} catch {
									SendMessage(botClient, message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>", ParseMode.Html);
									return;
								}

								arg = char.ToUpper(arg[0]) + arg.Substring(1);
								Tbot.Program.TelegramSetCurrentCelestial(coord, arg);
								return;


							case ("/editsettings"):
								if (message.Text.Split(' ').Length < 3) {
									SendMessage(botClient, message.Chat, "Coordinate and celestial type arguments required! Format: <code>/editsettings 2:56:8 moon/planet (AutoMine/AutoResearch/AutoRepatriate/Expeditions)</code>", ParseMode.Html);
									return;
								}

								arg = message.Text.ToLower().Split(' ')[2];
								if ((!arg.Equals("moon")) && (!arg.Equals("planet"))) {
									SendMessage(botClient, message.Chat, $"Celestial type argument needed! Format: <code>/editsettings 2:100:3 moon/planet (AutoMine/AutoResearch/AutoRepatriate/Expeditions)</code>", ParseMode.Html);
									return;
								}

								try {
									coord.Galaxy = Int32.Parse(message.Text.Split(' ')[1].Split(':')[0]);
									coord.System = Int32.Parse(message.Text.Split(' ')[1].Split(':')[1]);
									coord.Position = Int32.Parse(message.Text.Split(' ')[1].Split(':')[2]);
								} catch {
									SendMessage(botClient, message.Chat, "Error while parsing coordinates! Format: <code>3:125:9 moon/planet (AutoMine/AutoResearch/AutoRepatriate/Expeditions)</code>", ParseMode.Html);
									return;
								}
								var celestialType = char.ToUpper(arg[0]) + arg.Substring(1);

								Feature updateType = Feature.Null;
								if (message.Text.ToLower().Split(' ').Length > 3) {
									arg = message.Text.ToLower().Split(' ')[3];
									if ((!arg.Equals("AutoMine")) && (!arg.Equals("AutoResearch")) && (!arg.Equals("AutoRepatriate")) && (!arg.Equals("Expeditions"))) {
										SendMessage(botClient, message.Chat, $"Update type argument not valid! Format: <code>/editsettings 2:100:3 moon/planet (AutoMine/AutoResearch/AutoRepatriate/Expeditions)</code>", ParseMode.Html);
										return;
									} else {
										switch (arg) {
											case "AutoMine":
												updateType = Feature.BrainAutoMine;
												break;
											case "AutoResearch":
												updateType = Feature.BrainAutoResearch;
												break;
											case "AutoRepatriate":
												updateType = Feature.BrainAutoRepatriate;
												break;
											case "Expeditions":
												updateType = Feature.Expeditions;
												break;
											default:
												break;
										}
									}
								}

								Tbot.Program.TelegramSetCurrentCelestial(coord, celestialType, updateType, true);
								return;


							case ("/spycrash"):
								if (message.Text.Split(' ').Length != 2) {
									SendMessage(botClient, message.Chat, "<code>auto</code> or coordinate argument needed! Format: <code>/spycrash auto/2:56:8</code>", ParseMode.Html);
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
										SendMessage(botClient, message.Chat, "Error while parsing coordinates! Format: <code>3:125:9</code>, or <code>auto</code>", ParseMode.Html);
										return;
									}
								}
								Celestial origin = Tbot.Program.TelegramGetCurrentCelestial();

								Tbot.Program.SpyCrash(origin, target);
								return;


							case ("/attacked"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}
								bool isUnderAttack = Tbot.Program.TelegramIsUnderAttack();

								if (isUnderAttack) {
									SendMessage(botClient, message.Chat, "Yes! You're still under attack!");
								} else {
									SendMessage(botClient, message.Chat, "Nope! Your empire is safe.");
								}
								return;


							case ("/getcelestials"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}
								myCelestials = Tbot.Program.celestials.ToList();
								string celestialStr = "";
								foreach(Celestial c in myCelestials) {
									celestialStr += $"{c.Name.PadRight(16, ' ')} {c.Coordinate.ToString().PadRight(16)} {c.ID}\n";
								}
								SendMessage(botClient, message.Chat, celestialStr);

								return;


							case ("/ping"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}
								SendMessage(botClient, message.Chat, "Pong");
								return;


							case ("/stopautoping"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}
								Tbot.Program.StopTelegramAutoPing();
								SendMessage(botClient, message.Chat, "TelegramAutoPing stopped!");
								return;


							case ("/startautoping"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}
								Tbot.Program.InitializeTelegramAutoPing();
								SendMessage(botClient, message.Chat, "TelegramAutoPing started!");
								return;


							case ("/help"):
								if (message.Text.Split(' ').Length != 1) {
									SendMessage(botClient, message.Chat, "No argument accepted with this command!");
									return;
								}
								SendMessage(botClient, message.Chat,
									"/getfleets - Get OnGoing fleets ids (which are not already coming back)\n" +
									"/getcurrentauction - Get current Auction\n" +
									"/bidauction - Bid to current auction if there is one in progress. Format <code>/bidauction 213131 M:1000 C:1000 D:1000</code>\n" +
									"/subscribeauction - Get a notification when next auction will start\n" +
									"/ghostsleep - Wait fleets return, ghost harvest for current celestial only, and sleep for 5hours <code>/ghostsleep 4h3m or 3m50s Harvest</code>\n" +
									"/ghostsleepall - Wait fleets return, ghost harvest for all celestial and sleep for 5hours <code>/ghostsleepall 4h3m or 3m50s Harvest</code>\n" +
									"/ghost - Ghost for the specified amount of hours on the specified mission. Format: <code>/ghost 4h3m or 3m50s Harvest</code>\n" +
									"/ghostmoons - Ghost moons fleet for the specified amount of hours on the specified mission. Format: <code>/ghostto 4h30m Harvest</code>\n" +
									"/switch - Switch current celestial resources and fleets to its planet or moon at the specified speed. Format: <code>/switch 5</code>\n" +
									"/deploy - Deploy to celestial with full ships and resources. Format: <code>/deploy 3:41:9 moon/planet 10</code>\n" +
									"/jumpgate - jumpgate to moon with full ships [full], or keeps needed cargo amount for resources [auto]. Format: <code>/jumpgate 2:41:9 auto/full</code>\n" +
									"/cancelghostsleep - Cancel planned /ghostsleep(expe) if not already sent\n" +
									"/spycrash - Create a debris field by crashing a probe on target or automatically selected planet. Format: <code>/spycrash 2:41:9/auto</code>\n" +
									"/recall - Enable/disable fleet auto recall. Format: <code>/recall true/false</code>\n" +
									"/collect - Collect planets resources to JSON setting celestial\n" +
									"/collectdeut - Collect planets only deut resources -> to JSON repatriate setting celestial\n" +
									"/msg - Send a message to current attacker. Format: <code>/msg hello dude</code>\n" +
									"/sleep - Stop bot for the specified amount of hours. Format: <code>/sleep 4h3m or 3m50s</code>\n" +
									"/wakeup - Wakeup bot\n" +
									"/cancel - Cancel fleet with specified ID. Format: <code>/cancel 65656</code>\n" +
									"/getcelestials - Return the list of your celestials\n" +
									"/attacked - check if you're (still) under attack\n" +
									"/celestial - Update program current celestial target. Format: <code>/celestial 2:45:8 Moon/Planet</code>\n" +
									"/getinfo - Get current celestial resources and ships. Additional arg format has to be <code>/getinfo 2:45:8 Moon/Planet</code>\n" +
									"/editsettings - Edit JSON file to change Expeditions, Autominer's and Autoresearch Transport Origin, Repatriate and AutoReseach Target celestial. Format: <code>/editsettings 2:425:9 Moon</code>\n" +
									"/minexpecargo - Modify MinPrimaryToSend value inside JSON settings\n" +
									"/stopexpe - Stop sending expedition\n" +
									"/startexpe - Start sending expedition\n" +
									"/startdefender - start defender\n" +
									"/stopdefender - stop defender\n" +
									"/stopautoresearch - stop brain autoresearch\n" +
									"/startautoresearch - start brain autoresearch\n" +
									"/stopautomine - stop brain automine\n" +
									"/startautomine - start brain automine\n" +
									"/stoplifeformautomine - stop brain Lifeform automine\n" +
									"/startlifeformautomine - start brain Lifeform automine\n" +
									"/stoplifeformautoresearch - stop brain Lifeform autoresearch\n" +
									"/startlifeformautoresearch - start brain Lifeform autoresearch\n" +
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
						SendMessage(botClient, message.Chat, $"ApiRequestException Error!\nTry /ping to check if bot still alive!");
						return;

					} catch (FormatException) {
						SendMessage(botClient, message.Chat, $"FormatException Error!\nYou entered an unexpected value (string instead of integer?)\nTry /ping to check if bot still alive!");
						return;

					} catch (NullReferenceException) {
						SendMessage(botClient, message.Chat, $"NullReferenceException Error!\n Something unknown went wrong!\nTry /ping to check if bot still alive!");
						return;

					} catch (Exception) {
						SendMessage(botClient, message.Chat, $"Unknown Exception Error!\nTry /ping to check if bot still alive!");
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
