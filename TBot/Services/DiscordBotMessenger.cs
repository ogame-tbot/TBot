using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using Microsoft.Extensions.Logging;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Workers;
using TBot.Common.Logging;

namespace Tbot.Services {
	internal class DiscordBotMessenger : IDiscordMessenger {
		private readonly ILoggerService<DiscordBotMessenger> _logger;
		private readonly DiscordSocketClient _client;
		private readonly MessengerCommandHandler _commands;
		private readonly ulong _channelId;
		private readonly HashSet<ulong> _allowedUserIds;
		private readonly SemaphoreSlim _sendSem = new(1, 1);
		private readonly SemaphoreSlim _instanceSem = new(1, 1);
		private readonly DateTime _startTime = DateTime.UtcNow;

		private Timer _autoPingTimer;
		private SemaphoreSlim _pingSem;
		private long _pingHours = 1;
		private bool _started;
		private readonly bool _selfTestOnStart;
		private bool _selfTestStarted;

		public DiscordBotMessenger(
			ILoggerService<DiscordBotMessenger> logger,
			string token,
			ulong channelId,
			IEnumerable<ulong> allowedUserIds = null,
			bool selfTestOnStart = false) {
			_logger = logger;
			_channelId = channelId;
			_allowedUserIds = new HashSet<ulong>(allowedUserIds ?? Array.Empty<ulong>());
			_selfTestOnStart = selfTestOnStart;
			_commands = new MessengerCommandHandler(
				() => StartAutoPing(_pingHours),
				StopAutoPing);

			_client = new DiscordSocketClient(new DiscordSocketConfig {
				GatewayIntents = GatewayIntents.Guilds
					| GatewayIntents.GuildMessages
					| GatewayIntents.DirectMessages
					| GatewayIntents.MessageContent,
				LogLevel = LogSeverity.Info,
				MessageCacheSize = 0
			});
			_client.Log += OnDiscordLog;
			_client.Ready += OnReady;
			_client.MessageReceived += OnMessageReceived;
			Token = token;
		}

		private string Token { get; }

		public async Task Start() {
			if (_started) {
				return;
			}
			await _client.LoginAsync(TokenType.Bot, Token);
			await _client.StartAsync();
			_started = true;
		}

		public async Task AddTbotInstance(TBotMain instance, ITBotOgamedBridge tbotOgamedBridge) {
			_logger.WriteLog(LogLevel.Information, LogSender.Discord, $"Adding instance [{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}]");
			bool runSelfTest = false;
			await _instanceSem.WaitAsync();
			try {
				int instanceIndex = _commands.AddTbotInstance(instance, tbotOgamedBridge);
				await SendMessage($"`[{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}]` Instance added! (Index:{instanceIndex})");
				if (_selfTestOnStart && !_selfTestStarted && _commands.InstanceCount >= 2) {
					_selfTestStarted = true;
					runSelfTest = true;
				}
			} finally {
				_instanceSem.Release();
			}
			if (runSelfTest) {
				_ = Task.Run(async () => {
					try {
						await Task.Delay(2500);
						_logger.WriteLog(LogLevel.Information, LogSender.Discord, "Starting Discord read-only self-test");
						await _commands.RunSafeSelfTest(SendMessage, CancellationToken.None);
						_logger.WriteLog(LogLevel.Information, LogSender.Discord, "Discord read-only self-test finished");
					} catch (Exception ex) {
						_logger.WriteLog(LogLevel.Error, LogSender.Discord, $"Discord self-test failed: {ex.Message}");
					}
				});
			}
		}

		public async Task RemoveTBotInstance(TBotMain instance) {
			await _instanceSem.WaitAsync();
			try {
				_commands.RemoveTBotInstance(instance);
			} finally {
				_instanceSem.Release();
			}
		}

		public void StartAutoPing(long everyHours) {
			if (everyHours <= 0) {
				everyHours = 1;
			}
			DateTime now = DateTime.UtcNow;
			DateTime roundedNextHour = now.AddHours(everyHours).AddMinutes(-now.Minute).AddSeconds(-now.Second);
			long nextping = (long) roundedNextHour.Subtract(now).TotalMilliseconds;

			_logger.WriteLog(LogLevel.Information, LogSender.Discord, $"Initializing AutoPing (Hours {everyHours})...");
			StopAutoPing();

			_pingHours = everyHours;
			_pingSem = new SemaphoreSlim(1, 1);
			_autoPingTimer = new Timer(AutoPing, null, nextping, Timeout.Infinite);
		}

		public void StopAutoPing() {
			if (_autoPingTimer != null) {
				_autoPingTimer.Dispose();
				_autoPingTimer = null;
			}
			if (_pingSem != null) {
				_pingSem.Dispose();
				_pingSem = null;
			}
		}

		public async Task SendMessage(string message) {
			if (string.IsNullOrWhiteSpace(message)) {
				return;
			}
			IMessageChannel channel = _client.GetChannel(_channelId) as IMessageChannel;
			if (channel == null) {
				try {
					channel = await _client.Rest.GetChannelAsync(_channelId) as IMessageChannel;
				} catch (Exception ex) {
					_logger.WriteLog(LogLevel.Warning, LogSender.Discord, $"Discord notify channel is not available: {ex.Message}");
					return;
				}
			}
			if (channel == null) {
				_logger.WriteLog(LogLevel.Warning, LogSender.Discord, "Discord notify channel is not available yet.");
				return;
			}
			await SendToChannel(channel, message);
		}

