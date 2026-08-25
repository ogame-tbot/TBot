using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Serilog.Events;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Workers;
using TBot.Common.Logging;
using TBot.Model;
using TBot.Ogame.Infrastructure.Enums;
using TBot.Ogame.Infrastructure.Models;

namespace Tbot.Services {
	internal class MessengerCommandHandler {
		private class InstanceWithBridge {
			public InstanceWithBridge(TBotMain instance, ITBotOgamedBridge tBotOgamedBridge) {
				Instance = instance;
				TBotOGamedBridge = tBotOgamedBridge;
			}
			public TBotMain Instance { get; set; }
			public ITBotOgamedBridge TBotOGamedBridge { get; set; }
		}

		private readonly List<InstanceWithBridge> instances = new();
		private int currInstanceIndex = -1;
		private readonly Action _startAutoPing;
		private readonly Action _stopAutoPing;
		private readonly ILoggerService<TelegramMessenger> _telegramLogger;
		private readonly string _telegramApi;
		private readonly string _telegramChatId;

		public int CurrInstanceIndex => currInstanceIndex;
		public int InstanceCount => instances.Count;

		public MessengerCommandHandler(
			Action startAutoPing,
			Action stopAutoPing,
			ILoggerService<TelegramMessenger> telegramLogger = null,
			string telegramApi = null,
			string telegramChatId = null) {
			_startAutoPing = startAutoPing;
			_stopAutoPing = stopAutoPing;
			_telegramLogger = telegramLogger;
			_telegramApi = telegramApi;
			_telegramChatId = telegramChatId;
		}

		private void StartAutoPingCb() {
			_startAutoPing?.Invoke();
		}

		private void StopAutoPingCb() {
			_stopAutoPing?.Invoke();
		}

		public int AddTbotInstance(TBotMain instance, ITBotOgamedBridge tbotOgamedBridge) {
			var existing = instances.FirstOrDefault(i => i.Instance.InstanceAlias == instance.InstanceAlias);
			if (existing != null) {
				return instances.IndexOf(existing);
			}
			var instanceWithBridge = new InstanceWithBridge(instance, tbotOgamedBridge);
			instances.Add(instanceWithBridge);
			int instanceIndex = instances.IndexOf(instanceWithBridge);
			if (currInstanceIndex < 0) {
				currInstanceIndex = instanceIndex;
			}
			return instanceIndex;
		}

		public void RemoveTBotInstance(TBotMain instance) {
			instances.RemoveAll(i => i.Instance.InstanceAlias == instance.InstanceAlias);
			if (currInstanceIndex >= instances.Count) {
				currInstanceIndex = instances.Count - 1;
			}
		}

		public IEnumerable<TBotMain> GetInstances() {
			return instances.Select(i => i.Instance);
		}

