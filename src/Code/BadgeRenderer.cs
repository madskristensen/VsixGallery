using SkiaSharp;

using System.Text;

namespace VsixGallery
{
	/// <summary>
	/// Renders the "version" badge as either SVG markup or a rasterized PNG.
	/// Both formats share the exact same layout math and Skia text measurement
	/// so the two outputs stay visually identical.
	/// </summary>
	public static class BadgeRenderer
	{
		private const int LabelWidth = 66;
		private const int BadgeHeight = 20;
		private const int Padding = 6;
		private const int LogoX = 6;
		private const int LogoY = 4;
		private const int LogoSize = 12;
		private const int LabelTextX = 22;
		private const int Radius = 3;
		private const string LabelBg = "#555";
		private const string ValueBg = "#007ec6";
		private const string TextColor = "#fff";
		private const string FontFamily = "DejaVu Sans,Verdana,Geneva,sans-serif";
		private const int FontSize = 11;
		private const string Label = "version";

		private static readonly SKTypeface _typeface = LoadTypeface();

		private static SKTypeface LoadTypeface()
		{
			string path = Path.Combine(AppContext.BaseDirectory, "Fonts", "DejaVuSans.ttf");
			return File.Exists(path) ? SKTypeface.FromFile(path) : SKTypeface.Default;
		}

		// Measures the value text with the bundled font so the badge width is exact
		// for both the SVG and PNG outputs.
		private static (int totalWidth, int valueWidth) Measure(string version)
		{
			using var font = new SKFont(_typeface, FontSize);
			float measured = font.MeasureText(version);
			int valueWidth = (int)Math.Ceiling(measured) + (Padding * 2);
			return (LabelWidth + valueWidth, valueWidth);
		}

		public static string RenderSvg(string version)
		{
			(int totalWidth, int valueWidth) = Measure(version);

			var sb = new StringBuilder();
			sb.Append($"""
				<svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="{BadgeHeight}" role="img" aria-label="version: {version}">
				  <title>version: {version}</title>
				  <linearGradient id="s" x2="0" y2="100%">
					<stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
					<stop offset="1" stop-opacity=".1"/>
				  </linearGradient>
				  <clipPath id="r">
					<rect width="{totalWidth}" height="{BadgeHeight}" rx="{Radius}" fill="{TextColor}"/>
				  </clipPath>
				  <g clip-path="url(#r)">
					<rect width="{LabelWidth}" height="{BadgeHeight}" fill="{LabelBg}"/>
					<rect x="{LabelWidth}" width="{valueWidth}" height="{BadgeHeight}" fill="{ValueBg}"/>
					<rect width="{totalWidth}" height="{BadgeHeight}" fill="url(#s)"/>
				  </g>
				  <svg x="{LogoX}" y="{LogoY}" width="{LogoSize}" height="{LogoSize}" viewBox="0 0 256 256" aria-hidden="true">
					<rect x="30" y="30" width="90" height="90" fill="#09F"/>
					<rect x="30" y="136" width="90" height="90" fill="#09F"/>
					<rect x="136" y="136" width="90" height="90" fill="#09F"/>
					<rect x="151" y="15" width="90" height="90" fill="#A8D4FF" transform="rotate(45 196 60)"/>
				  </svg>
				  <g fill="{TextColor}" font-family="{FontFamily}" font-size="{FontSize}">
					<text x="{LabelTextX}" y="15" fill="#010101" fill-opacity=".3">version</text>
					<text x="{LabelTextX}" y="14">version</text>
					<text x="{LabelWidth + valueWidth / 2}" y="15" fill="#010101" fill-opacity=".3" text-anchor="middle">{version}</text>
					<text x="{LabelWidth + valueWidth / 2}" y="14" text-anchor="middle">{version}</text>
				  </g>
				</svg>
				""");
			return sb.ToString();
		}

		public static byte[] RenderPng(string version)
		{
			(int totalWidth, int valueWidth) = Measure(version);

			var info = new SKImageInfo(totalWidth, BadgeHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
			using SKSurface surface = SKSurface.Create(info);
			SKCanvas canvas = surface.Canvas;
			canvas.Clear(SKColors.Transparent);

			var bounds = new SKRect(0, 0, totalWidth, BadgeHeight);
			var clip = new SKRoundRect(bounds, Radius, Radius);

			canvas.Save();
			canvas.ClipRoundRect(clip, antialias: true);

			DrawBackground(canvas, totalWidth, valueWidth);
			DrawLogo(canvas);
			DrawText(canvas, Label, LabelTextX, SKTextAlign.Left);
			DrawText(canvas, version, LabelWidth + (valueWidth / 2f), SKTextAlign.Center);

			canvas.Restore();

			using SKImage image = surface.Snapshot();
			using SKData data = image.Encode(SKEncodedImageFormat.Png, 100);
			return data.ToArray();
		}

		private static void DrawBackground(SKCanvas canvas, int totalWidth, int valueWidth)
		{
			using (var paint = new SKPaint { Color = SKColor.Parse(LabelBg), IsAntialias = true })
			{
				canvas.DrawRect(0, 0, LabelWidth, BadgeHeight, paint);
			}

			using (var paint = new SKPaint { Color = SKColor.Parse(ValueBg), IsAntialias = true })
			{
				canvas.DrawRect(LabelWidth, 0, valueWidth, BadgeHeight, paint);
			}

			// Subtle top-to-bottom gloss matching the SVG linear gradient
			// (#bbb @ 10% on top, black @ 10% on the bottom).
			SKColor[] colors = [new SKColor(0xBB, 0xBB, 0xBB, 26), new SKColor(0, 0, 0, 26)];
			using SKShader shader = SKShader.CreateLinearGradient(
				new SKPoint(0, 0),
				new SKPoint(0, BadgeHeight),
				colors,
				null,
				SKShaderTileMode.Clamp);
			using var gloss = new SKPaint { Shader = shader, IsAntialias = true };
			canvas.DrawRect(0, 0, totalWidth, BadgeHeight, gloss);
		}

		private static void DrawLogo(SKCanvas canvas)
		{
			canvas.Save();
			canvas.Translate(LogoX, LogoY);
			float scale = LogoSize / 256f;
			canvas.Scale(scale, scale);

			using (var blue = new SKPaint { Color = SKColor.Parse("#09F"), IsAntialias = true })
			{
				canvas.DrawRect(30, 30, 90, 90, blue);
				canvas.DrawRect(30, 136, 90, 90, blue);
				canvas.DrawRect(136, 136, 90, 90, blue);
			}

			using (var light = new SKPaint { Color = SKColor.Parse("#A8D4FF"), IsAntialias = true })
			{
				canvas.Save();
				canvas.RotateDegrees(45, 196, 60);
				canvas.DrawRect(151, 15, 90, 90, light);
				canvas.Restore();
			}

			canvas.Restore();
		}

		private static void DrawText(SKCanvas canvas, string text, float x, SKTextAlign align)
		{
			using var font = new SKFont(_typeface, FontSize)
			{
				Subpixel = true,
				Edging = SKFontEdging.SubpixelAntialias,
			};

			// Drop shadow first (#010101 @ 30%), then the white text on top.
			using (var shadow = new SKPaint { Color = new SKColor(0x01, 0x01, 0x01, 77), IsAntialias = true })
			{
				canvas.DrawText(text, x, 15, align, font, shadow);
			}

			using (var fill = new SKPaint { Color = SKColor.Parse(TextColor), IsAntialias = true })
			{
				canvas.DrawText(text, x, 14, align, font, fill);
			}
		}
	}
}
