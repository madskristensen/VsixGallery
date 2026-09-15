namespace VsixGallery.Tests;

public class StaticAssetCacheTests
{
	[Theory]
	[InlineData("/extensions/example/Example%20Extension%20v1.2.3.vsix")]
	[InlineData("/extensions/example/Example Extension v1.2.3.vsix?download=true")]
	public void IsVersionedVsixPath_AcceptsFriendlyVersionedUrls(string path)
	{
		Assert.True(StaticAssetCache.IsVersionedVsixPath(path));
	}

	[Theory]
	[InlineData("/extensions/example/extension.vsix")]
	[InlineData("/extensions/example/extension.vsix?v=1")]
	[InlineData("/extensions/example/icon-v1.webp")]
	public void IsVersionedVsixPath_RejectsCanonicalAndNonVsixUrls(string path)
	{
		Assert.False(StaticAssetCache.IsVersionedVsixPath(path));
	}
}
