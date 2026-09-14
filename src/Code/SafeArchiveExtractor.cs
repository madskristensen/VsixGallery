using System.IO.Compression;

namespace VsixGallery;

internal static class SafeArchiveExtractor
{
	internal const int MaxEntries = 10_000;
	internal const long MaxExpandedBytes = 1_000_000_000;
	internal const int MaxCompressionRatio = 200;

	public static async Task ExtractAsync(string archivePath, string destinationRoot, CancellationToken cancellationToken)
	{
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		if (archive.Entries.Count > MaxEntries)
		{
			throw new InvalidDataException($"The VSIX contains more than {MaxEntries:N0} entries.");
		}

		long expandedBytes = 0;
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entry.Length > MaxExpandedBytes - expandedBytes)
			{
				throw new InvalidDataException("The expanded VSIX is too large.");
			}
			expandedBytes += entry.Length;

			if (entry.CompressedLength > 0 && entry.Length / entry.CompressedLength > MaxCompressionRatio)
			{
				throw new InvalidDataException("The VSIX contains an entry with an unsafe compression ratio.");
			}

			string relativePath = entry.FullName.Replace('\\', Path.DirectorySeparatorChar);
			string destinationPath = PackagePath.GetContainedPath(destinationRoot, relativePath);

			if (string.IsNullOrEmpty(entry.Name))
			{
				Directory.CreateDirectory(destinationPath);
				continue;
			}

			Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
			await using Stream source = entry.Open();
			await using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			await source.CopyToAsync(destination, cancellationToken);
		}
	}
}
