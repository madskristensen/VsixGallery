using Ganss.Xss;

using AngleSharp.Dom;

using Microsoft.Extensions.Caching.Memory;

using System.Diagnostics.CodeAnalysis;

namespace VsixGallery;

public sealed class ReadmeService(
	HttpClient httpClient,
	IMemoryCache cache,
	ILogger<ReadmeService> logger)
{
	private const int MaxResponseBytes = 2_000_000;
	private static readonly TimeSpan _successCacheDuration = TimeSpan.FromHours(6);
	private static readonly TimeSpan _failureCacheDuration = TimeSpan.FromMinutes(5);
	private static readonly HashSet<string> _allowedSourceHosts = new(StringComparer.OrdinalIgnoreCase)
	{
		"gist.githubusercontent.com",
		"raw.githubusercontent.com",
	};

	public async Task<string?> GetSanitizedHtmlAsync(string? readmeUrl, CancellationToken cancellationToken)
	{
		if (!IsSafeSource(readmeUrl, out Uri? source))
		{
			return null;
		}

		string cacheKey = "readme:" + source.AbsoluteUri;
		if (cache.TryGetValue(cacheKey, out ReadmeCacheEntry? cached))
		{
			return cached?.Html;
		}

		string? html = await FetchWithFallbackAsync(source, cancellationToken);
		cache.Set(
			cacheKey,
			new ReadmeCacheEntry(html),
			html is null ? _failureCacheDuration : _successCacheDuration);
		return html;
	}

	private async Task<string?> FetchWithFallbackAsync(Uri source, CancellationToken cancellationToken)
	{
		string? html = await FetchAsync(source, cancellationToken);
		if (!string.IsNullOrWhiteSpace(html))
		{
			return html;
		}

		Uri? alternate = GetAlternateUrl(source);
		return alternate is null ? null : await FetchAsync(alternate, cancellationToken);
	}

	private async Task<string?> FetchAsync(Uri source, CancellationToken cancellationToken)
	{
		try
		{
			string requestPath = "markdown.ashx?url=" + Uri.EscapeDataString(source.AbsoluteUri);
			using HttpResponseMessage response = await httpClient.GetAsync(
				requestPath,
				HttpCompletionOption.ResponseHeadersRead,
				cancellationToken);

			if (!response.IsSuccessStatusCode)
			{
				logger.LogInformation(
					"README rendering returned {StatusCode} for {ReadmeHost}.",
					response.StatusCode,
					source.Host);
				return null;
			}

			if (response.Content.Headers.ContentLength > MaxResponseBytes)
			{
				logger.LogWarning("README rendering exceeded the size limit for {ReadmeHost}.", source.Host);
				return null;
			}

			await response.Content.LoadIntoBufferAsync(MaxResponseBytes, cancellationToken);
			string rendered = await response.Content.ReadAsStringAsync(cancellationToken);
			if (string.IsNullOrWhiteSpace(rendered))
			{
				return null;
			}

			HtmlSanitizer sanitizer = CreateSanitizer();
			string sanitized = sanitizer.Sanitize(rendered, source.AbsoluteUri);
			return string.IsNullOrWhiteSpace(sanitized) ? null : sanitized;
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (OperationCanceledException ex)
		{
			logger.LogWarning(ex, "README rendering timed out for {ReadmeHost}.", source.Host);
			return null;
		}
		catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
		{
			logger.LogWarning(ex, "README rendering failed for {ReadmeHost}.", source.Host);
			return null;
		}
	}

	private static HtmlSanitizer CreateSanitizer()
	{
		HtmlSanitizer sanitizer = new();
		sanitizer.AllowedAttributes.Remove("style");
		sanitizer.AllowedSchemes.Clear();
		sanitizer.AllowedSchemes.Add("https");
		sanitizer.AllowedSchemes.Add("http");
		sanitizer.AllowedSchemes.Add("mailto");
		sanitizer.PostProcessNode += static (_, args) =>
		{
			if (args.Node is IElement { LocalName: "a" } link)
			{
				link.SetAttribute("rel", "noopener noreferrer");
			}
		};
		return sanitizer;
	}

	private static bool IsSafeSource(string? value, [NotNullWhen(true)] out Uri? source)
	{
		source = null;
		if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? candidate) ||
			candidate.Scheme != Uri.UriSchemeHttps ||
			!string.IsNullOrEmpty(candidate.UserInfo) ||
			candidate.IsLoopback ||
			!_allowedSourceHosts.Contains(candidate.Host))
		{
			return false;
		}

		source = candidate;
		return true;
	}

	private static Uri? GetAlternateUrl(Uri source)
	{
		string url = source.AbsoluteUri;
		(string From, string To)[] swaps =
		[
			("/refs/heads/main/", "/refs/heads/master/"),
			("/refs/heads/master/", "/refs/heads/main/"),
			("/main/", "/master/"),
			("/master/", "/main/"),
		];

		string alternate = string.Empty;
		foreach ((string from, string to) in swaps)
		{
			if (url.Contains(from, StringComparison.Ordinal))
			{
				alternate = url.Replace(from, to, StringComparison.Ordinal);
				break;
			}
		}

		return Uri.TryCreate(alternate, UriKind.Absolute, out Uri? result) ? result : null;
	}

	private sealed record ReadmeCacheEntry(string? Html);
}
