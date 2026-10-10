namespace VsixGallery.Tests;

public class GalleryCacheVersionTests
{
	[Fact]
	public void Changes_KeepUnrelatedVersionsStable_AndInvalidateRemovedScopes()
	{
		GalleryCacheVersion versions = new();
		versions.Initialize([
			new Package { ID = "One", Author = "Old Publisher" },
			new Package { ID = "Two", Author = "Unchanged Publisher" },
		]);

		versions.Change("One", "Old Publisher", "New Publisher", true, ["New Publisher", "Unchanged Publisher"]);
		Assert.Equal(1, versions.Value);
		Assert.Equal(1, versions.Extension("One"));
		Assert.Equal(0, versions.Extension("Two"));
		Assert.Equal(1, versions.Author("old publisher"));
		Assert.Equal(1, versions.Author("new publisher"));
		Assert.Equal(0, versions.Author("unchanged publisher"));

		versions.Change("One", "New Publisher", null, false, ["Unchanged Publisher"]);
		Assert.Equal(2, versions.Extension("One"));
		Assert.Equal(2, versions.Author("New Publisher"));
		Assert.Equal(0, versions.Extension("Two"));

		versions.Change("One", null, "New Publisher", true, ["New Publisher", "Unchanged Publisher"]);
		Assert.Equal(3, versions.Extension("One"));
		Assert.Equal(3, versions.Author("NEW PUBLISHER"));
		Assert.Equal(GalleryCacheVersion.AuthorTag("New Publisher"), GalleryCacheVersion.AuthorTag("NEW PUBLISHER"));
	}
}