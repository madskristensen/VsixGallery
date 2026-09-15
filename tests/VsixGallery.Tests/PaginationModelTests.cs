namespace VsixGallery.Tests;

public class PaginationModelTests
{
	[Fact]
	public void GetUrl_PreservesSearchTermAndFragment()
	{
		PaginationModel model = new()
		{
			CurrentPage = 2,
			TotalPages = 3,
			Path = "/search/",
			SearchTerm = "git tools",
			Fragment = "results",
		};

		Assert.Equal("/search/?q=git%20tools&page=3#results", model.GetUrl(3));
	}

	[Fact]
	public void GetUrl_FirstHomepagePageUsesFragment()
	{
		PaginationModel model = new()
		{
			CurrentPage = 2,
			TotalPages = 3,
			Path = "/",
			Fragment = "extensions",
		};

		Assert.Equal("/#extensions", model.GetUrl(1));
	}
}
