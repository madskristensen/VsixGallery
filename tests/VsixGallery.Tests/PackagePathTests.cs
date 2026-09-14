namespace VsixGallery.Tests;

public class PackagePathTests
{
	[Theory]
	[InlineData("Example.Extension")]
	[InlineData("Silverlight Unit Test Adapter.1234")]
	[InlineData("extension-name_2")]
	public void IsValidExtensionId_AcceptsSafeIds(string id)
	{
		Assert.True(PackagePath.IsValidExtensionId(id));
	}

	[Theory]
	[InlineData("")]
	[InlineData(".")]
	[InlineData("..")]
	[InlineData("../outside")]
	[InlineData("folder/name")]
	[InlineData("folder\\name")]
	[InlineData("name..suffix")]
	public void IsValidExtensionId_RejectsUnsafeIds(string id)
	{
		Assert.False(PackagePath.IsValidExtensionId(id));
	}

	[Fact]
	public void GetContainedPath_RejectsParentTraversal()
	{
		string root = Path.Combine(Path.GetTempPath(), "vsixgallery-root");

		Assert.Throws<InvalidDataException>(() => PackagePath.GetContainedPath(root, "../outside"));
	}
}
