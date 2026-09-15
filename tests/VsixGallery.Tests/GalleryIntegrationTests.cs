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
		Assert.Equal("noindex, nofollow", Assert.Single(first.Headers.GetValues("X-Robots-Tag")));
		using JsonDocument payload = JsonDocument.Parse(json);
		JsonElement summary = Assert.Single(payload.RootElement.EnumerateArray());
		Assert.Equal("Public.Extension", summary.GetProperty("id").GetString());
		Assert.Equal("Public Extension", summary.GetProperty("name").GetString());
		Assert.True(summary.TryGetProperty("detailsLink", out _));
		Assert.True(summary.TryGetProperty("downloadLink", out _));
		Assert.False(summary.TryGetProperty("validation", out _));
		Assert.False(summary.TryGetProperty("license", out _));
		Assert.DoesNotContain("Hidden.Extension", json);
		EntityTagHeaderValue etag = Assert.IsType<EntityTagHeaderValue>(first.Headers.ETag);

		using HttpRequestMessage conditional = new(HttpMethod.Get, "/api");
		conditional.Headers.IfNoneMatch.Add(etag);
		using HttpResponseMessage second = await client.SendAsync(conditional);

		Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);

		using HttpResponseMessage details = await client.GetAsync("/api/Public.Extension");
		using JsonDocument detailPayload = JsonDocument.Parse(await details.Content.ReadAsStringAsync());
		Assert.True(detailPayload.RootElement.TryGetProperty("validation", out _));
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
		using HttpResponseMessage author = await client.GetAsync("/author/Example%20Publisher");
		using HttpResponseMessage extension = await client.GetAsync("/extension/Public.Extension");
		using HttpResponseMessage guide = await client.GetAsync("/devguide/");
		using HttpResponseMessage missing = await client.GetAsync("/missing-page");

		Assert.Equal(HttpStatusCode.OK, home.StatusCode);
		string homeHtml = await home.Content.ReadAsStringAsync();
		Assert.Contains("Visual Studio extensions beyond the Marketplace", homeHtml);
		string csp = Assert.Single(home.Headers.GetValues("Content-Security-Policy"));
		Assert.Contains("script-src 'self'", csp);
		Assert.Contains("style-src 'self'", csp);
		Assert.Contains("https://*.clarity.ms", csp);
		Assert.Contains("https://c.bing.com", csp);
		Assert.DoesNotContain("'unsafe-inline'", csp);
		Assert.DoesNotContain("require-trusted-types-for", csp);
		Assert.Equal("nosniff", Assert.Single(home.Headers.GetValues("X-Content-Type-Options")));
		Assert.Contains(
			"https://www.clarity.ms/tag/yigw7yp0j4",
			homeHtml,
			StringComparison.Ordinal);
		Assert.Contains("\"@type\":\"ItemList\"", homeHtml);

		string feedXml = await feed.Content.ReadAsStringAsync();
		Assert.Contains("https://www.vsixgallery.com/extension/Public.Extension", feedXml);
		Assert.DoesNotContain("Hidden.Extension", feedXml);
		Assert.Equal("noindex, nofollow", Assert.Single(feed.Headers.GetValues("X-Robots-Tag")));

		string sitemapXml = await sitemap.Content.ReadAsStringAsync();
		Assert.Contains("https://www.vsixgallery.com/extension/Public.Extension", sitemapXml);
		Assert.Contains("https://www.vsixgallery.com/author/Example%20Publisher", sitemapXml);
		Assert.DoesNotContain("Hidden.Extension", sitemapXml);

		string authorHtml = await author.Content.ReadAsStringAsync();
		Assert.Contains("<title>Extensions by Example Publisher | Open VSIX Gallery</title>", authorHtml);
		Assert.DoesNotContain("Hidden Extension", authorHtml);
		Assert.Contains("rel=canonical", authorHtml);
		Assert.Contains("https://www.vsixgallery.com/author/Example%20Publisher", authorHtml);
		Assert.Contains("\"@type\":\"CollectionPage\"", authorHtml);
		Assert.Contains("\"@type\":\"BreadcrumbList\"", authorHtml);

		string extensionHtml = await extension.Content.ReadAsStringAsync();
		Assert.Contains("<title>Public Extension - Visual Studio extension | Open VSIX Gallery</title>", extensionHtml);
		Assert.Contains("rel=canonical", extensionHtml);
		Assert.Contains("https://www.vsixgallery.com/extension/Public.Extension", extensionHtml);
		Assert.Contains("\"@type\":\"SoftwareApplication\"", extensionHtml);
		Assert.Contains("\"@type\":\"BreadcrumbList\"", extensionHtml);

		string guideHtml = await guide.Content.ReadAsStringAsync();
		Assert.Contains("rel=canonical", guideHtml);
		Assert.Contains("https://www.vsixgallery.com/devguide", guideHtml);
		Assert.Contains("Publish a Visual Studio extension", guideHtml);

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
		string feedBefore = await client.GetStringAsync("/feed");
		string sitemapBefore = await client.GetStringAsync("/sitemap.xml");
		string authorBefore = await client.GetStringAsync("/author/Example%20Publisher");
		Assert.DoesNotContain("Uploaded Extension", before);
		Assert.DoesNotContain("Uploaded Extension", feedBefore);
		Assert.DoesNotContain("Uploaded.Extension", sitemapBefore);
		Assert.DoesNotContain("Uploaded Extension", authorBefore);

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
		string feedAfter = await client.GetStringAsync("/feed");
		string sitemapAfter = await client.GetStringAsync("/sitemap.xml");
		string authorAfter = await client.GetStringAsync("/author/Example%20Publisher");
		Assert.Contains("Uploaded Extension", after);
		Assert.Contains("Uploaded Extension", feedAfter);
		Assert.Contains("Uploaded.Extension", sitemapAfter);
		Assert.Contains("Uploaded Extension", authorAfter);

		using HttpResponseMessage publicDetails = await client.GetAsync("/api/Uploaded.Extension");
		string publicJson = await publicDetails.Content.ReadAsStringAsync();
		Assert.DoesNotContain("manageUrl", publicJson, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("token=", publicJson, StringComparison.OrdinalIgnoreCase);
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
