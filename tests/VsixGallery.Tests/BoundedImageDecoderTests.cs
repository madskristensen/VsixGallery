using SkiaSharp;

namespace VsixGallery.Tests;

public class BoundedImageDecoderTests
{
	[Fact]
	public void Decode_UsesSupportedJpegScaleBeforeAllocatingPixels()
	{
		using TestImage image = new(2_048, 1_024, SKEncodedImageFormat.Jpeg, new SKColor(20, 25, 35));

		using SKBitmap? bitmap = BoundedImageDecoder.Decode(image.Path, 135);

		Assert.NotNull(bitmap);
		Assert.True(bitmap.Width < 2_048);
		Assert.True(bitmap.Height < 1_024);
		Assert.InRange(bitmap.Width, 135, 256);
		Assert.InRange(bitmap.Height, 1, 128);
		Assert.True(ImageContrastAnalyzer.TryAnalyze(image.Path, out ImageContrastResult contrast));
		Assert.True(contrast.LowContrastOnDarkTheme);
		Assert.False(contrast.LowContrastOnLightTheme);
	}

	[Fact]
	public void Decode_PreservesSmallPngAndTransparentContrast()
	{
		using TestImage image = new(32, 32, SKEncodedImageFormat.Png, SKColors.Transparent);

		using SKBitmap? bitmap = BoundedImageDecoder.Decode(image.Path, 135);

		Assert.NotNull(bitmap);
		Assert.Equal(32, bitmap.Width);
		Assert.Equal(32, bitmap.Height);
		Assert.True(ImageContrastAnalyzer.TryAnalyze(image.Path, out ImageContrastResult contrast));
		Assert.True(contrast.LowContrastOnDarkTheme);
		Assert.True(contrast.LowContrastOnLightTheme);
	}

	[Theory]
	[InlineData(4_097, 1)]
	[InlineData(1, 4_097)]
	public void Decode_AndContrastRejectOversizedSourceDimensions(int width, int height)
	{
		using TestImage image = new(width, height, SKEncodedImageFormat.Png, SKColors.Black);

		Assert.Null(BoundedImageDecoder.Decode(image.Path, 128));
		Assert.False(ImageContrastAnalyzer.TryAnalyze(image.Path, out _));
	}

	private sealed class TestImage : IDisposable
	{
		public TestImage(int width, int height, SKEncodedImageFormat format, SKColor color)
		{
			Path = System.IO.Path.Combine(AppContext.BaseDirectory, Guid.NewGuid().ToString("N") + ".image");
			using SKBitmap bitmap = new(width, height);
			bitmap.Erase(color);
			using SKImage image = SKImage.FromBitmap(bitmap);
			using SKData data = image.Encode(format, 90);
			File.WriteAllBytes(Path, data.ToArray());
		}

		public string Path { get; }

		public void Dispose() => File.Delete(Path);
	}
}