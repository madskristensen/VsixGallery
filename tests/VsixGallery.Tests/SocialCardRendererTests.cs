using SkiaSharp;

namespace VsixGallery.Tests;

public class SocialCardRendererTests
{
	[Theory]
	[InlineData(true, 4_097, 1)]
	[InlineData(true, 1, 4_097)]
	[InlineData(false, 4_097, 1)]
	[InlineData(false, 1, 4_097)]
	public void RenderExtensionCard_OversizedIconOrFooterLogo_PreservesFallback(bool oversizedIcon, int width, int height)
	{
		string root = Path.Combine(AppContext.BaseDirectory, "social-card-tests", Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);
		try
		{
			string oversizedPath = Path.Combine(root, "oversized.png");
			string invalidPath = Path.Combine(root, "invalid.png");
			using (SKBitmap source = new(width, height))
			{
				source.Erase(SKColors.Red);
				using SKImage image = SKImage.FromBitmap(source);
				using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
				File.WriteAllBytes(oversizedPath, data.ToArray());
			}
			File.WriteAllText(invalidPath, "not an image");
			using (SKCodec codec = SKCodec.Create(oversizedPath))
			{
				Assert.Equal(width, codec.Info.Width);
				Assert.Equal(height, codec.Info.Height);
			}

			SocialCardRenderer renderer = new();
			Package package = new() { Name = "Example Extension", Description = "Example description", Author = "Publisher", Version = "1.0" };
			byte[] expected = renderer.RenderExtensionCard(package, invalidPath, "Gallery", invalidPath);
			byte[] actual = renderer.RenderExtensionCard(package,
				oversizedIcon ? oversizedPath : invalidPath,
				"Gallery", oversizedIcon ? invalidPath : oversizedPath);

			Assert.Equal([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A], actual[..8]);
			using SKBitmap rendered = SKBitmap.Decode(actual);
			Assert.Equal(SocialCardRenderer.Width, rendered.Width);
			Assert.Equal(SocialCardRenderer.Height, rendered.Height);
			Assert.Equal(expected, actual);
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}
}