using System.Reflection;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.Extensions.FileProviders;
using Tbot.Common.Settings;
using TBot.Common.Logging.Hubs;

namespace TBot.WebUI {
	public static class WebApp {
		static WebApp() {
			_builder = WebApplication.CreateBuilder();
		}

		private static WebApplicationBuilder _builder;
		private static WebApplication _webApplication;

		public static IServiceCollection GetServiceCollection() {
			return _builder.Services;
		}

		public static IServiceProvider Build(string settingsPath) {
			var assembly = Assembly.GetExecutingAssembly();
			_builder.Services.AddControllersWithViews()
				.AddRazorRuntimeCompilation()
				.AddApplicationPart(assembly)
				.AddControllersAsServices();
			_builder.Services.AddSignalR();

			Console.WriteLine($"Folder: {AppDomain.CurrentDomain.BaseDirectory}");

			var settingsFile = SettingsService.GetSettings(settingsPath);
			string urls = (string) settingsFile.WebUI.Urls;

			_builder.WebHost.UseUrls(urls.Split(",").Select(c => c.Trim()).ToArray());
			_webApplication = _builder.Build();
			return _webApplication.Services;
		}

		public static async Task Main(CancellationToken ct) {
			// Configure the HTTP request pipeline.
			if (!_webApplication.Environment.IsDevelopment()) {
				_webApplication.UseExceptionHandler("/Home/Error");
				// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
				_webApplication.UseHsts();
			}

			_webApplication.UseHttpsRedirection();

			var filesProvider = new ManifestEmbeddedFileProvider(Assembly.GetExecutingAssembly(), "wwwroot");
			_webApplication.UseStaticFiles(new StaticFileOptions() {
				FileProvider = filesProvider
			});

			_webApplication.UseRouting();
			_webApplication.UseEndpoints(endpoints => {
				endpoints.MapHub<WebHub>("/realTimeLog");
			});

			_webApplication.UseAuthorization();

			_webApplication.MapControllerRoute(
				name: "default",
				pattern: "{controller=Home}/{action=Index}/{id?}");

			await _webApplication.RunAsync(ct);
		}
	}
}
