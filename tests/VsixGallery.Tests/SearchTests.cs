using VsixGallery.Pages;

namespace VsixGallery.Tests;

public class SearchTests
{
	[Fact]
	public void Lookup_RequiresEverySearchToken()
	{
		Package completeMatch = new() { Name = "Git Lens", Description = "Repository tools" };
		Package partialMatch = new() { Name = "Git Helper", Description = "Repository tools" };

		Package[] results = [.. SearchModel.Lookup("git lens", [completeMatch, partialMatch])];

		Assert.Equal([completeMatch], results);
	}
}
