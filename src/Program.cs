using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

using System.IO.Compression;

using VsixGallery;

using WebMarkupMin.AspNetCoreLatest;
using WebMarkupMin.Core;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// ---- Kestrel ----
builder.WebHost.ConfigureKestrel(options =>
{
	// Don't advertise the server in response headers (was: removeServerHeader in web.config).
	options.AddServerHeader = false;

	// Match the IIS requestLimits/maxAllowedContentLength from web.config (~500 MB).
	options.Limits.MaxRequestBodySize = 500_000_000;
});

// ---- Services ----
IMvcBuilder mvcBuilder = builder.Services.AddRazorPages();
#if DEBUG
// The runtime compilation package is only installed for the Debug configuration.
mvcBuilder.AddRazorRuntimeCompilation();
#endif

builder.Services.AddMvc(options => options.EnableEndpointRouting = false);
builder.Services.AddHsts(options =>
{
	options.MaxAge = TimeSpan.FromDays(126);
});

builder.Services.AddOutputCaching();

// Response compression replaces the IIS <httpCompression> section so it
// works on both Kestrel (Linux) and IIS.
builder.Services.AddResponseCompression(static options =>
{
	options.EnableForHttps = true;
	options.Providers.Add<BrotliCompressionProvider>();
	options.Providers.Add<GzipCompressionProvider>();
	options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(
	[
		"image/svg+xml",
		"application/manifest+json",
		"application/atom+xml",
		"application/xaml+xml",
	]);
});

builder.Services.Configure<BrotliCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);
builder.Services.Configure<GzipCompressionProviderOptions>(o => o.Level = CompressionLevel.Fastest);

// Match the IIS requestLimits/maxAllowedContentLength from web.config (~500 MB).
builder.Services.Configure<FormOptions>(options =>
{
	options.MultipartBodyLengthLimit = 500_000_000;
});

// PackgeHelper caches packages, so we need to register it as a singleton.
builder.Services.AddSingleton<PackageHelper>();
builder.Services.AddSingleton<SocialCardRenderer>();

builder.Services.Configure<ExtensionsOptions>(builder.Configuration.GetSection("Extensions"));
builder.Services.Configure<DisplayOptions>(builder.Configuration.GetSection("Display"));
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection("Upload"));

// HTML minification (https://github.com/Taritsyn/WebMarkupMin)
builder.Services
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

WebApplication app = builder.Build();

// ---- Pipeline ----
PackageHelper packageHelper = app.Services.GetRequiredService<PackageHelper>();

// If extensions are being stored in a custom path, then we need to create a file provider
// that will act as though that custom path is under the "wwwroot/extensions" directory.
if (packageHelper.IsCustomExtensionPath)
{
	app.Environment.WebRootFileProvider = new CompositeFileProvider(
	new ExtensionsFileProvider(packageHelper.FileProvider),
	app.Environment.WebRootFileProvider
	);
}

if (app.Environment.IsDevelopment())
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
	using StreamReader webConfig = File.OpenText("web.config");
	app.UseRewriter(new RewriteOptions().AddIISUrlRewrite(webConfig));
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

if (!app.Environment.IsDevelopment())
{
	app.UseOutputCaching();
}

app.UseWebMarkupMin();
app.UseRouting();
app.UseMvcWithDefaultRoute();

app.UseAuthorization();

app.MapRazorPages();

app.Run();
