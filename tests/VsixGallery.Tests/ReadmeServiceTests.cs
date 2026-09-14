using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;

using System.Net;
using System.Text;

namespace VsixGallery.Tests;

public class ReadmeServiceTests
{
	[Fact]
	public async Task GetSanitizedHtmlAsync_RemovesActiveContentAndHardensLinks()
	{
		using StubHandler handler = new(_ => HtmlResponse(
			"""<h1>Hello</h1><script>alert(1)</script><img src="javascript:alert(1)"><a href="javascript:alert(2)" onclick="alert(3)" style="color:red">Bad</a><a href="/docs">Safe</a>"""));
		ReadmeService service = CreateService(handler);

		string? html = await service.GetSanitizedHtmlAsync(
			"https://raw.githubusercontent.com/example/project/refs/heads/main/README.md",
			CancellationToken.None);

		Assert.NotNull(html);
		Assert.Contains("<h1>Hello</h1>", html);
		Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("href=\"https://raw.githubusercontent.com/docs\"", html);
		Assert.Contains("rel=\"noopener noreferrer\"", html);
	}

	[Fact]
	public async Task GetSanitizedHtmlAsync_TriesMasterWhenMainDoesNotRender()
	{
		using StubHandler handler = new(request =>
			Uri.UnescapeDataString(request.RequestUri!.Query).Contains("/master/", StringComparison.Ordinal)
				? HtmlResponse("<p>Fallback</p>")
				: new HttpResponseMessage(HttpStatusCode.NotFound));
		ReadmeService service = CreateService(handler);

		string? html = await service.GetSanitizedHtmlAsync(
			"https://raw.githubusercontent.com/example/project/refs/heads/main/README.md",
			CancellationToken.None);

		Assert.Equal("<p>Fallback</p>", html);
		Assert.Equal(2, handler.RequestCount);
	}

	[Fact]
	public async Task GetSanitizedHtmlAsync_RejectsOversizedResponse()
	{
		using StubHandler handler = new(_ => HtmlResponse(new string('x', 2_000_001)));
		ReadmeService service = CreateService(handler);

		string? html = await service.GetSanitizedHtmlAsync(
			"https://raw.githubusercontent.com/example/project/refs/heads/main/README.md",
			CancellationToken.None);

		Assert.Null(html);
	}

	[Fact]
	public async Task GetSanitizedHtmlAsync_TreatsRendererTimeoutAsUnavailable()
	{
		using TimeoutHandler handler = new();
		ReadmeService service = CreateService(handler);

		string? html = await service.GetSanitizedHtmlAsync(
			"https://raw.githubusercontent.com/example/project/refs/heads/main/README.md",
			CancellationToken.None);

		Assert.Null(html);
	}

	[Fact]
	public async Task GetSanitizedHtmlAsync_CachesSuccessfulResponse()
	{
		using StubHandler handler = new(_ => HtmlResponse("<p>Cached</p>"));
		ReadmeService service = CreateService(handler);
		const string readmeUrl = "https://raw.githubusercontent.com/example/project/refs/heads/main/README.md";

		string? first = await service.GetSanitizedHtmlAsync(readmeUrl, CancellationToken.None);
		string? second = await service.GetSanitizedHtmlAsync(readmeUrl, CancellationToken.None);

		Assert.Equal("<p>Cached</p>", first);
		Assert.Equal(first, second);
		Assert.Equal(1, handler.RequestCount);
	}

	[Theory]
	[InlineData("http://example.com/README.md")]
	[InlineData("https://localhost/README.md")]
	[InlineData("https://127.0.0.1/README.md")]
	[InlineData("https://user:password@example.com/README.md")]
	[InlineData("https://example.com/README.md")]
	public async Task GetSanitizedHtmlAsync_RejectsUnsafeSource(string readmeUrl)
	{
		using StubHandler handler = new(_ => HtmlResponse("<p>Unexpected</p>"));
		ReadmeService service = CreateService(handler);

		string? html = await service.GetSanitizedHtmlAsync(readmeUrl, CancellationToken.None);

		Assert.Null(html);
		Assert.Equal(0, handler.RequestCount);
	}

	private static ReadmeService CreateService(HttpMessageHandler handler)
	{
		HttpClient client = new(handler)
		{
			BaseAddress = new Uri("https://markdownservice.azurewebsites.net/"),
		};
		MemoryCache cache = new(new MemoryCacheOptions());
		return new ReadmeService(client, cache, NullLogger<ReadmeService>.Instance);
	}

	private static HttpResponseMessage HtmlResponse(string html) =>
		new(HttpStatusCode.OK)
		{
			Content = new StringContent(html, Encoding.UTF8, "text/html"),
		};

	private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
	{
		public int RequestCount { get; private set; }

		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken)
		{
			RequestCount++;
			return Task.FromResult(responder(request));
		}
	}

	private sealed class TimeoutHandler : HttpMessageHandler
	{
		protected override Task<HttpResponseMessage> SendAsync(
			HttpRequestMessage request,
			CancellationToken cancellationToken) =>
			Task.FromException<HttpResponseMessage>(new TaskCanceledException("Timed out."));
	}
}
