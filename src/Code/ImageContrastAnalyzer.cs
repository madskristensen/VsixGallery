using SkiaSharp;

namespace VsixGallery
{
	internal readonly record struct ImageContrastResult(
		bool LowContrastOnDarkTheme,
		bool LowContrastOnLightTheme);

	internal static class ImageContrastAnalyzer
	{
		private const double MinimumContrastRatio = 1.8;
		private const double MinimumColorSeparation = 32d / 255;
		private const double MaximumLowContrastFraction = 0.9;
		private const byte MinimumVisibleAlpha = 32;

		private const byte DarkBackgroundComponent = 30;
		private const byte LightBackgroundComponent = 255;

		public static bool TryAnalyze(string path, out ImageContrastResult result)
		{
			result = default;
			try
			{
				using SKBitmap? bitmap = BoundedImageDecoder.Decode(path, 128);
				if (bitmap is null || bitmap.Width <= 0 || bitmap.Height <= 0)
				{
					return false;
				}

				int sampleWidth = Math.Min(bitmap.Width, 128);
				int sampleHeight = Math.Min(bitmap.Height, 128);
				double visibleWeight = 0;
				double darkLowContrastWeight = 0;
				double lightLowContrastWeight = 0;

				for (int sampleY = 0; sampleY < sampleHeight; sampleY++)
				{
					int y = sampleY * bitmap.Height / sampleHeight;
					for (int sampleX = 0; sampleX < sampleWidth; sampleX++)
					{
						int x = sampleX * bitmap.Width / sampleWidth;
						SKColor color = bitmap.GetPixel(x, y);
						if (color.Alpha < MinimumVisibleAlpha)
						{
							continue;
						}

						double alpha = color.Alpha / 255d;
						visibleWeight += alpha;

						if (HasLowContrast(color, alpha, DarkBackgroundComponent))
						{
							darkLowContrastWeight += alpha;
						}

						if (HasLowContrast(color, alpha, LightBackgroundComponent))
						{
							lightLowContrastWeight += alpha;
						}
					}
				}

				if (visibleWeight == 0)
				{
					result = new(true, true);
					return true;
				}

				result = new(
					darkLowContrastWeight / visibleWeight >= MaximumLowContrastFraction,
					lightLowContrastWeight / visibleWeight >= MaximumLowContrastFraction);
				return true;
			}
			catch (ArgumentException)
			{
				return false;
			}
		}

		private static bool HasLowContrast(SKColor color, double alpha, byte backgroundComponent)
		{
			double backgroundLuminance = RelativeLuminance(
				backgroundComponent,
				backgroundComponent,
				backgroundComponent);
			double foregroundLuminance = RelativeLuminance(color.Red, color.Green, color.Blue);
			double compositeLuminance =
				(alpha * foregroundLuminance) + ((1 - alpha) * backgroundLuminance);
			double lighter = Math.Max(compositeLuminance, backgroundLuminance);
			double darker = Math.Min(compositeLuminance, backgroundLuminance);
			double contrastRatio = (lighter + 0.05) / (darker + 0.05);
			double colorSeparation = alpha * Math.Max(
				Math.Abs(color.Red - backgroundComponent),
				Math.Max(
					Math.Abs(color.Green - backgroundComponent),
					Math.Abs(color.Blue - backgroundComponent))) / 255;

			return contrastRatio < MinimumContrastRatio &&
				colorSeparation < MinimumColorSeparation;
		}

		private static double RelativeLuminance(byte red, byte green, byte blue) =>
			(0.2126 * Linearize(red)) +
			(0.7152 * Linearize(green)) +
			(0.0722 * Linearize(blue));

		private static double Linearize(byte component)
		{
			double value = component / 255d;
			return value <= 0.04045
				? value / 12.92
				: Math.Pow((value + 0.055) / 1.055, 2.4);
		}
	}
}
