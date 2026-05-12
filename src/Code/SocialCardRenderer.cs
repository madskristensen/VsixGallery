using SkiaSharp;

using System;
using System.IO;

namespace VsixGallery
{
	/// <summary>
	/// Renders a 1200x630 PNG suitable for use as an og:image / twitter:card image.
	/// Cards are cached to disk so they are only rendered once per extension version.
	/// </summary>
	public class SocialCardRenderer
	{
		public const int Width = 1200;
		public const int Height = 630;

		private readonly static SKColor _bgTop = new(0x1B, 0x2A, 0x4E);
		private readonly static SKColor _bgBottom = new(0x0B, 0x14, 0x2A);
		private readonly static SKColor _textPrimary = new(0xFF, 0xFF, 0xFF);
		private readonly static SKColor _textSecondary = new(0xAE, 0xB8, 0xD0);
		private readonly static SKColor _accent = new(0x4F, 0x9C, 0xFF);

		private readonly object _renderLock = new();

		public byte[] RenderExtensionCard(Package package, string iconPath, string siteName, string logoPath)
		{
			if (package == null)
			{
				return RenderDefaultCard(siteName, "An alternative Visual Studio extension gallery", logoPath);
			}

			string title = package.Name ?? string.Empty;
			string description = package.Description ?? string.Empty;
			string author = string.IsNullOrWhiteSpace(package.Author) ? null : $"by {package.Author}";
			string version = string.IsNullOrWhiteSpace(package.Version) ? null : $"v{package.Version}";

			return Render(title, description, author, version, iconPath, siteName, logoPath);
		}

		public byte[] RenderDefaultCard(string siteName, string tagline, string logoPath)
		{
			return Render(
				siteName ?? "Open VSIX Gallery",
				tagline ?? "An alternative Visual Studio extension gallery for nightly builds.",
				null,
				null,
				logoPath,
				siteName,
				logoPath);
		}

		private byte[] Render(string title, string description, string author, string version, string iconPath, string siteName, string logoPath)
		{
			lock (_renderLock)
			{
				SKImageInfo info = new(Width, Height, SKColorType.Rgba8888, SKAlphaType.Premul);
				using SKSurface surface = SKSurface.Create(info);
				SKCanvas canvas = surface.Canvas;

				DrawBackground(canvas);
				DrawAccentBar(canvas);

				const int padding = 80;
				const int iconSize = 220;
				int textX = padding;
				int textWidth = Width - padding * 2;

				if (!string.IsNullOrEmpty(iconPath))
				{
					DrawIcon(canvas, iconPath, padding, padding, iconSize);
					textX = padding + iconSize + 60;
					textWidth = Width - textX - padding;
				}

				using SKTypeface boldFace = GetTypeface(SKFontStyle.Bold);
				using SKTypeface regularFace = GetTypeface(SKFontStyle.Normal);

				int titleY = padding + 70;
				DrawWrappedText(canvas, title, boldFace, 64, _textPrimary, textX, titleY, textWidth, 2, out int titleBottom);

				if (!string.IsNullOrWhiteSpace(description))
				{
					int descY = titleBottom + 30;
					DrawWrappedText(canvas, description, regularFace, 32, _textSecondary, textX, descY, textWidth, 4, out _);
				}

				DrawFooter(canvas, regularFace, boldFace, author, version, siteName, padding, logoPath);

				using SKImage image = surface.Snapshot();
				using SKData data = image.Encode(SKEncodedImageFormat.Png, 90);
				return data.ToArray();
			}
		}

		private static void DrawBackground(SKCanvas canvas)
		{
			using SKPaint paint = new()
			{
				Shader = SKShader.CreateLinearGradient(
					new SKPoint(0, 0),
					new SKPoint(0, Height),
					new[] { _bgTop, _bgBottom },
					null,
					SKShaderTileMode.Clamp),
			};
			canvas.DrawRect(new SKRect(0, 0, Width, Height), paint);
		}

		private static void DrawAccentBar(SKCanvas canvas)
		{
			using SKPaint paint = new() { Color = _accent, Style = SKPaintStyle.Fill };
			canvas.DrawRect(new SKRect(0, 0, Width, 10), paint);
		}

		private static void DrawIcon(SKCanvas canvas, string iconPath, int x, int y, int size)
		{
			try
			{
				using SKBitmap bitmap = SKBitmap.Decode(iconPath);
				if (bitmap == null)
				{
					DrawIconPlaceholder(canvas, x, y, size);
					return;
				}

				using SKPaint bgPaint = new() { Color = new SKColor(0xFF, 0xFF, 0xFF, 0x20), Style = SKPaintStyle.Fill, IsAntialias = true };
				canvas.DrawRoundRect(new SKRect(x, y, x + size, y + size), 24, 24, bgPaint);

				int inset = 20;
				SKRect target = new(x + inset, y + inset, x + size - inset, y + size - inset);
				using SKImage image = SKImage.FromBitmap(bitmap);
				canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
			}
			catch
			{
				DrawIconPlaceholder(canvas, x, y, size);
			}
		}

