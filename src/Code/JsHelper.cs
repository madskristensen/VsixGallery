using WebMarkupMin.Core;

namespace VsixGallery
{
	public static class JsHelper
	{
		private static readonly Lock _lock = new();
		private static readonly Dictionary<string, string> _cache = [];

		public static string GetMinified(IWebHostEnvironment env, string relativePath)
		{
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
				string js = File.ReadAllText(fullPath);

				CrockfordJsMinifier minifier = new();
				CodeMinificationResult result = minifier.Minify(js, isInlineCode: false);
				string minified = result.Errors.Count == 0 ? result.MinifiedContent : js;

				_cache[relativePath] = minified;
				return minified;
			}
		}
	}
}
