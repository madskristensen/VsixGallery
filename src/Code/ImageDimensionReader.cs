using SkiaSharp;

using System.IO;

namespace VsixGallery
{
	// Reads pixel dimensions of PNG, JPEG, and GIF images by parsing their
	// headers directly. Replaces the previous use of System.Drawing.Image, which
	// is only supported on Windows on modern .NET and produces CA1416 warnings.
	static internal class ImageDimensionReader
	{
		public static bool TryGetDimensions(string path, out int width, out int height)
		{
			width = 0;
			height = 0;

			try
			{
				using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
				using BinaryReader reader = new(stream);

				if (stream.Length < 12)
				{
					return false;
				}

				byte[] header = reader.ReadBytes(8);
				stream.Position = 0;

				if (IsPng(header))
				{
					return TryReadPng(reader, out width, out height);
				}

				if (IsGif(header))
				{
					return TryReadGif(reader, out width, out height);
				}

				if (IsJpeg(header))
				{
					return TryReadJpeg(reader, out width, out height);
				}
			}
			catch
			{
				// Ignore malformed images; validation will surface the missing dimensions.
			}

			// Fallback: use SkiaSharp for formats not handled above (e.g. WebP).
			try
			{
				using SKBitmap bitmap = SKBitmap.Decode(path);
				if (bitmap != null)
				{
					width = bitmap.Width;
					height = bitmap.Height;
					return width > 0 && height > 0;
				}
			}
			catch { }

			return false;
		}

		private static bool IsPng(byte[] header)
		{
			return header.Length >= 8
				&& header[0] == 0x89 && header[1] == 0x50 && header[2] == 0x4E && header[3] == 0x47
				&& header[4] == 0x0D && header[5] == 0x0A && header[6] == 0x1A && header[7] == 0x0A;
		}

		private static bool IsGif(byte[] header)
		{
			return header.Length >= 6
				&& header[0] == 'G' && header[1] == 'I' && header[2] == 'F'
				&& header[3] == '8' && (header[4] == '7' || header[4] == '9') && header[5] == 'a';
		}

		private static bool IsJpeg(byte[] header)
		{
			return header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;
		}

		private static bool TryReadPng(BinaryReader reader, out int width, out int height)
		{
			// PNG: 8-byte signature, then IHDR chunk: length(4), "IHDR"(4),
			// width(4 BE), height(4 BE), ...
			reader.BaseStream.Position = 16;
			width = ReadInt32BigEndian(reader);
			height = ReadInt32BigEndian(reader);
			return width > 0 && height > 0;
		}

		private static bool TryReadGif(BinaryReader reader, out int width, out int height)
		{
			// GIF: 6-byte signature, then logical screen width (LE u16), height (LE u16).
			reader.BaseStream.Position = 6;
			width = reader.ReadUInt16();
			height = reader.ReadUInt16();
			return width > 0 && height > 0;
		}

		private static bool TryReadJpeg(BinaryReader reader, out int width, out int height)
		{
			width = 0;
			height = 0;

			Stream stream = reader.BaseStream;
			stream.Position = 2; // Skip SOI (FF D8).

			while (stream.Position < stream.Length)
			{
				if (reader.ReadByte() != 0xFF)
				{
					return false;
				}

				byte marker = reader.ReadByte();

				// Skip fill bytes.
				while (marker == 0xFF)
				{
					marker = reader.ReadByte();
				}

				// Standalone markers with no payload.
				if (marker == 0xD8 || marker == 0xD9 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01)
				{
					continue;
				}

				int segmentLength = (reader.ReadByte() << 8) | reader.ReadByte();
				if (segmentLength < 2)
				{
					return false;
				}

				// SOF markers (excluding 0xC4 DHT, 0xC8 JPG, 0xCC DAC) carry frame dimensions.
				bool isSof = (marker >= 0xC0 && marker <= 0xCF) && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
				if (isSof)
				{
					stream.Position += 1; // Sample precision.
					height = (reader.ReadByte() << 8) | reader.ReadByte();
					width = (reader.ReadByte() << 8) | reader.ReadByte();
					return width > 0 && height > 0;
				}

				stream.Position += segmentLength - 2;
			}

			return false;
		}

		private static int ReadInt32BigEndian(BinaryReader reader)
		{
			byte b0 = reader.ReadByte();
			byte b1 = reader.ReadByte();
			byte b2 = reader.ReadByte();
			byte b3 = reader.ReadByte();
			return (b0 << 24) | (b1 << 16) | (b2 << 8) | b3;
		}
	}
}
