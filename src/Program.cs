using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace VsixGallery
{
	public class Program
	{
		public static void Main(string[] args)
		{
			CreateHostBuilder(args).Build().Run();
		}

		public static IHostBuilder CreateHostBuilder(string[] args) =>
			Host.CreateDefaultBuilder(args)
				.ConfigureWebHostDefaults(webBuilder =>
				{
					webBuilder.ConfigureKestrel(options =>
					{
						// Don't advertise the server in response headers (was: removeServerHeader in web.config).
						options.AddServerHeader = false;

						// Match the IIS requestLimits/maxAllowedContentLength from web.config (~500 MB).
						options.Limits.MaxRequestBodySize = 500_000_000;
					});

					webBuilder.UseStartup<Startup>();
				});
	}
}
