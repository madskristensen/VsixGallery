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
			"""<h1>Hello</h1><script>alert(1)</script><img src="https://example.com/icon.png"><a href="javascript:alert(2)" onclick="alert(3)" style="color:red">Bad</a><a href="/docs">Safe</a>"""));
		ReadmeService service = CreateService(handler);

		string? html = await service.GetSanitizedHtmlAsync(
			"https://raw.githubusercontent.com/example/project/refs/heads/main/README.md",
			CancellationToken.None);

		Assert.NotNull(html);
		Assert.Contains("<h2>Hello</h2>", html);
		Assert.DoesNotContain("<h1", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("javascript:", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("onclick", html, StringComparison.OrdinalIgnoreCase);
		Assert.DoesNotContain("style=", html, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("href=\"https://raw.githubusercontent.com/docs\"", html);
		Assert.Contains("rel=\"noopener noreferrer\"", html);
		Assert.Contains("loading=\"lazy\"", html);
		Assert.Contains("decoding=\"async\"", html);
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
		ReadmeCache cache = new();
		return new ReadmeService(client, cache, NullLogger<ReadmeService>.Instance);
	}

	[Fact]
	public async Task ConcurrentMisses_AcrossServiceInstances_FetchOnce()
	{
		using ReadmeCache cache = new();
		using BlockingHandler handler = new();
		using HttpClient client = new(handler) { BaseAddress = new Uri("https://markdownservice.azurewebsites.net/") };
		ReadmeService first = new(client, cache, NullLogger<ReadmeService>.Instance);
		ReadmeService second = new(client, cache, NullLogger<ReadmeService>.Instance);
		const string url = "https://raw.githubusercontent.com/example/project/main/README.md";
		Task<string?> leader = first.GetSanitizedHtmlAsync(url, CancellationToken.None);
		await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		Task<string?>[] waiters = Enumerable.Range(0, 20)
			.Select(_ => second.GetSanitizedHtmlAsync(url, CancellationToken.None)).ToArray();
		handler.Release.SetResult();

		Assert.Equal("<p>Shared</p>", await leader);
		Assert.All(await Task.WhenAll(waiters), html => Assert.Equal("<p>Shared</p>", html));
		Assert.Equal(1, handler.RequestCount);
	}

	[Fact]
	public async Task CancellingWaiter_DoesNotCancelSharedFetch()
	{
		using ReadmeCache cache = new();
		using BlockingHandler handler = new();
		using HttpClient client = new(handler) { BaseAddress = new Uri("https://markdownservice.azurewebsites.net/") };
		ReadmeService service = new(client, cache, NullLogger<ReadmeService>.Instance);
		const string url = "https://raw.githubusercontent.com/example/project/main/README.md";
		Task<string?> leader = service.GetSanitizedHtmlAsync(url, CancellationToken.None);
		await handler.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
		using CancellationTokenSource cancellation = new();
		Task<string?> waiter = service.GetSanitizedHtmlAsync(url, cancellation.Token);
		cancellation.Cancel();
		await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
		handler.Release.SetResult();

		Assert.Equal("<p>Shared</p>", await leader);
		Assert.Equal("<p>Shared</p>", await service.GetSanitizedHtmlAsync(url, CancellationToken.None));
		Assert.Equal(1, handler.RequestCount);
	}

	[Fact]
	public async Task Cache_RespectsByteBudget_WithoutLosingRenderedResponse()
	{
		using ReadmeCache cache = new(1_024);
		using StubHandler handler = new(_ => HtmlResponse("<p>" + new string('x', 300) + "</p>"));
		using HttpClient client = new(handler) { BaseAddress = new Uri("https://markdownservice.azurewebsites.net/") };
		ReadmeService service = new(client, cache, NullLogger<ReadmeService>.Instance);
		for (int i = 0; i < 10; i++)
		{
			string? html = await service.GetSanitizedHtmlAsync(
				$"https://raw.githubusercontent.com/example/project{i}/main/README.md", CancellationToken.None);
			Assert.Contains(new string('x', 300), html);
		}

		Assert.InRange(cache.Cache.Count, 0, 1);
	}

	private sealed class BlockingHandler : HttpMessageHandler
	{
		public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
		public int RequestCount;

		protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
		{
			Interlocked.Increment(ref RequestCount);
			Started.TrySetResult();
			await Release.Task.WaitAsync(cancellationToken);
			return HtmlResponse("<p>Shared</p>");
		}
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
