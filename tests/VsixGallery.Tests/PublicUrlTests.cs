using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace VsixGallery.Tests;

public class PublicUrlTests
{
	[Fact]
	public void GetOrigin_UsesConfiguredPublicSiteInsteadOfRequestHost()
	{
		PublicUrl publicUrl = new(Options.Create(new DisplayOptions
		{
			SiteUrl = "https://www.vsixgallery.com/path",
		}));
		DefaultHttpContext context = new();
		context.Request.Scheme = "http";
		context.Request.Host = new HostString("untrusted.example");

		Assert.Equal("https://www.vsixgallery.com", publicUrl.GetOrigin(context.Request));
	}
}
