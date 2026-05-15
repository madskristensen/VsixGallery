using WebMarkupMin.Core;

namespace VsixGallery
{
	public static class CssHelper
	{
		private static readonly Lock _lock = new();
		private static readonly Dictionary<string, string> _cache = [];

		public static string GetMinified(IWebHostEnvironment env, string relativePath)
		{
			// In Development, always read from disk so CSS edits show up on F5
			// without restarting the app.
			if (env.IsDevelopment())
			{
				string devPath = Path.Combine(env.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
				return File.ReadAllText(devPath);
			}

			if (_cache.TryGetValue(relativePath, out string? cached))
			{
				return cached;
			}

			lock (_lock)
			{
				if (_cache.TryGetValue(relativePath, out cached))
				{
					return cached;
				}

				string fullPath = Path.Combine(env.WebRootPath, relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
				string css = File.ReadAllText(fullPath);

				KristensenCssMinifier minifier = new();
				CodeMinificationResult result = minifier.Minify(css, isInlineCode: false);
				string minified = result.Errors.Count == 0 ? result.MinifiedContent : css;

				_cache[relativePath] = minified;
				return minified;
			}
		}
	}
}
