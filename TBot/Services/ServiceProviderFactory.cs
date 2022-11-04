using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TBot.Common.Logging;
using Tbot.Includes;
using TBot.Ogame.Infrastructure;

namespace Tbot.Services {
	public static class ServiceProviderFactory {
		public static IServiceProvider ServiceProvider { get; }

		static ServiceProviderFactory() {
			ServiceProvider = new ServiceCollection()
				.AddSingleton(typeof(ILoggerService<>), typeof(LoggerService<>))
				.AddScoped<IHelpersService, HelpersService>()
				.AddScoped<IOgameService, OgameService>()
				.AddScoped<ITelegramMessenger, TelegramMessenger>()
				.AddScoped<IInstanceManager, InstanceManager>()
				.BuildServiceProvider();
		}
	}
}
