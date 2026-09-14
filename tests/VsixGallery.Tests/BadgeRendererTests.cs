namespace VsixGallery.Tests;

public class BadgeRendererTests
{
	[Fact]
	public void RenderSvg_IncludesAccessibleVersionText()
	{
		string svg = BadgeRenderer.RenderSvg("1.2.3");

		Assert.Contains("aria-label=\"version: 1.2.3\"", svg);
		Assert.Contains("<title>version: 1.2.3</title>", svg);
	}

	[Fact]
	public void RenderPng_ReturnsPngSignature()
	{
		byte[] png = BadgeRenderer.RenderPng("1.2.3");

		Assert.Equal([0x89, 0x50, 0x4E, 0x47], png[..4]);
	}
}
