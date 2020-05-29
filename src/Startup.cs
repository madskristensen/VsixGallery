using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Security;
using System.Threading.Tasks;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace VsixGallery
{
	public class Startup
	{
		public Startup(IConfiguration configuration)
		{
			Configuration = configuration;
		}

		public IConfiguration Configuration { get; }

		// This method gets called by the runtime. Use this method to add services to the container.
		public void ConfigureServices(IServiceCollection services)
		{
			services.AddRazorPages();
			services.AddMvc(options => options.EnableEndpointRouting = false);

			services.AddWebOptimizer(pipeline =>
				pipeline.CompileScssFiles()
			);
		}

		// This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
		public void Configure(IApplicationBuilder app, IWebHostEnvironment env)
		{
			if (env.IsDevelopment())
			{
				app.UseBrowserLink();
				app.UseDeveloperExceptionPage();
			}
			else
			{
				app.UseExceptionHandler("/Error");
				// The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
				app.UseHsts();
			}

			app.UseHttpsRedirection();
			app.UseWebOptimizer();

			// PR with fix for .webmanifest was merged Mar 3, 2020. https://github.com/dotnet/aspnetcore/pull/19661
			// Remove the custom FileExtensionContentTypeProvider after upgrading to newer .NET version
			var provider = new FileExtensionContentTypeProvider();
			provider.Mappings[".webmanifest"] = "application/manifest+json";
			provider.Mappings[".vsix"] = "application/octed-stream";

			app.UseStaticFiles(new StaticFileOptions()
			{
				ContentTypeProvider = provider
			});

			app.UseRouting();
			app.UseMvcWithDefaultRoute();

			app.UseAuthorization();

			app.UseEndpoints(endpoints =>
			{
				endpoints.MapRazorPages();
			});
		}
	}
}
