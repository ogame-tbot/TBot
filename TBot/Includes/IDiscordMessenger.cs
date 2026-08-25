using System.Threading.Tasks;
using Tbot.Services;
using Tbot.Workers;

namespace Tbot.Includes {
	public interface IDiscordMessenger {
		Task AddTbotInstance(TBotMain instance, ITBotOgamedBridge tbotOgameBridge);
		Task RemoveTBotInstance(TBotMain instance);
		Task SendMessage(string message);
		void StartAutoPing(long everyHours);
		void StopAutoPing();
		Task Disable();
	}
}
