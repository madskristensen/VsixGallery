using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

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

builder.Services.AddControllers();
builder.Services.AddHsts(options =>
{
	options.MaxAge = TimeSpan.FromDays(730);
	options.IncludeSubDomains = true;
	options.Preload = true;
});

builder.Services.AddOutputCaching();
builder.Services
	.AddHealthChecks()
	.AddCheck<ExtensionStorageHealthCheck>("extension_storage", tags: ["ready"]);
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<ReadmeService>(client =>
{
	client.BaseAddress = new Uri("https://markdownservice.azurewebsites.net/");
	client.Timeout = TimeSpan.FromSeconds(15);
});

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

// PackageHelper caches packages, so we need to register it as a singleton.
builder.Services.AddSingleton<PackageHelper>();
builder.Services.AddSingleton<SocialCardRenderer>();
builder.Services.AddSingleton<PublicUrl>();
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.Configure<ExtensionsOptions>(builder.Configuration.GetSection("Extensions"));
builder.Services.Configure<DisplayOptions>(builder.Configuration.GetSection("Display"));
builder.Services.Configure<UploadOptions>(builder.Configuration.GetSection("Upload"));
builder.Services.Configure<AdminOptions>(builder.Configuration.GetSection("Admin"));
builder.Services.AddSingleton<AdminAuth>();
builder.Services.AddHostedService<TrashCleanupService>();

// HTML minification (https://github.com/Taritsyn/WebMarkupMin)
builder.Services
.AddWebMarkupMin(
options =>
{
	options.AllowMinificationInDevelopmentEnvironment = false;
	options.DisablePoweredByHttpHeaders = true;
})
.AddHtmlMinification(
options =>
{
	options.MinificationSettings.RemoveOptionalEndTags = false;
	options.MinificationSettings.WhitespaceMinificationMode = WhitespaceMinificationMode.Aggressive;
	// JsHelper already minifies inline JS and computes its CSP hash before WebMarkupMin
	// runs. Re-minifying here would produce different content and invalidate that hash.
	options.MinificationSettings.MinifyEmbeddedCssCode = false;
	options.MinificationSettings.MinifyEmbeddedJsCode = false;
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

if (!app.Environment.IsDevelopment())
{
	app.UseExceptionHandler("/Error");
	app.UseHsts();
}

app.UseWhen(
	static context => !context.Request.Path.StartsWithSegments("/health"),
	static branch => branch.UseStatusCodePagesWithReExecute("/NotFound"));

app.UseWhen(
	static context => !context.Request.Path.StartsWithSegments("/health"),
	static branch => branch.UseHttpsRedirection());

// Pre-warm the JS helper and register its hash globally.
// This ensures the CSP header always contains the correct hash even when
// the response is served from a cache and Razor rendering is skipped.
{
string jsContent = JsHelper.GetMinified(app.Environment, "js/site.js");
SecurityHeadersMiddleware.RegisterGlobalScriptHash(
	Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(jsContent))));
}

app.UseSecurityHeaders();

app.UseResponseCompression();

RewriteOptions rewriteOptions = new RewriteOptions()
	.AddRewrite(@"^(.+)/(.+\.vsix)$", "$1/extension.vsix", skipRemainingRules: true);

if (!app.Environment.IsDevelopment())
{
	rewriteOptions.AddRedirectToWwwPermanent();
	app.UseOutputCaching();
}

app.UseRewriter(rewriteOptions);

FileExtensionContentTypeProvider contentTypeProvider = new();
contentTypeProvider.Mappings[".vsix"] = "application/vsix";
contentTypeProvider.Mappings[".webmanifest"] = "application/manifest+json; charset=utf-8";

app.UseStaticFiles(new StaticFileOptions
{
	ContentTypeProvider = contentTypeProvider,
	OnPrepareResponse = static ctx =>
	{
		// All assets with a ?v= content-hash query (fingerprinted by asp-append-version)
		// are immutable: the URL changes whenever the file changes, so they can be
		// cached indefinitely. This covers both project assets (CSS/JS/icons) and
		// extension icon images. Un-fingerprinted paths like .vsix downloads are
		// intentionally left without a Cache-Control header so the browser revalidates.
		if (ctx.Context.Request.Query.ContainsKey("v"))
		{
			ctx.Context.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
		}
	}
});

app.UseWebMarkupMin();

app.UseAuthorization();

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
	Predicate = static _ => false,
	ResponseWriter = WriteHealthResponseAsync,
});
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
	Predicate = static registration => registration.Tags.Contains("ready"),
	ResponseWriter = WriteHealthResponseAsync,
});
app.MapRazorPages().WithStaticAssets();
app.MapControllers();
app.MapStaticAssets();

app.Run();

static Task WriteHealthResponseAsync(HttpContext context, Microsoft.Extensions.Diagnostics.HealthChecks.HealthReport report)
{
	context.Response.ContentType = "application/json; charset=utf-8";
	context.Response.Headers.CacheControl = "no-store";

	var response = new
	{
		status = report.Status.ToString(),
		checks = report.Entries.Select(entry => new
		{
			name = entry.Key,
			status = entry.Value.Status.ToString(),
			description = entry.Value.Description,
			data = entry.Value.Data,
		}),
	};

	return context.Response.WriteAsync(JsonSerializer.Serialize(response));
}
