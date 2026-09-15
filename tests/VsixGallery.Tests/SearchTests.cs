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

	[Fact]
	public void Lookup_OrdersByRelevanceBeforeRecency()
	{
		Package recentDescriptionMatch = new()
		{
			Name = "Recent extension",
			Description = "Git tooling",
			DatePublished = new DateTime(2026, 9, 14),
		};
		Package olderNameMatch = new()
		{
			Name = "Git extension",
			Description = "Developer tooling",
			DatePublished = new DateTime(2026, 9, 1),
		};

		Package[] results = [.. SearchModel.Lookup("git", [recentDescriptionMatch, olderNameMatch])];

		Assert.Equal([olderNameMatch, recentDescriptionMatch], results);
	}

	[Fact]
	public void Lookup_UsesRecencyToBreakRelevanceTies()
	{
		Package older = new() { Name = "Git tools", DatePublished = new DateTime(2026, 9, 1) };
		Package newer = new() { Name = "Git helper", DatePublished = new DateTime(2026, 9, 14) };

		Package[] results = [.. SearchModel.Lookup("git", [older, newer])];

		Assert.Equal([newer, older], results);
	}
}
