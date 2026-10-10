using SkiaSharp;

namespace VsixGallery;

internal static class BoundedImageDecoder
{
	internal const int MaxDimension = 4_096;
	internal const int MaxFileBytes = 10_000_000;
	internal const long MaxDecodedPixels = (long)MaxDimension * MaxDimension;

	public static SKBitmap? Decode(string path, int targetSize)
	{
		if (targetSize <= 0 || new FileInfo(path).Length > MaxFileBytes) return null;
		using SKCodec codec = SKCodec.Create(path);
		if (codec is null || !IsSafe(codec.Info.Width, codec.Info.Height)) return null;

		float scale = Math.Min(1f, (float)targetSize / Math.Max(codec.Info.Width, codec.Info.Height));
		SKSizeI dimensions = codec.GetScaledDimensions(scale);
		if (!IsSafe(dimensions.Width, dimensions.Height)) return null;

		// Ask the codec for a supported scale (JPEG can decode directly at 1/8).
		// Formats without scaled decoding still obey the source allocation cap.
		SKImageInfo info = new(dimensions.Width, dimensions.Height, SKColorType.Rgba8888, SKAlphaType.Premul);
		return SKBitmap.Decode(codec, info);
	}

	private static bool IsSafe(int width, int height) =>
		width > 0 && height > 0 && width <= MaxDimension && height <= MaxDimension &&
		(long)width * height <= MaxDecodedPixels;
}