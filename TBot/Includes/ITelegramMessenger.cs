using System.Threading;
using System.Threading.Tasks;
using Tbot.Services;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace Tbot.Includes {
	internal interface ITelegramMessenger {
		string Api { get; }
		string Channel { get; }
		ITelegramBotClient Client { get; }

		Task AddTbotInstance(TBotMain instance);
		Task RemoveTBotInstance(TBotMain instance);
		Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken);
		Task SendMessage(ITelegramBotClient client, Chat chat, string message, ParseMode parseMode = ParseMode.Html);
		Task SendMessage(string message, ParseMode parseMode = ParseMode.Html, CancellationToken cancellationToken = default);
		Task SendTyping(CancellationToken cancellationToken);
		void StartAutoPing(long everyHours);
		void StopAutoPing();
		void TelegramBot();
		void TelegramBotDisable();
	}
}
