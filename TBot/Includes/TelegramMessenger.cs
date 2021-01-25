using Tbot.Model;
using System;
using System.Collections.Generic;
using System.Text;
using Telegram.Bot;

namespace Tbot.Includes
{
    class TelegramMessenger
    {
        public string Api { get; private set; }
        public string Channel { get; private set; }
        private TelegramBotClient Client { get; set; }
        public TelegramMessenger(string api, string channel)
        {
            Api = api;
            Client = new TelegramBotClient(Api);
            Channel = channel;
        }

        public async void SendMessage(string message)
        {
            Helpers.WriteLog(LogType.Info, LogSender.Tbot, "Sending Telegram message...");
            await Client.SendTextMessageAsync(Channel, message);
        }
    }
}