		public async Task HandleAsync(string text, Func<string, Task> reply, CancellationToken ct) {
			if (string.IsNullOrWhiteSpace(text)) {
				return;
			}
			text = NormalizeCommand(text);
			List<string> core_cmds = new List<string>()
			{
				"/setmain",
				"/getmain",
				"/getmainstats",
				"/listinstances",
				"/loglevel",
				"/setloglevel",
				"/ping",
				"/stopautoping",
				"/startautoping",
				"/help",
				"/selftest"
			};
			// Commands targeting specific TBotMain instance
			List<string> commands = new List<string>()
			{
				"/ghostsleep",
				"/ghostsleepall",
				"/ghost",
				"/ghostmoons",
				"/switch",
				"/sleep",
				"/wakeup",
				"/build",
				"/collect",
				"/collectall",
				"/collectdeut",
				"/minexpecargo",
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
				"/getinfo",
				"/celestial",
				"/cancel",
				"/cancelmission",
				"/cancelghostsleep",
				"/editsettings",
				"/spycrash",
				"/attacked",
				"/getcelestials",
				"/recall",
				"/jumpgate",
				"/phalanx",
				"/deploy",
				"/getfleets",
				"/getcurrentauction",
				"/bidauction",
				"/subscribeauction",
				"/stopautofarm",
				"/startautofarm",
				"/stopautodiscovery",
				"/startautodiscovery"
			};

			var arg = "";
				var test = "";
				decimal speed;
				long duration;
				Celestial celestial;
				int celestialID = 0;
				List<Celestial> myCelestials;
				Resources resources;
				Coordinate coord = new();
				Coordinate target = new();
				string[] args;

				if (core_cmds.Any(x => text.ToLower().Contains(x))) {
					args = text.ToLower().Split(' ');
					arg = args.ElementAt(0);

					switch (arg) {
						case "/setmain":
							if (args.Length != 2) {
								await reply("Invalid number of arguments. Expected 1");
								return;
							}

							if (int.TryParse(args.ElementAt(1), out int UserSelectedInstance) == true) {
								if (UserSelectedInstance >= instances.Count()) {
									await reply($"Selected index \"{args.ElementAt(1)}\" exceeds managed {instances.Count()}");
								} else {
									currInstanceIndex = UserSelectedInstance;
									var cInstance = instances.ElementAt(currInstanceIndex).Instance;
									await reply($"Selected index \"{args.ElementAt(1)}\"" +
										$"{cInstance.userData.userInfo.PlayerName}@{cInstance.userData.serverData.Name}");
								}
							} else {
								await reply($"Error parsing instance index from \"{args.ElementAt(1)}\"");
							}
							return;
						case "/getmain":
							if (currInstanceIndex < 0 || currInstanceIndex >= instances.Count()) {
								await reply("Currently managing no instance");
							} else {
								var instance = instances[currInstanceIndex].Instance;
								await reply($"Managing #{currInstanceIndex} {instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}");
							}
							return;
						case "/getmainstats":
							if (currInstanceIndex < 0 || currInstanceIndex >= instances.Count()) {
								await reply("Currently managing no instance");
							} else {
								var instance = instances[currInstanceIndex].Instance;
								foreach (var feat in Features.AllFeatures) {
									await reply($"Instance #{currInstanceIndex} <code>{instance.ToString()}</code> <b>{feat.ToString()}</b> Running: {instance.IsFeatureRunning(feat)}");
								}
							}
							return;
						case "/listinstances":
							await reply($"Listing #{instances.Count}");
							foreach (var instanceWithBridge in instances) {
								var instance = instanceWithBridge.Instance;
								await reply($"{instances.IndexOf(instanceWithBridge)} {instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}");
							}
							return;
						case "/loglevel":
							if (_telegramLogger == null) {
								await reply("Telegram logging is not available on Discord. Check TBot console/WebUI logs.");
								return;
							}
							if (_telegramLogger.IsTelegramLoggerEnabled() == true) {
								await reply($"Telegram Logger Enabled. LogLevel is {_telegramLogger.GetTelegramLoggerLevel()}");
							}
							else {
								await reply("Telegram Logger is disabled.");
							}
							return;
						case "/setloglevel":
							if (_telegramLogger == null) {
								await reply("Telegram logging is not available on Discord. Check TBot console/WebUI logs.");
								return;
							}
							if (args.Length != 2) {
								await reply("Usage is <code>/setloglevel Debug|Information|Warning|Error</code>");
								return;
							}
							
							if (Enum.TryParse<LogEventLevel>(args[1], true, out LogEventLevel newLevel) == true) {
								await reply($"Enabling Telegram logger with level {newLevel.ToString()}");
								_telegramLogger.AddTelegramLogger(_telegramApi, _telegramChatId);
								_telegramLogger.SetTelegramLoggerLogLevel(newLevel);
								await reply($"Telegram logger enabled! Level: {newLevel.ToString()}");
							}
							return;
						case "/ping":
							if (args.Length != 1) {
								await reply("No argument accepted with this command!");
								return;
							}
							await reply("Pong");
							return;
						case "/selftest":
							if (args.Length != 1) {
								await reply("No argument accepted with this command!");
								return;
							}
							await RunSafeSelfTest(reply, ct);
							return;
						case "/stopautoping":
							if (args.Length != 1) {
								await reply("No argument accepted with this command!");
								return;
							}
							StopAutoPingCb();
							await reply("AutoPing stopped!");
							return;


						case "/startautoping":
							if (args.Length != 1) {
								await reply("No argument accepted with this command!");
								return;
							}
							StartAutoPingCb();
							await reply("AutoPing started!");
							return;
						case "/help":
							if (args.Length != 1) {
								await reply("No argument accepted with this command!");
								return;
							}
							await reply(HelpText());
							return;
						default:

							return;
					}
				}
				// Check if instance is correct
				else if (currInstanceIndex < 0 || currInstanceIndex >= instances.Count()) {
					await reply("Select an instance with /setmain !");
					return;
				} else if (commands.Any(x => text.ToLower().Contains(x))) {
					//Handle /commands@botname in string if exist
					if (text.Contains("@") && text.Split(" ").Length == 1)
						text = text.ToLower().Split(' ')[0].Split('@')[0];

					TBotMain currInstance = instances.ElementAt(currInstanceIndex).Instance;
					args = text.ToLower().Split(' ');
					arg = args.ElementAt(0);

					try {
						await currInstance.WaitFeature();

						switch (arg) {

							case "/getfleets":
								if (args.Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}
								await currInstance.TelegramGetFleets();

								return;

							case "/getcurrentauction":
								if (args.Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.TelegramGetCurrentAuction();

								return;

							case "/subscribeauction":
								// If there is no auction in progress, then we will trigger a timer when next auction will be in place
								await currInstance.TelegramSubscribeToNextAuction();

								return;

							case "/bidauction":
								if (args.Length == 1) {
									// Bid minimum amount
									await currInstance.TelegramBidAuctionMinimum();
								} else if (args.Length < 3) {
									await reply(
										"To bid auction must format: <code>/bidauction 33651579 M:1000 C:1000 D:1000 </code> \n" +
										"Or <code>/bidauction</code> to bid minimum amount to take auction");
									return;
								} else {
									// First string has to be a valid celestialID
									try {
										myCelestials = currInstance.userData.celestials.ToList();
										celestial = myCelestials.Single(celestial => celestial.ID == int.Parse(args[1]));
										// If above has not thrown InvalidOperationException, then remaining can be any resource
										resources = Resources.FromString(string.Join(' ', args.Skip(2)));
										if (resources.TotalResources > 0)
											await currInstance.TelegramBidAuction(celestial, resources);
										else
											await reply("Cannot bid to auction with 0 resources set!");
									} catch (Exception e) {
										await reply($"Error parsing bid auction command \"{e.Message}\"");
									}
								}

								return;


							case "/ghost":
								if (args.Length != 2) {
									await reply("Duration (in hours) argument required! Format: <code>/ghost 4h3m or 3m50s or 1h</code>");
									return;
								}
								arg = text.Split(' ')[1];
								duration = FormattingHelper.ParseDurationFromString(arg);

								celestial = await currInstance.TelegramGetCurrentCelestial();
								await currInstance.FleetScheduler.AutoFleetSave(celestial, false, duration, false, Missions.None, true);

								return;


							case "/ghostto":
								if (args.Length != 3) {
									await reply("Duration (in hours) and mission arguments required! Format: <code>/ghostto 4h3m or 3m50s or 1h Harvest</code>");
									return;
								}
								arg = text.Split(' ')[1];

								Missions mission;
								if (!Enum.TryParse(args[2], true, out mission)) {
									await reply($"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}
								duration = FormattingHelper.ParseDurationFromString(args[1]);

								celestial = await currInstance.TelegramGetCurrentCelestial();
								await currInstance.FleetScheduler.AutoFleetSave(celestial, false, duration, false, mission, true);

								return;

							case "/ghostmoons":
								if (args.Length != 3) {
									await reply("Duration (in hours) argument required! Format: <code>/ghostmoons 4h3m or 3m50s or 1h <mission></code>!");
									return;
								}

								Missions mission_to_do;
								if (!Enum.TryParse(args[2], true, out mission_to_do)) {
									await reply($"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'. Got \"{test}\"");
									return;
								}
								duration = FormattingHelper.ParseDurationFromString(args[1]);

								List<Celestial> myMoons = currInstance.userData.celestials.Where(p => p.Coordinate.Type == Celestials.Moon).ToList();
								if (myMoons.Count > 0) {
									int fleetSaved = 0;
									foreach (Celestial moon in myMoons) {
										await reply($"Enqueueign FleetSave for {moon.ToString()}...");
										await currInstance.FleetScheduler.AutoFleetSave(moon, false, duration, false, mission_to_do, true);
										// Let's sleep a bit :)
										fleetSaved++;
										if (fleetSaved != myMoons.Count)
											await Task.Delay(RandomizeHelper.CalcRandomInterval(IntervalType.AFewSeconds), ct);
									}
									await reply("Moons FleetSave done!");
								} else {
									await reply("No moons found");
								}

								return;


							case "/ghostsleep":
								if (args.Length != 3) {
									await reply("Duration (in hours) argument required! Format: <code>/ghostsleep 4h3m or 3m50s or 1h Harvest</code>");
									return;
								}
								duration = FormattingHelper.ParseDurationFromString(args[1]);

								if (!Enum.TryParse(args[2], true,out mission)) {
									await reply($"{test} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}

								celestial = await currInstance.TelegramGetCurrentCelestial();
								currInstance.telegramUserData.CurrentCelestialToSave = celestial;
								currInstance.telegramUserData.Mission = mission;
								await currInstance.FleetScheduler.AutoFleetSave(celestial, false, duration, true, currInstance.telegramUserData.Mission, true);
								return;


							case "/ghostsleepall":
								if (args.Length != 3) {
									await reply("Duration (in hours) argument required! Format: <code>/ghostsleep 4h3m or 3m50s or 1h Harvest</code>");
									return;
								}
								arg = text.Split(' ')[1];
								duration = FormattingHelper.ParseDurationFromString(args[1]);

								if (!Enum.TryParse(args[2], true, out mission)) {
									await reply($"{args[2]} error: Mission argument must be 'Harvest', 'Deploy', 'Transport', 'Spy' or 'Colonize'");
									return;
								}

								celestial = await currInstance.TelegramGetCurrentCelestial();
								await currInstance.FleetScheduler.AutoFleetSave(celestial, false, duration, true, mission, true, true);
								return;


							case "/switch":
								if (text.Split(' ').Length != 2) {
									await reply("Speed argument required! Format: <code>5 for 50%</code>");
									return;
								}
								test = text.Split(' ')[1];
								speed = decimal.Parse(test);

								if (1 <= speed && speed <= 10) {
									await currInstance.TelegramSwitch(speed);
									return;
								}
								await reply($"{test} error: Speed argument must be 1 or 2 or 3 for 10%, 20%, 30% etc.");
								return;


							case "/deploy":
								if (text.Split(' ').Length != 4) {
									await reply("Coordinates, celestial type and speed arguments are needed! Format: <code>/deploy 2:56:8 moon/planet 1/3/5/7/10</code>");

									return;
								}

								try {
									coord.Galaxy = int.Parse(text.Split(' ')[1].Split(':')[0]);
									coord.System = int.Parse(text.Split(' ')[1].Split(':')[1]);
									coord.Position = int.Parse(text.Split(' ')[1].Split(':')[2]);
								} catch {
									await reply("Error while parsing coordinates! Format: <code>3:125:9</code>");
									return;
								}

								Celestials type;
								arg = text.ToLower().Split(' ')[2];
								if (!arg.Equals("moon") && !arg.Equals("planet")) {
									await reply($"Celestial type argument is needed! Format: <code>/celestial 2:41:9 moon/planet</code>");
									return;
								}
								arg = char.ToUpper(arg[0]) + arg.Substring(1);
								if (Enum.TryParse(arg, out type)) {
									coord.Type = type;
								}

								test = text.Split(' ')[3];
								speed = decimal.Parse(test);

								if (1 <= speed && speed <= 10) {
									celestial = await currInstance.TelegramGetCurrentCelestial();
									await currInstance.TelegramDeploy(celestial, coord, speed);
									return;
								}
								await reply($"{test} error: Speed argument must be 1 or 2 or 3 for 10%, 20%, 30% etc.");

								return;


							case "/jumpgate":
								if (text.Split(' ').Length != 3) {
									await reply("Destination coordinates and full/auto arguments are needed (auto: keeps required cargo for resources) Format: <code>/jumpgate 2:20:8 auto</code>");
									return;
								}

								try {
									coord.Galaxy = int.Parse(text.Split(' ')[1].Split(':')[0]);
									coord.System = int.Parse(text.Split(' ')[1].Split(':')[1]);
									coord.Position = int.Parse(text.Split(' ')[1].Split(':')[2]);
								} catch {
									await reply("Error while parsing coordinates! Format: <code>3:125:9</code>");
									return;
								}

								string mode = text.ToLower().Split(' ')[2];
								if (!mode.Equals("full") && !mode.Equals("auto")) {
									await reply("Eerror! Format: <code>/jumpgate 2:20:8 auto/full</code>");
									return;
								}

								celestial = await currInstance.TelegramGetCurrentCelestial();
								await currInstance.TelegramJumpGate(celestial, coord, mode);
								return;

							case "/phalanx": {
								if (args.Length != 3) {
									await reply("Error! Format: <code>2:241:9 4:100:1</code>");
									return;
								}

								// Parse Origin
								try {
									coord.Galaxy = int.Parse(args[1].Split(':')[0]);
									coord.System = int.Parse(args[1].Split(':')[1]);
									coord.Position = int.Parse(args[1].Split(':')[2]);
									coord.Type = Celestials.Moon;
								} catch {
									await reply("Error while parsing origin coordinates! Format: <code>3:125:9</code>");
									return;
								}

								// Parse destination
								try {
									target = new();
									target.Galaxy = int.Parse(args[2].Split(':')[0]);
									target.System = int.Parse(args[2].Split(':')[1]);
									target.Position = int.Parse(args[2].Split(':')[2]);
									target.Type = Celestials.Planet;
								} catch {
									await reply("Error while parsing destination coordinates! Format: <code>3:125:9</code>");
									return;
								}

								// Check if Origin is a valid moon
								myCelestials = currInstance.userData.celestials.Where(p => p.Coordinate.IsSame(coord)).ToList();
								if (myCelestials.Count > 0) {
									await reply($"Phalanx from \"{myCelestials[0].Coordinate.ToString()}\" to \"{target.ToString()}\"");
									await currInstance.TelegramPhalanx(myCelestials[0], target);

								} else {
									await reply($"Origin coordinate \"{coord.ToString()}\" is not a Moon or does not belong to us!");
								}
								return;
							}



							case "/build":
								string listbuildables = "RocketLauncher\nLightLaser\nHeavyLaser\nGaussCannon\nPlasmaTurret\nSmallCargo\nLargeCargo\nLightFighter\nCruiser\nBattleship\nRecycler\nDestroyer\nBattlecruiser\nDeathstar\nCrawler\nPathfinder";
								decimal number = 0;
								Buildables buildable = Buildables.Null;

								if (text.Split(' ').Length < 2) {
									await reply($"English buildable name required such as:\n{listbuildables}");
									return;
								}
								if (text.Split(' ').Length == 3) {
									try {
										number = int.Parse(text.Split(' ')[2]);
									} catch {
										await reply("Error while parsing number value!");
										return;
									}
								}
								if (Enum.TryParse(text.Split(' ')[1], out buildable)) {
									await currInstance.TelegramBuild(buildable, number);
								} else {
									await reply("Error while parsing buildable value!");
									return;
								}
								return;


							case "/cancel":
								if (text.Split(' ').Length != 2) {
									await reply("Mission argument required!");
									return;
								}
								arg = text.Split(' ')[1];
								int fleetId = int.Parse(arg);

								await currInstance.TelegramRetireFleet(fleetId);
								return;


							case "/cancelmission":
								if (text.Split(' ').Length != 2) {
									await reply("Mission argument required!");
									return;
								}
								arg = text.Split(' ')[1];
								Missions.TryParse(arg, out mission);

								await currInstance.TelegramRetireFleetM(mission);
								return;


							case "/cancelghostsleep":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.TelegramCancelGhostSleep();
								return;


							case "/recall":
								if (text.Split(' ').Length < 2) {
									await reply("Enable/disable auto fleetsave recall argument required! Format: <code>/recall true/false</code>");
									return;
								}

								if (text.Split(' ')[1] != "true" && text.Split(' ')[1] != "false") {
									await reply("Argument must be <code>true</code> or <code>false</code>.");
									return;
								}
								string recall = text.Split(' ')[1];

								if (await currInstance.EditSettings(null, Feature.Null, recall))
									await reply($"Recall value updated to {recall}.");
								return;


							case "/sleep":
								if (text.Split(' ').Length != 2) {
									await reply("Time argument required!");
									return;
								}
								arg = text.Split(' ')[1];
								duration = FormattingHelper.ParseDurationFromString(arg);
								var instanceWithBridge = instances.First(i => i.Instance.InstanceAlias == currInstance.InstanceAlias);
								DateTime timeNow = await instanceWithBridge.TBotOGamedBridge.GetDateTime();
								DateTime WakeUpTime = timeNow.AddSeconds(duration);

								await currInstance.SleepNow(WakeUpTime);
								return;


							case "/wakeup":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}
								currInstance.WakeUpNow(null);
								return;


							case "/msg":
								if (text.Split(' ').Length < 2) {
									await reply("Need message argument!");
									return;
								}
								arg = text.Split(new[] { ' ' }, 2).Last();
								await currInstance.TelegramMesgAttacker(arg);
								return;


							case "/minexpecargo":
								if (text.Split(' ').Length < 2) {
									await reply("Need minimum cargo number argument!");
									return;
								}
								if (!int.TryParse(text.Split(' ')[1], out int value)) {
									await reply("argument must be an integer!");
									return;
								}

								arg = text.Split(' ')[1];
								int cargo = int.Parse(arg);
								if (await currInstance.EditSettings(null, Feature.Null, string.Empty, cargo))
									await reply($"MinPrimaryToSend value updated to {cargo}.");
								return;


							case "/stopexpe":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.StopFeature(Feature.Expeditions);
								await reply("Expeditions stopped!");
								return;


							case "/startexpe":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.InitializeFeature(Feature.Expeditions);
								await reply("Expeditions initialized!");
								return;


							case "/collect":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								currInstance.TelegramCollect();
								return;


							case "/collectall":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								currInstance.TelegramCollect(true);
								return;


							case "/collectdeut":
								if (text.Split(' ').Length != 2) {
									await reply("Need minimum deut amount argument <code>/collectdeut 500000</code>");
									return;
								}
								if (!int.TryParse(text.Split(' ')[1], out int val)) {
									await reply("argument must be an integer!");
									return;
								}

								long MinAmount = int.Parse(text.Split(' ')[1]);
								await currInstance.TelegramCollectDeut(MinAmount);
								return;


							case "/stopautoresearch":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.StopFeature(Feature.BrainAutoResearch);
								await reply("AutoResearch stopped!");
								return;


							case "/startautoresearch":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.InitializeFeature(Feature.BrainAutoResearch);
								await reply("AutoResearch started!");
								return;


							case "/stopautomine":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.StopFeature(Feature.BrainAutoMine);
								await reply("AutoMine stopped!");
								return;


							case "/startautomine":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.InitializeFeature(Feature.BrainAutoMine);
								await reply("AutoMine started!");
								return;


							case "/stoplifeformautomine":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.StopFeature(Feature.BrainLifeformAutoMine);
								await reply("Lifeform AutoMine stopped!");
								return;


							case "/startlifeformautomine":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.InitializeFeature(Feature.BrainLifeformAutoMine);
								await reply("Lifeform AutoMine started!");
								return;


							case "/stoplifeformautoresearch":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.StopFeature(Feature.BrainLifeformAutoResearch);
								await reply("Lifeform AutoResearch stopped!");
								return;


							case "/startlifeformautoresearch":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.InitializeFeature(Feature.BrainLifeformAutoResearch);
								await reply("Lifeform AutoResearch started!");
								return;


							case "/stopdefender":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.StopFeature(Feature.Defender);
								await reply("Defender stopped!");
								return;


							case "/startdefender":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.InitializeFeature(Feature.Defender);
								await reply("Defender started!");
								return;


							case "/stopautofarm":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.StopFeature(Feature.AutoFarm);
								await reply("Autofarm stopped!");
								return;


							case "/startautofarm":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.InitializeFeature(Feature.AutoFarm);
								await reply("Autofarm started!");
								return;

							case "/stopautodiscovery":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.StopFeature(Feature.AutoDiscovery);
								await reply("Autodiscovery stopped!");
								return;


							case "/startautodiscovery":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}

								await currInstance.InitializeFeature(Feature.AutoDiscovery);
								await reply("Autodiscovery started!");
								return;


							case "/getinfo":
								args = text.Split(' ');
								if (args.Length == 1) {
									celestial = await currInstance.TelegramGetCurrentCelestial();
									await currInstance.TelegramGetInfo(celestial);

									return;
								} else if (args.Length == 2) {
									myCelestials = currInstance.userData.celestials.ToList();
									// Try celestial ID first
									try {
										celestialID = int.Parse(args[1]);
										celestial = myCelestials.Single(c => c.ID == celestialID);
										await currInstance.TelegramGetInfo(celestial);

									} catch (Exception e) {
										await reply(
											$"Invalid arguments specified for Format <code>/getinfo 321312132</code>\n" +
											$"Error:{e.Message}");
									}
									return;
								} else if (args.Length == 3) {
									myCelestials = currInstance.userData.celestials.ToList();
									// Try format Galaxy:System:Position (Moon|Planet)
									try {
										coord = Coordinate.FromString(string.Join(' ', args.Skip(1)));
										celestial = myCelestials.Single(c => c.Coordinate.IsSame(coord));
										await currInstance.TelegramGetInfo(celestial);

									} catch (Exception e) {
										await reply(
											$"Invalid arguments specified for Format <code>/getinfo 2:48:5 Moon/Planet</code>\n" +
											$"Error:{e.Message}");
									}
								} else {
									await reply("Invalid number of argument specified for current command");
								}


								return;


							case "/celestial":
								if (text.Split(' ').Length != 3) {
									await reply("Coordinate and celestial type arguments required! Format: <code>/celestial 2:56:8 moon/planet</code>");

									return;
								}

								arg = text.ToLower().Split(' ')[2];
								if (!arg.Equals("moon") && !arg.Equals("planet")) {
									await reply($"Celestial type argument required! Format: <code>/celestial 2:41:9 moon/planet</code>");
									return;
								}

								try {
									coord.Galaxy = int.Parse(text.Split(' ')[1].Split(':')[0]);
									coord.System = int.Parse(text.Split(' ')[1].Split(':')[1]);
									coord.Position = int.Parse(text.Split(' ')[1].Split(':')[2]);
								} catch {
									await reply("Error while parsing coordinates! Format: <code>3:125:9</code>");
									return;
								}

								arg = char.ToUpper(arg[0]) + arg.Substring(1);
								currInstance.TelegramSetCurrentCelestial(coord, arg);
								return;


							case "/editsettings":
								if (text.Split(' ').Length < 3) {
									await reply("Coordinate and celestial type arguments required! Format: <code>/editsettings 2:56:8 moon/planet (AutoMine/AutoResearch/AutoRepatriate/Expeditions/Colonize)</code>");
									return;
								}

								arg = text.ToLower().Split(' ')[2];
								if (!arg.Equals("moon") && !arg.Equals("planet")) {
									await reply($"Celestial type argument needed! Format: <code>/editsettings 2:100:3 moon/planet (AutoMine/AutoResearch/AutoRepatriate/Expeditions/Colonize)</code>");
									return;
								}

								try {
									coord.Galaxy = int.Parse(text.Split(' ')[1].Split(':')[0]);
									coord.System = int.Parse(text.Split(' ')[1].Split(':')[1]);
									coord.Position = int.Parse(text.Split(' ')[1].Split(':')[2]);
								} catch {
									await reply("Error while parsing coordinates! Format: <code>3:125:9 moon/planet (AutoMine/AutoResearch/AutoRepatriate/Expeditions/Colonize)</code>");
									return;
								}
								var celestialType = char.ToUpper(arg[0]) + arg.Substring(1);

								Feature updateType = Feature.Null;
								if (text.ToLower().Split(' ').Length > 3) {
									arg = text.ToLower().Split(' ')[3];
									if (!arg.Equals("AutoMine") && !arg.Equals("AutoResearch") && !arg.Equals("AutoRepatriate") && !arg.Equals("Expeditions") && !arg.Equals("Colonize")) {
										await reply($"Update type argument not valid! Format: <code>/editsettings 2:100:3 moon/planet (AutoMine/AutoResearch/AutoRepatriate/Expeditions/Colonize)</code>");
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
											case "Colonize":
												updateType = Feature.Colonize;
												break;
											default:
												break;
										}
									}
								}

								currInstance.TelegramSetCurrentCelestial(coord, celestialType, updateType, true);
								return;


							case "/spycrash":
								if (text.Split(' ').Length != 2) {
									await reply("<code>auto</code> or coordinate argument needed! Format: <code>/spycrash auto/2:56:8</code>");
									return;
								}

								if (text.Split(' ')[1].ToLower().Equals("auto")) {
									target = null;
								} else {
									try {
										coord.Galaxy = int.Parse(text.Split(' ')[1].Split(':')[0]);
										coord.System = int.Parse(text.Split(' ')[1].Split(':')[1]);
										coord.Position = int.Parse(text.Split(' ')[1].Split(':')[2]);
										target = new Coordinate() { Galaxy = coord.Galaxy, System = coord.System, Position = coord.Position, Type = Celestials.Planet };
									} catch {
										await reply("Error while parsing coordinates! Format: <code>3:125:9</code>, or <code>auto</code>");
										return;
									}
								}
								Celestial origin = await currInstance.TelegramGetCurrentCelestial();

								await currInstance.FleetScheduler.SpyCrash(origin, target);
								return;


							case "/attacked":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}
								bool isUnderAttack = await currInstance.TelegramIsUnderAttack();

								if (isUnderAttack) {
									await reply("Yes! You're still under attack!");
								} else {
									await reply("Nope! Your empire is safe.");
								}
								return;


							case "/getcelestials":
								if (text.Split(' ').Length != 1) {
									await reply("No argument accepted with this command!");
									return;
								}
								myCelestials = currInstance.userData.celestials.ToList();
								string celestialStr = "";
								foreach (Celestial c in myCelestials) {
									celestialStr += $"{c.Name.PadRight(16, ' ')} {c.Coordinate.ToString().PadRight(16)} {c.ID}\n";
								}
								await reply(celestialStr);

								return;
							default:
								return;
						}

					} catch (Exception ex) when (ex.GetType().Name == "ApiRequestException") {
						await reply($"ApiRequestException Error!\nTry /ping to check if bot still alive!");
						return;

					} catch (FormatException) {
						await reply($"FormatException Error!\nYou entered an unexpected value (string instead of integer?)\nTry /ping to check if bot still alive!");
						return;

					} catch (NullReferenceException) {
						await reply($"NullReferenceException Error!\n Something unknown went wrong!\nTry /ping to check if bot still alive!");
						return;

					} catch (OperationCanceledException) when (ct.IsCancellationRequested == true) {
						await reply($"OperationCanceledException Maybe TelegramMessenger has been disabled.");
						return;

					} catch (Exception) {
						await reply($"Unknown Exception Error!\nTry /ping to check if bot still alive!");
						return;

					} finally {
						currInstance.releaseFeature();
					}
				}
		}

		internal static string NormalizeCommand(string text) {
			text = text.Trim();
			if (text.StartsWith('!')) {
				text = "/" + text.Substring(1);
			}
			return text;
		}

		private int _selfTestRunning;

		public async Task RunSafeSelfTest(Func<string, Task> reply, CancellationToken ct) {
			if (Interlocked.CompareExchange(ref _selfTestRunning, 1, 0) != 0) {
				await reply("Self-test already running.");
				return;
			}
			try {
				await reply("Starting **read-only** self-test. Sleep/ghost/build/collect/deploy/start-stop will not run.");
				string[] cmds = {
					"!ping",
					"!listinstances",
					"!getmain",
					"!setmain 0",
					"!getmain",
					"!getmainstats",
					"!getfleets",
					"!getcelestials",
					"!getinfo",
					"!attacked",
					"!getcurrentauction",
					"!loglevel",
					"!setmain 1",
					"!getmain",
					"!getcelestials",
					"!getinfo",
					"!attacked",
					"!setmain 0",
					"!getmain"
				};
				foreach (string cmd in cmds) {
					ct.ThrowIfCancellationRequested();
					await reply($"▶️ `{cmd}`");
					try {
						await HandleAsync(cmd, reply, ct);
					} catch (Exception ex) {
						await reply($"❌ `{cmd}` failed: {ex.Message}");
					}
					await Task.Delay(400, ct);
				}
				await reply("Read-only self-test finished. In Discord you can also type `!help` yourself to confirm inbound messages.");
			} finally {
				Interlocked.Exchange(ref _selfTestRunning, 0);
			}
		}

		internal static string HelpText() {
			return
				"On Discord, prefer !help / !startexpe (a leading / is often captured by Discord slash commands).\n" +
				"\t Core Commands\n" +
				"/setmain - Set the TBot main instance to pilot. Format <code>/setmain 0</code>\n" +
				"/getmain - Get the current TBot instance that Telegram is managing\n" +
				"/getmainstats - Get current TBot instance statistics\n" +
				"/listinstances - List TBot main instances\n" +
				"/loglevel - Get current log level on telegram logging\n" +
				"/setloglevel - Set log level on telegram logging and enables it. Format <code>/setloglevel Debug|Information|Warning|Error </code>\n" +
				"/ping - Ping bot\n" +
				"/selftest - Run read-only Discord/Telegram command checks\n" +
				"/stopautoping - stop autoping\n" +
				"/startautoping - start autoping [Receive message every X hours]\n" +
				"/help - Display this help\n" +
				"\n\t TBot Main instance commands\n" +
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
				"/phalanx - use phalanx from moon to destination. Format <code>/phalanx 2:241:9 4:100:1</code>\n" +
				"/cancelghostsleep - Cancel planned /ghostsleep(expe) if not already sent\n" +
				"/spycrash - Create a debris field by crashing a probe on target or automatically selected planet. Format: <code>/spycrash 2:41:9/auto</code>\n" +
				"/recall - Enable/disable fleet auto recall. Format: <code>/recall true/false</code>\n" +
				"/collect - Collect planets resources to JSON setting celestial\n" +
				"/collectall - Collect planets resources to JSON setting celestial with no MinimumResources\n" +
				"/build - Try to build buildable on each planet. Build max possible if no number value sent <code>/build LightFighter [100]</code>\n" +
				"/collectdeut - Collect planets only deut resources -> to JSON repatriate setting celestial\n" +
				"/msg - Send a message to current attacker. Format: <code>/msg hello dude</code>\n" +
				"/sleep - Stop bot for the specified amount of hours. Format: <code>/sleep 4h3m or 3m50s</code>\n" +
				"/wakeup - Wakeup bot\n" +
				"/cancel - Cancel fleet with specified ID. Format: <code>/cancel 65656</code>\n" +
				"/cancelmission - Cancel all fleets with specified mission. Format: <code>/cancel Deploy</code> or other mission\n" +
				"/getcelestials - Return the list of your celestials\n" +
				"/attacked - check if you're (still) under attack\n" +
				"/celestial - Update program current celestial target. Format: <code>/celestial 2:45:8 Moon/Planet</code>\n" +
				"/getinfo - Get current celestial resources and ships. Additional arg format has to be <code>/getinfo 2:45:8 Moon/Planet</code>\n" +
				"/editsettings - Edit JSON file to change Expeditions, Colonize, Autominer's and Autoresearch Transport Origin, Repatriate and AutoReseach Target celestial. Format: <code>/editsettings 2:425:9 Moon</code>\n" +
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
				"/stopautodiscovery - stop autodiscovery\n" +
				"/startautodiscovery - start autodiscovery\n";
		}
	}
}