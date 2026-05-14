using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.Rewrite;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.FileProviders;

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

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
	// CssHelper and JsHelper already minify inline CSS/JS and compute CSP hashes before
	// WebMarkupMin runs. Re-minifying here would produce different content, causing a
	// mismatch between the CSP hash and the bytes the browser actually receives.
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

app.UseStatusCodePagesWithReExecute("/NotFound");

app.UseHttpsRedirection();

// Pre-warm CSS/JS helpers and register their hashes globally.
// This ensures the CSP header always contains the correct hashes even when
// the response is served from a cache and Razor rendering is skipped.
{
	string cssContent = CssHelper.GetMinified(app.Environment, "css/site.css");
	SecurityHeadersMiddleware.RegisterGlobalStyleHash(
		Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(cssContent))));

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

app.MapRazorPages().WithStaticAssets();
app.MapControllers();
app.MapStaticAssets();

app.Run();
