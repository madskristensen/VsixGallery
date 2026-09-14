namespace VsixGallery.Tests;

public class GalleryCacheVersionTests
{
	[Fact]
	public void Increment_ChangesVersion()
	{
		GalleryCacheVersion version = new();

		version.Increment();

		Assert.Equal(1, version.Value);
	}
}