		public async Task Disable() {
			StopAutoPing();
			await _instanceSem.WaitAsync();
			try {
				foreach (var instance in _commands.GetInstances().ToList()) {
					instance.RemoveDiscordMessenger();
				}
			} finally {
				_instanceSem.Release();
			}

			try {
				if (_started) {
					await _client.StopAsync();
					await _client.LogoutAsync();
				}
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Warning, LogSender.Discord, $"Error stopping Discord bot: {ex.Message}");
			} finally {
				_client.MessageReceived -= OnMessageReceived;
				_client.Ready -= OnReady;
				_client.Log -= OnDiscordLog;
				await _client.DisposeAsync();
				_started = false;
			}
		}

		private async void AutoPing(object state) {
			await _pingSem.WaitAsync();
			try {
				DateTime now = DateTime.UtcNow;
				TimeSpan upTime = now - _startTime;
				DateTime roundedNextHour = now.AddHours(_pingHours).AddMinutes(-now.Minute).AddSeconds(-now.Second);
				long nextping = (long) roundedNextHour.Subtract(now).TotalMilliseconds;
				DateTime newTime = now.AddMilliseconds(nextping);
				_autoPingTimer.Change(nextping, Timeout.Infinite);

				string pingStr = $"TBot is running since {FormattingHelper.TimeSpanToString(upTime)}\n";
				int instanceIndex = 0;
				foreach (var instance in _commands.GetInstances()) {
					TimeSpan instanceUpTime = now - instance.startTime;
					pingStr += $"#{instanceIndex} `{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}` since {FormattingHelper.TimeSpanToString(instanceUpTime)}\n";
					instanceIndex++;
				}
				await SendMessage(pingStr);
				_logger.WriteLog(LogLevel.Information, LogSender.Discord, $"AutoPing sent, Next ping at {newTime}");
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Error, LogSender.Discord, $"Error in AutoPing: {ex.Message}");
			} finally {
				_pingSem.Release();
			}
		}

		private Task OnDiscordLog(LogMessage message) {
			var level = message.Severity switch {
				LogSeverity.Critical => LogLevel.Error,
				LogSeverity.Error => LogLevel.Error,
				LogSeverity.Warning => LogLevel.Warning,
				_ => LogLevel.Information
			};
			if (message.Severity <= LogSeverity.Info) {
				_logger.WriteLog(level, LogSender.Discord, message.Exception == null
					? message.Message
					: $"{message.Message} {message.Exception.Message}");
			}
			return Task.CompletedTask;
		}

		private async Task OnReady() {
			try {
				await _client.SetGameAsync("TBot / !help");
				await SendMessage("TBot Discord bot connected. Use `!help` (Discord captures `/` for slash commands).");
				_logger.WriteLog(LogLevel.Information, LogSender.Discord, "Discord bot ready");
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Error, LogSender.Discord, $"Error in Discord Ready: {ex.Message}");
			}
		}

		private async Task OnMessageReceived(SocketMessage message) {
			try {
				if (message.Author.IsBot || message.Author.IsWebhook) {
					return;
				}
				if (message is not SocketUserMessage userMessage) {
					return;
				}

				string text = userMessage.Content ?? "";
				if (_client.CurrentUser != null) {
					text = text
						.Replace($"<@{_client.CurrentUser.Id}>", "", StringComparison.Ordinal)
						.Replace($"<@!{_client.CurrentUser.Id}>", "", StringComparison.Ordinal)
						.Trim();
				}
				if (string.IsNullOrWhiteSpace(text)) {
					return;
				}
				if (!(text.StartsWith('!') || text.StartsWith('/'))) {
					return;
				}

				bool isDm = userMessage.Channel is IDMChannel;
				bool inNotifyChannel = userMessage.Channel.Id == _channelId;
				bool userAllowed = _allowedUserIds.Count == 0 || _allowedUserIds.Contains(userMessage.Author.Id);
				if (isDm) {
					if (_allowedUserIds.Count == 0 || !userAllowed) {
						return;
					}
				} else if (!inNotifyChannel || !userAllowed) {
					return;
				}

				await userMessage.Channel.TriggerTypingAsync();
				await _commands.HandleAsync(text, async (msg) => {
					await SendToChannel(userMessage.Channel, msg);
				}, CancellationToken.None);
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Error, LogSender.Discord, $"Error handling Discord message: {ex.Message}");
			}
		}

		private async Task SendToChannel(IMessageChannel channel, string message) {
			if (channel == null || string.IsNullOrWhiteSpace(message)) {
				return;
			}
			await _sendSem.WaitAsync();
			try {
				foreach (var chunk in DiscordTextFormatter.Chunk(DiscordTextFormatter.ToDiscordMarkdown(message))) {
					await channel.SendMessageAsync(chunk);
					await Task.Delay(350);
				}
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Error, LogSender.Discord, $"Error sending Discord message: {ex.Message}");
			} finally {
				_sendSem.Release();
			}
		}
	}
}
