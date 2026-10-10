using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using VsixGallery.Controllers;

using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text;
using System.Security.Cryptography;

namespace VsixGallery.Tests;

public class GalleryIntegrationTests
{
	[Fact]
	public void OutputCache_HasExplicitMemoryAndBodyLimits()
	{
		using GalleryApplication factory = new();
		OutputCacheOptions options = factory.Services.GetRequiredService<IOptions<OutputCacheOptions>>().Value;
		Assert.Equal(32 * 1024 * 1024, options.SizeLimit);
		Assert.Equal(8 * 1024 * 1024, options.MaximumBodySize);
	}

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
		using HttpResponseMessage feedGuide = await client.GetAsync("/feedguide/");
		using HttpResponseMessage guidelines = await client.GetAsync("/guidelines");
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
		Assert.Contains("https://www.vsixgallery.com/guidelines", sitemapXml);
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
		Assert.DoesNotContain("gist.github.com", extensionHtml);

		string guideHtml = await guide.Content.ReadAsStringAsync();
		Assert.Contains("rel=canonical", guideHtml);
		Assert.Contains("https://www.vsixgallery.com/devguide", guideHtml);
		Assert.Contains("Publish a Visual Studio extension", guideHtml);
		Assert.Contains("guide-page", guideHtml);

		string feedGuideHtml = await feedGuide.Content.ReadAsStringAsync();
		Assert.Contains("Visual Studio Enterprise feature", feedGuideHtml);
		Assert.Contains("guide-page", feedGuideHtml);

		string guidelinesHtml = await guidelines.Content.ReadAsStringAsync();
		Assert.Equal(HttpStatusCode.OK, guidelines.StatusCode);
		Assert.Contains("https://www.vsixgallery.com/guidelines", guidelinesHtml);
		Assert.Contains("icon.invalid-dimensions", guidelinesHtml);
		Assert.Contains("Larger images are supported and scaled down", guidelinesHtml);

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

		string extensionHtml = await client.GetStringAsync("/extension/Uploaded.Extension");
		Assert.Contains("href=/guidelines", extensionHtml);
		Assert.DoesNotContain("gist.github.com", extensionHtml);
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

