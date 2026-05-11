using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
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

			// Response compression replaces the IIS <httpCompression> section so it
			// works on both Kestrel (Linux) and IIS.
			services.AddResponseCompression(options =>
			{
				options.EnableForHttps = true;
				options.Providers.Add<BrotliCompressionProvider>();
				options.Providers.Add<GzipCompressionProvider>();
				options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(new[]
				{
					"image/svg+xml",
					"application/manifest+json",
					"application/atom+xml",
					"application/xaml+xml",
				});
			});
			services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
			services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

			// Match the IIS requestLimits/maxAllowedContentLength from web.config (~500 MB).
			services.Configure<FormOptions>(options =>
			{
				options.MultipartBodyLengthLimit = 500_000_000;
			});

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
					// Replaces the Arr-Disable-Session-Affinity custom header from web.config.
					context.Response.Headers["Arr-Disable-Session-Affinity"] = "true";
					return next();
				});

			app.UseResponseCompression();

			// Apply the IIS-style URL rewrite rules from web.config. AddIISUrlRewrite
			// parses the rules in managed code, so this works on Kestrel (Linux) too.
			// When running under IIS in-process, IIS itself will already have applied
			// these rules, so we skip them to avoid double-processing.
			if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("APP_POOL_ID")))
			{
				using (StreamReader webConfig = File.OpenText("web.config"))
				{
					app.UseRewriter(new RewriteOptions().AddIISUrlRewrite(webConfig));
				}
			}

			// Register MIME types previously defined in <staticContent> in web.config
			// so Kestrel serves .webmanifest and .svg with the correct content type.
			FileExtensionContentTypeProvider contentTypeProvider = new();
			contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json; charset=utf-8";
			contentTypeProvider.Mappings[".svg"] = "image/svg+xml; charset=utf-8";
			app.UseStaticFiles(new StaticFileOptions
			{
				ContentTypeProvider = contentTypeProvider,
			});

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
