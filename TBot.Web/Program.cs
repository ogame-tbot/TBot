namespace TBot.Web {
	public static class WebProgram {
		public static async Task Main(CancellationToken cts) {
			var builder = WebApplication.CreateBuilder();

			// Add services to the container.
			builder.Services.AddRazorPages();

			builder.WebHost.UseUrls("http://localhost:8091", "https://localhost:8092");

			var app = builder.Build();

			// Configure the HTTP request pipeline.
			if (!app.Environment.IsDevelopment()) {
				app.UseExceptionHandler("/Error");
				// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
				app.UseHsts();
			}

			app.UseHttpsRedirection();
			app.UseStaticFiles();

			app.UseRouting();

			app.UseAuthorization();

			app.MapRazorPages();
			await app.RunAsync(cts);
		}
	}
}
