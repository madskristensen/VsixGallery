
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using System;

using WebMarkupMin.AspNetCore2;
using WebMarkupMin.Core;

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
			services.AddHsts(options =>
			{
				options.MaxAge = TimeSpan.FromDays(126);
			});

			services.AddOutputCaching();
			services.AddWebOptimizer(pipeline =>
				pipeline.CompileScssFiles()
			);

			// PackgeHelper caches packages, so we need to register it as a singleton.
			services.AddSingleton<PackageHelper>();

			// HTML minification (https://github.com/Taritsyn/WebMarkupMin)
			services
				.AddWebMarkupMin(
					options =>
					{
						options.AllowMinificationInDevelopmentEnvironment = true;
						options.DisablePoweredByHttpHeaders = true;
					})
				.AddHtmlMinification(
					options =>
					{
						options.MinificationSettings.RemoveOptionalEndTags = false;
						options.MinificationSettings.WhitespaceMinificationMode = WhitespaceMinificationMode.Aggressive;
					});
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

			app.Use((context, next) =>
				{
					context.Response.Headers["X-Content-Type-Options"] = "nosniff";
					return next();
				});

			app.UseWebOptimizer();
			app.UseStaticFilesWithCache();

			if (!env.IsDevelopment())
			{
				app.UseOutputCaching();
			}

			app.UseWebMarkupMin();
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
