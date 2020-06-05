
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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

			services.AddOutputCaching();
			services.AddWebOptimizer(pipeline =>
				pipeline.CompileScssFiles()
			);

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
						options.MinificationSettings.WhitespaceMinificationMode = WhitespaceMinificationMode.Safe;
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

			// PR with fix for .webmanifest was merged Mar 3, 2020. https://github.com/dotnet/aspnetcore/pull/19661
			// Remove the custom FileExtensionContentTypeProvider after upgrading to newer .NET version
			FileExtensionContentTypeProvider provider = new FileExtensionContentTypeProvider();
			provider.Mappings[".webmanifest"] = "application/manifest+json";
			provider.Mappings[".vsix"] = "application/octed-stream";
			app.UseStaticFiles(new StaticFileOptions()
			{
				ContentTypeProvider = provider
			});

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