	[Theory]
	[InlineData("multipart/form-data")]
	[InlineData("text/plain")]
	public async Task Upload_MalformedContentType_ReturnsBadRequest(string contentType)
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();
		using ByteArrayContent content = new("invalid"u8.ToArray());
		content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);

		using HttpResponseMessage response = await client.PostAsync("/api/upload", content);

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
	}

	[Theory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task Upload_TruncatedFileOrTrailingSection_DoesNotPublish(bool trailingSection)
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();
		using MemoryStream body = new();
		body.Write(Encoding.UTF8.GetBytes("--boundary\r\nContent-Disposition: form-data; name=\"file\"; filename=\"upload.vsix\"\r\n\r\n"));
		body.Write(TestVsix.Create("Truncated.Extension", "1.0"));
		if (trailingSection)
		{
			body.Write(Encoding.UTF8.GetBytes("\r\n--boundary\r\nContent-Disposition: form-data; name=\"note\"\r\n\r\ntruncated"));
		}
		using ByteArrayContent content = new(body.ToArray());
		content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/form-data; boundary=boundary");

		using HttpResponseMessage response = await client.PostAsync("/api/upload", content);
		using HttpResponseMessage details = await client.GetAsync("/api/Truncated.Extension");

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
	}

	[Fact]
	public async Task Upload_StreamsFirstFileAndPreservesFormFallbackAndManageToken()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();
		byte[] vsix = TestVsix.Create("Streamed.Extension", "1.0");
		using MultipartFormDataContent form = new();
		form.Add(new StringContent("issues/from-form"), "issuetracker");
		form.Add(new ByteArrayContent(vsix), "file", "upload.vsix");
		form.Add(new StringContent("other/README.md"), "readmeUrl");
		form.Add(new ByteArrayContent("ignored second file"u8.ToArray()), "file", "ignored.vsix");
		using HttpRequestMessage request = new(HttpMethod.Post, "/api/upload?readmeUrl=main/README.md");
		request.Headers.Add("X-Manage-Token", "publisher-token");
		request.Content = form;

		using HttpResponseMessage response = await client.SendAsync(request);
		using JsonDocument payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

		Assert.Equal(HttpStatusCode.Created, response.StatusCode);
		Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(vsix)), payload.RootElement.GetProperty("sha256").GetString());
		Assert.Equal("https://github.com/example/project/issues/from-form", payload.RootElement.GetProperty("issueTracker").GetString());
		Assert.Equal("https://raw.githubusercontent.com/example/project/refs/heads/main/README.md", payload.RootElement.GetProperty("readmeUrl").GetString());
		Assert.DoesNotContain("token=", payload.RootElement.GetProperty("manageUrl").GetString());

		using HttpRequestMessage delete = new(HttpMethod.Delete, "/api/extension/Streamed.Extension");
		delete.Headers.Add("X-Manage-Token", "publisher-token");
		using HttpResponseMessage deleted = await client.SendAsync(delete);
		Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
	}

	[Fact]
	public async Task Upload_UnauthorizedMalformedRequest_IsRejectedBeforeReadingBody()
	{
		using GalleryApplication factory = new(uploadSecret: "upload-secret");
		ApiController controller = new(
			factory.Services.GetRequiredService<PackageHelper>(),
			factory.Services.GetRequiredService<IOptions<UploadOptions>>(),
			factory.Services.GetRequiredService<PublicUrl>(),
			factory.Services.GetRequiredService<ILogger<ApiController>>())
		{
			ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
		};
		controller.Request.ContentType = "text/plain";

		IActionResult response = await controller.Upload();

		Assert.IsType<UnauthorizedResult>(response);
	}

	[Fact]
	public async Task Upload_EnforcesConfiguredSectionLimitBeforePublishing()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();
		factory.Services.GetRequiredService<IOptions<FormOptions>>().Value.ValueCountLimit = 1;
		using MultipartFormDataContent form = new();
		form.Add(new ByteArrayContent(TestVsix.Create("Limited.Extension", "1.0")), "file", "upload.vsix");
		form.Add(new StringContent("extra field"), "note");

		using HttpResponseMessage response = await client.PostAsync("/api/upload", form);
		using HttpResponseMessage details = await client.GetAsync("/api/Limited.Extension");

		Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
		Assert.Equal(HttpStatusCode.NotFound, details.StatusCode);
	}

	[Fact]
	public async Task Upload_PropagatesRequestCancellation()
	{
		using GalleryApplication factory = new();
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();
		using MemoryStream body = new("--boundary\r\n"u8.ToArray());
		ApiController controller = new(
			factory.Services.GetRequiredService<PackageHelper>(),
			factory.Services.GetRequiredService<IOptions<UploadOptions>>(),
			factory.Services.GetRequiredService<PublicUrl>(),
			factory.Services.GetRequiredService<ILogger<ApiController>>())
		{
			ControllerContext = new ControllerContext
			{
				HttpContext = new DefaultHttpContext
				{
					RequestServices = factory.Services,
					RequestAborted = cancellation.Token,
				},
			},
		};
		controller.Request.ContentType = "multipart/form-data; boundary=boundary";
		controller.Request.Body = body;

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.Upload());
	}

	[Fact]
	public async Task PublicCache_IgnoresTrackingQueries_AndCachesBadges()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();
		string[] paths = ["/extension/Public.Extension", "/author/Example%20Publisher", "/api", "/api/Public.Extension",
			"/feed", "/feed/extension/Public.Extension", "/feed/author/Example%20Publisher", "/sitemap.xml",
			"/badge/Public.Extension.svg", "/badge/Public.Extension.png"];

		foreach (string path in paths)
		{
			byte[] first = await client.GetByteArrayAsync(path);
			byte[] second = await client.GetByteArrayAsync(path + "?utm_source=one");
			byte[] third = await client.GetByteArrayAsync(path + "?utm_source=two");
			Assert.Equal(first, second);
			Assert.Equal(first, third);
			Assert.Equal(2, factory.CacheHits.For(path));
		}
	}

	[Fact]
	public async Task Api_QueryBoundId_RemainsDistinctFromAggregateResponse()
	{
		using GalleryApplication factory = new();
		using HttpClient client = factory.CreateHttpsClient();
		using JsonDocument all = JsonDocument.Parse(await client.GetStringAsync("/api"));
		using JsonDocument details = JsonDocument.Parse(await client.GetStringAsync("/api?id=Public.Extension"));
		using JsonDocument cached = JsonDocument.Parse(await client.GetStringAsync("/api?id=Public.Extension&utm_source=one"));
		Assert.Equal(JsonValueKind.Array, all.RootElement.ValueKind);
		Assert.Equal("Public.Extension", details.RootElement.GetProperty("id").GetString());
		Assert.Equal(details.RootElement.GetRawText(), cached.RootElement.GetRawText());
	}

	[Fact]
	public async Task Mutations_InvalidateAffectedScopes_AndPreserveUnrelatedCacheEntries()
	{
		using GalleryApplication factory = new(includeOtherPublisher: true);
		using HttpClient client = factory.CreateHttpsClient();
		string[] affected = ["/extension/Public.Extension", "/api/Public.Extension", "/feed/extension/Public.Extension",
			"/badge/Public.Extension.svg", "/badge/Public.Extension.png", "/author/Example%20Publisher",
			"/feed/author/Example%20Publisher", "/author/New%20Publisher", "/feed/author/New%20Publisher", "/", "/api", "/feed", "/sitemap.xml"];
		string[] unaffected = ["/extension/Other.Extension", "/api/Other.Extension", "/feed/extension/Other.Extension",
			"/badge/Other.Extension.png", "/author/Other%20Publisher", "/feed/author/Other%20Publisher"];
		foreach (string path in affected.Concat(unaffected))
		{
			await client.GetByteArrayAsync(path);
			await client.GetByteArrayAsync(path);
			Assert.Equal(1, factory.CacheHits.For(path));
		}

		using MultipartFormDataContent form = new();
		form.Add(new ByteArrayContent(TestVsix.Create("Public.Extension", "2.0", "Updated Public Extension",
			publisher: "New Publisher", moreInfo: string.Empty)), "file", "updated.vsix");
		using HttpResponseMessage upload = await client.PostAsync("/api/upload", form);
		Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
		foreach (string path in affected)
		{
			await client.GetByteArrayAsync(path);
			Assert.Equal(1, factory.CacheHits.For(path));
		}
		foreach (string path in unaffected)
		{
			await client.GetByteArrayAsync(path);
			Assert.Equal(2, factory.CacheHits.For(path));
		}
		Assert.Contains("2.0", await client.GetStringAsync("/badge/Public.Extension.svg"));
		Assert.DoesNotContain("Public.Extension", await client.GetStringAsync("/feed/author/Example%20Publisher"));
		Assert.Contains("Public.Extension", await client.GetStringAsync("/feed/author/New%20Publisher"));

		PackageHelper helper = factory.Services.GetRequiredService<PackageHelper>();
		helper.SoftDelete("Public.Extension");
		using HttpResponseMessage deletedBadge = await client.GetAsync("/badge/Public.Extension.png");
		Assert.Equal(HttpStatusCode.NotFound, deletedBadge.StatusCode);
		Assert.DoesNotContain("Public.Extension", await client.GetStringAsync("/feed/author/New%20Publisher"));
		Assert.DoesNotContain("Public.Extension", await client.GetStringAsync("/api"));
		Assert.True(helper.Restore(Assert.Single(helper.ListTrash()).TrashFolder!));
		Assert.Contains("2.0", await client.GetStringAsync("/badge/Public.Extension.svg"));
		Assert.Contains("Public.Extension", await client.GetStringAsync("/feed/author/New%20Publisher"));
		Assert.Contains("Public.Extension", await client.GetStringAsync("/api"));
	}

	private sealed class CacheHitPolicy : IOutputCachePolicy
	{
		private readonly ConcurrentDictionary<string, int> _hits = new(StringComparer.OrdinalIgnoreCase);
		public int For(string path) => _hits.GetValueOrDefault(Uri.UnescapeDataString(path));
		public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
		public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
		public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken)
		{
			_hits.AddOrUpdate(context.HttpContext.Request.Path.Value!, 1, (_, count) => count + 1);
			return ValueTask.CompletedTask;
		}
	}

	private sealed class GalleryApplication : WebApplicationFactory<Program>
	{
		public CacheHitPolicy CacheHits { get; } = new();
		private readonly string? _uploadSecret;

		public GalleryApplication(bool includeOtherPublisher = false, string? uploadSecret = null)
		{
			_uploadSecret = uploadSecret;
			Root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Root);
			WritePackage("Public.Extension", "Public Extension", tags: null);
			WritePackage("Hidden.Extension", "Hidden Extension", tags: "unlisted");
			if (includeOtherPublisher)
			{
				WritePackage("Other.Extension", "Other Extension", tags: null, author: "Other Publisher");
			}
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
			if (_uploadSecret is not null) builder.UseSetting("Upload:SecretKey", _uploadSecret);
			builder.ConfigureServices(services => services.AddOutputCache(options => options.AddBasePolicy(CacheHits)));
		}

		protected override void Dispose(bool disposing)
		{
			base.Dispose(disposing);
			if (Directory.Exists(Root))
			{
				Directory.Delete(Root, recursive: true);
			}
		}

		private void WritePackage(string id, string name, string? tags, string author = "Example Publisher")
		{
			string folder = Path.Combine(Root, id);
			Directory.CreateDirectory(folder);
			File.WriteAllText(Path.Combine(folder, "extension.vsix"), "test");
			Package package = new()
			{
				ID = id,
				Name = name,
				Description = "A sufficiently detailed extension description for integration tests.",
				Author = author,
				Version = "1.0",
				Tags = tags,
				DatePublished = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Utc),
			};
			string json = JsonSerializer.Serialize(package, PackageJsonContext.Default.Package);
			File.WriteAllText(Path.Combine(folder, "extension.json"), json);
		}
	}
}
