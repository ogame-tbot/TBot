using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Tbot.Helpers;
using Tbot.Includes;
using Tbot.Workers;
using TBot.Common.Logging;
using Microsoft.Extensions.Logging;

namespace Tbot.Services {
	internal class DiscordWebhookMessenger : IDiscordMessenger {
		private class InstanceWithBridge {
			public InstanceWithBridge(TBotMain instance, ITBotOgamedBridge tBotOgamedBridge) {
				Instance = instance;
				TBotOGamedBridge = tBotOgamedBridge;
			}
			public TBotMain Instance { get; set; }
			public ITBotOgamedBridge TBotOGamedBridge { get; set; }
		}

		public string WebhookUrl { get; private set; }

		private readonly ILoggerService<DiscordWebhookMessenger> _logger;
		private readonly HttpClient _httpClient;
		private readonly string _username;
		private readonly SemaphoreSlim _sendSem = new(1, 1);
		private readonly SemaphoreSlim _instanceSem = new(1, 1);
		private readonly List<InstanceWithBridge> _instances = new();
		private readonly DateTime _startTime = DateTime.UtcNow;

		private Timer _autoPingTimer;
		private SemaphoreSlim _pingSem;
		private long _pingHours;

		public DiscordWebhookMessenger(ILoggerService<DiscordWebhookMessenger> logger, string webhookUrl, string username = "TBot") {
			_logger = logger;
			WebhookUrl = webhookUrl?.Trim() ?? "";
			_username = string.IsNullOrWhiteSpace(username) ? "TBot" : username;
			_httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
		}

		public void StartAutoPing(long everyHours) {
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
				foreach (var instanceWithBridge in _instances) {
					var instance = instanceWithBridge.Instance;
					TimeSpan instanceUpTime = now - instance.startTime;
					int instanceIndex = _instances.IndexOf(instanceWithBridge);
					pingStr += $"#{instanceIndex} `{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}` since {FormattingHelper.TimeSpanToString(instanceUpTime)}\n";
				}
				await SendMessage(pingStr);
				_logger.WriteLog(LogLevel.Information, LogSender.Discord, $"AutoPing sent, Next ping at {newTime}");
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Error, LogSender.Discord, $"Error in AutoPing: {ex.Message}");
			} finally {
				_pingSem.Release();
			}
		}

		public async Task AddTbotInstance(TBotMain instance, ITBotOgamedBridge tbotOgamedBridge) {
			_logger.WriteLog(LogLevel.Information, LogSender.Discord, $"Adding instance [{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}]");
			await _instanceSem.WaitAsync();
			try {
				if (!_instances.Any(i => i.Instance.InstanceAlias == instance.InstanceAlias)) {
					var instanceWithBridge = new InstanceWithBridge(instance, tbotOgamedBridge);
					_instances.Add(instanceWithBridge);
					int instanceIndex = _instances.IndexOf(instanceWithBridge);
					await SendMessage($"`[{instance.userData.userInfo.PlayerName}@{instance.userData.serverData.Name}]` Instance added! (Index:{instanceIndex})");
				}
			} finally {
				_instanceSem.Release();
			}
		}

		public async Task RemoveTBotInstance(TBotMain instance) {
			await _instanceSem.WaitAsync();
			try {
				_instances.RemoveAll(i => i.Instance.InstanceAlias == instance.InstanceAlias);
			} finally {
				_instanceSem.Release();
			}
		}

		public async Task SendMessage(string message) {
			if (string.IsNullOrWhiteSpace(WebhookUrl) || string.IsNullOrWhiteSpace(message)) {
				return;
			}

			await _sendSem.WaitAsync();
			try {
				foreach (var chunk in DiscordTextFormatter.Chunk(DiscordTextFormatter.ToDiscordMarkdown(message))) {
					var payload = JsonConvert.SerializeObject(new {
						username = _username,
						content = chunk
					});
					using var content = new StringContent(payload, Encoding.UTF8, "application/json");
					using var response = await _httpClient.PostAsync(WebhookUrl, content);
					if (!response.IsSuccessStatusCode) {
						_logger.WriteLog(LogLevel.Warning, LogSender.Discord, $"Webhook POST failed: {(int) response.StatusCode} {response.ReasonPhrase}");
					}
					await Task.Delay(350);
				}
			} catch (Exception ex) {
				_logger.WriteLog(LogLevel.Error, LogSender.Discord, $"Error sending webhook: {ex.Message}");
			} finally {
				_sendSem.Release();
			}
		}

		public async Task Disable() {
			StopAutoPing();
			await _instanceSem.WaitAsync();
			try {
				foreach (var instanceWithBridge in _instances.ToList()) {
					instanceWithBridge.Instance.RemoveDiscordMessenger();
				}
				_instances.Clear();
			} finally {
				_instanceSem.Release();
			}
			_httpClient.Dispose();
		}
	}
}
