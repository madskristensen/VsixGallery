using Microsoft.AspNetCore.OutputCaching;

using System.Globalization;

namespace VsixGallery;

internal sealed class GalleryOutputCachePolicy : IOutputCachePolicy
{
	public GalleryOutputCachePolicy()
	{
	}

	public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
	{
		HttpRequest request = context.HttpContext.Request;
		GalleryCacheVersion versions = context.HttpContext.RequestServices.GetRequiredService<GalleryCacheVersion>();
		string? id = Convert.ToString(request.RouteValues["id"], CultureInfo.InvariantCulture);
		string? author = Convert.ToString(request.RouteValues["author"], CultureInfo.InvariantCulture);
		if (string.IsNullOrEmpty(id) && request.Path.StartsWithSegments("/api"))
		{
			id = request.Query["id"].FirstOrDefault();
		}
		if (request.Path.StartsWithSegments("/feed/author"))
		{
			author = id;
			id = null;
		}

		long version;
		if (!string.IsNullOrEmpty(author))
		{
			version = versions.Author(author);
			context.Tags.Add(GalleryCacheVersion.AuthorTag(author));
		}
		else if (!string.IsNullOrWhiteSpace(id))
		{
			version = versions.Extension(id);
			context.Tags.Add(GalleryCacheVersion.ExtensionTag(id));
		}
		else
		{
			version = versions.Value;
			context.Tags.Add(PackageHelper.GalleryCacheTag);
		}

		context.CacheVaryByRules.VaryByValues["generation"] = version.ToString(CultureInfo.InvariantCulture);
		return ValueTask.CompletedTask;
	}

	public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
	public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}