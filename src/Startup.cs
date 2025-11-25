using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using System;
using System.IO;
using WebMarkupMin.AspNetCoreLatest;
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
			IMvcBuilder mvcBuilder = services.AddRazorPages();
#if DEBUG
			// The runtime compilation package is only installed for the Debug configuration.
			mvcBuilder.AddRazorRuntimeCompilation();
#endif

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

			services.Configure<ExtensionsOptions>(Configuration.GetSection("Extensions"));
			services.Configure<DisplayOptions>(Configuration.GetSection("Display"));
			services.Configure<UploadOptions>(Configuration.GetSection("Upload"));

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
			PackageHelper packageHelper = app.ApplicationServices.GetRequiredService<PackageHelper>();

			// If extensions are being stored in a custom path, then we need to create a file provider
			// that will act as though that custom path is under the "wwwroot/extensions" directory.
			if (packageHelper.IsCustomExtensionPath)
			{
				env.WebRootFileProvider = new CompositeFileProvider(
					new ExtensionsFileProvider(packageHelper.FileProvider),
					env.WebRootFileProvider
				);
			}

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

			// When running outside of IIS, we need to manually apply the rewrite
			// rules that convert the extension file names to "extension.vsix".
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_POOL_ID")))
			{
				using (StreamReader webConfig = File.OpenText("web.config"))
				{
					app.UseRewriter(new RewriteOptions().AddIISUrlRewrite(webConfig));
				}
			}

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