		private static void DrawIconPlaceholder(SKCanvas canvas, int x, int y, int size)
		{
			using SKPaint paint = new() { Color = new SKColor(0xFF, 0xFF, 0xFF, 0x18), Style = SKPaintStyle.Fill, IsAntialias = true };
			canvas.DrawRoundRect(new SKRect(x, y, x + size, y + size), 24, 24, paint);
		}

		private static void DrawWrappedText(SKCanvas canvas, string text, SKTypeface typeface, float fontSize, SKColor color, int x, int y, int maxWidth, int maxLines, out int finalBottom)
		{
			finalBottom = y;
			if (string.IsNullOrEmpty(text))
			{
				return;
			}

			using SKFont font = new(typeface, fontSize);
			using SKPaint paint = new() { Color = color, IsAntialias = true };

			string[] words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			System.Collections.Generic.List<string> lines = [];
			string current = string.Empty;

			foreach (string word in words)
			{
				string candidate = current.Length == 0 ? word : current + " " + word;
				float width = font.MeasureText(candidate);
				if (width <= maxWidth)
				{
					current = candidate;
				}
				else
				{
					if (current.Length > 0)
					{
						lines.Add(current);
					}
					current = word;
					if (lines.Count >= maxLines)
					{
						break;
					}
				}
			}

			if (current.Length > 0 && lines.Count < maxLines)
			{
				lines.Add(current);
			}

			if (lines.Count >= maxLines && lines.Count > 0)
			{
				string last = lines[lines.Count - 1];
				while (last.Length > 0 && font.MeasureText(last + "…") > maxWidth)
				{
					last = last.Substring(0, last.Length - 1);
				}
				if (lines.Count == maxLines && words.Length > 0)
				{
					string joined = string.Join(' ', lines);
					if (joined.Length < text.Length)
					{
						last += "…";
					}
				}
				lines[lines.Count - 1] = last;
			}

			float lineHeight = fontSize * 1.2f;
			float currentY = y;
			foreach (string line in lines)
			{
				canvas.DrawText(line, x, currentY, SKTextAlign.Left, font, paint);
				currentY += lineHeight;
			}

			finalBottom = (int)(currentY - lineHeight * 0.2f);
		}

		private static void DrawFooter(SKCanvas canvas, SKTypeface regular, SKTypeface bold, string author, string version, string siteName, int padding, string logoPath)
		{
			int footerY = Height - padding;

			using SKFont smallFont = new(regular, 26);
			using SKPaint mutedPaint = new() { Color = _textSecondary, IsAntialias = true };

			System.Collections.Generic.List<string> footerParts = [];
			if (!string.IsNullOrEmpty(author)) footerParts.Add(author);
			if (!string.IsNullOrEmpty(version)) footerParts.Add(version);

			if (footerParts.Count > 0)
			{
				canvas.DrawText(string.Join("  •  ", footerParts), padding, footerY, SKTextAlign.Left, smallFont, mutedPaint);
			}

			if (!string.IsNullOrWhiteSpace(siteName))
			{
				using SKFont siteFont = new(bold, 32);
				using SKPaint accentPaint = new() { Color = _textPrimary, IsAntialias = true };
				float textWidth = siteFont.MeasureText(siteName);

				const int logoSize = 44;
				const int logoGap = 16;
				bool hasLogo = !string.IsNullOrEmpty(logoPath) && File.Exists(logoPath);
				float totalWidth = textWidth + (hasLogo ? logoSize + logoGap : 0);
				float startX = Width - padding - totalWidth;

				if (hasLogo)
				{
					try
					{
						using SKBitmap bitmap = SKBitmap.Decode(logoPath);
						if (bitmap != null)
						{
							using SKImage image = SKImage.FromBitmap(bitmap);
							SKRect target = new(startX, footerY - logoSize + 4, startX + logoSize, footerY + 4);
							canvas.DrawImage(image, target, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
						}
					}
					catch
					{
						// Best effort; ignore decoding errors.
					}
					startX += logoSize + logoGap;
				}

				canvas.DrawText(siteName, startX, footerY, SKTextAlign.Left, siteFont, accentPaint);
			}
		}

		private static SKTypeface GetTypeface(SKFontStyle style)
		{
			// Try a series of common family names. SkiaSharp.NativeAssets.Linux
			// uses fontconfig to pick a real font; Windows has plenty. As a last
			// resort fall back to the default typeface so the call never fails.
			string[] families = ["Inter", "Segoe UI", "Helvetica Neue", "Helvetica", "Arial", "DejaVu Sans", "Liberation Sans", "sans-serif"];

			foreach (string family in families)
			{
				SKTypeface tf = SKTypeface.FromFamilyName(family, style);
				if (tf != null && !string.IsNullOrEmpty(tf.FamilyName) && !tf.FamilyName.Equals("System Font", StringComparison.OrdinalIgnoreCase))
				{
					return tf;
				}
				tf?.Dispose();
			}

			return SKTypeface.Default;
		}
	}
}
