using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

namespace VsixGallery.Tests;

public class GalleryIntegrationTests
{
	[Fact]
	public async Task HealthEndpoints_ReturnJsonWithoutCaching()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();

		using HttpResponseMessage live = await client.GetAsync("/health/live");
		using HttpResponseMessage ready = await client.GetAsync("/health/ready");

		Assert.Equal(HttpStatusCode.OK, live.StatusCode);
		Assert.Equal(HttpStatusCode.OK, ready.StatusCode);
		Assert.Contains("no-store", ready.Headers.CacheControl!.ToString());
		using JsonDocument payload = JsonDocument.Parse(await ready.Content.ReadAsStringAsync());
		Assert.Equal("Healthy", payload.RootElement.GetProperty("status").GetString());
	}

	[Fact]
	public async Task Api_ExcludesUnlistedPackagesAndSupportsConditionalRequests()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();

		using HttpResponseMessage first = await client.GetAsync("/api");
		string json = await first.Content.ReadAsStringAsync();

		Assert.Equal(HttpStatusCode.OK, first.StatusCode);
		Assert.Contains("Public.Extension", json);
		Assert.DoesNotContain("Hidden.Extension", json);
		EntityTagHeaderValue etag = Assert.IsType<EntityTagHeaderValue>(first.Headers.ETag);

		using HttpRequestMessage conditional = new(HttpMethod.Get, "/api");
		conditional.Headers.IfNoneMatch.Add(etag);
		using HttpResponseMessage second = await client.SendAsync(conditional);

		Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
	}

	[Fact]
	public async Task FeedSitemapAndPages_ExposeExpectedPublicMetadataAndSecurityHeaders()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();

		using HttpResponseMessage home = await client.GetAsync("/");
		using HttpResponseMessage feed = await client.GetAsync("/feed");
		using HttpResponseMessage sitemap = await client.GetAsync("/sitemap.xml");
		using HttpResponseMessage search = await client.GetAsync("/search/?q=public");
		using HttpResponseMessage missing = await client.GetAsync("/missing-page");

		Assert.Equal(HttpStatusCode.OK, home.StatusCode);
		string homeHtml = await home.Content.ReadAsStringAsync();
		Assert.Contains("Visual Studio extensions beyond the Marketplace", homeHtml);
		string csp = Assert.Single(home.Headers.GetValues("Content-Security-Policy"));
		Assert.Contains("script-src 'self'", csp);
		Assert.Contains("style-src 'self'", csp);
		Assert.DoesNotContain("'unsafe-inline'", csp);
		Assert.Equal("nosniff", Assert.Single(home.Headers.GetValues("X-Content-Type-Options")));

		string feedXml = await feed.Content.ReadAsStringAsync();
		Assert.Contains("https://www.vsixgallery.com/extension/Public.Extension", feedXml);
		Assert.DoesNotContain("Hidden.Extension", feedXml);

		string sitemapXml = await sitemap.Content.ReadAsStringAsync();
		Assert.Contains("https://www.vsixgallery.com/extension/Public.Extension", sitemapXml);
		Assert.DoesNotContain("Hidden.Extension", sitemapXml);

		Assert.Contains("noindex", await search.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
		Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
	}

	[Fact]
	public async Task Search_NoMatches_ShowsHelpfulEmptyState()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();

		string html = await client.GetStringAsync("/search/?q=does-not-exist");

		Assert.Contains("No extensions found", html);
		Assert.Contains("Browse all extensions", html);
	}

	[Fact]
	public async Task Upload_InvalidatesCachedHomepage()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();

		string before = await client.GetStringAsync("/");
		Assert.DoesNotContain("Uploaded Extension", before);

		using MultipartFormDataContent form = new();
		ByteArrayContent vsix = new(TestVsix.Create(
			"Uploaded.Extension",
			"1.0",
			"Uploaded Extension"));
		form.Add(vsix, "file", "uploaded.vsix");
		using HttpResponseMessage upload = await client.PostAsync("/api/upload", form);

		Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
		using JsonDocument uploadPayload = JsonDocument.Parse(await upload.Content.ReadAsStringAsync());
		Assert.Equal("Uploaded.Extension", uploadPayload.RootElement.GetProperty("id").GetString());
		JsonElement warning = Assert.Single(uploadPayload.RootElement.GetProperty("validation").EnumerateArray());
		Assert.Equal("warning", warning.GetProperty("severity").GetString());
		Assert.Equal("icon.missing", warning.GetProperty("code").GetString());
		string after = await client.GetStringAsync("/");
		Assert.Contains("Uploaded Extension", after);
	}

	[Fact]
	public async Task Upload_InvalidVsix_ReturnsStructuredValidationError()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();
		using MultipartFormDataContent form = new();
		form.Add(new ByteArrayContent("not a VSIX"u8.ToArray()), "file", "invalid.vsix");

		using HttpResponseMessage response = await client.PostAsync("/api/upload", form);
		using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		JsonElement finding = Assert.Single(
			payload.RootElement.GetProperty("validation").EnumerateArray());
		Assert.Equal("error", finding.GetProperty("severity").GetString());
		Assert.Equal("package.invalid", finding.GetProperty("code").GetString());
	}

	private sealed class GalleryApplication : WebApplicationFactory<Program>
	{
		public GalleryApplication()
		{
			Root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Root);
			WritePackage("Public.Extension", "Public Extension", tags: null);
			WritePackage("Hidden.Extension", "Hidden Extension", tags: "unlisted");
		}

		private string Root { get; }

		public HttpClient CreateHttpsClient() =>
			CreateClient(new WebApplicationFactoryClientOptions
			{
				BaseAddress = new Uri("https://www.vsixgallery.com"),
			});

		protected override void ConfigureWebHost(IWebHostBuilder builder)
		{
			builder.UseEnvironment("Production");
			builder.UseSetting("Extensions:Directory", Root);
			builder.UseSetting("Extensions:MinimumFreeSpaceBytes", "0");
			builder.UseSetting("Extensions:RemoveOldExtensions", "false");
			builder.UseSetting("Extensions:ValidateLicenses", "false");
			builder.UseSetting("Display:SiteUrl", "https://www.vsixgallery.com");
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}

		private void WritePackage(string id, string name, string? tags)
		{
			string folder = Path.Combine(Root, id);
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "extension.vsix"), "test");
			Package package = new()
			{
				ID = id,
				Name = name,
				Description = "A sufficiently detailed extension description for integration tests.",
				Author = "Example Publisher",
				Version = "1.0",
				Tags = tags,
				DatePublished = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc),
			};
			string json = JsonSerializer.Serialize(package, PackageJsonContext.Default.Package);
			File.WriteAllText(Path.Combine(folder, "extension.json"), json);
		}
	}
}
