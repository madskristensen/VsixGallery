using Microsoft.Extensions.Options;

namespace VsixGallery;

public sealed class PublicUrl(IOptions<DisplayOptions> options)
{
	private readonly string? _configuredOrigin = NormalizeOrigin(options.Value.SiteUrl);

	public string GetOrigin(HttpRequest request)
	{
		return _configuredOrigin ?? $"{request.Scheme}://{request.Host}";
	}

	private static string? NormalizeOrigin(string? value)
	{
		if (string.IsNullOrWhiteSpace(value) ||
			!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
			(uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
		{
			return null;
		}

		return uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
	}
}
