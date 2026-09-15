namespace VsixGallery;

internal static class StaticAssetCache
{
	public static bool IsVersionedVsixPath(string rawTarget)
	{
		int queryStart = rawTarget.IndexOf('?');
		string rawPath = queryStart >= 0 ? rawTarget[..queryStart] : rawTarget;
		int fileNameStart = rawPath.LastIndexOf('/') + 1;
		string requestedFileName = rawPath[fileNameStart..];

		return requestedFileName.EndsWith(".vsix", StringComparison.OrdinalIgnoreCase) &&
			(requestedFileName.Contains("%20v", StringComparison.OrdinalIgnoreCase) ||
			 requestedFileName.Contains(" v", StringComparison.OrdinalIgnoreCase));
	}
}
