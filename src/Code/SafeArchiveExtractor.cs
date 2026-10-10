using System.IO.Compression;
using System.Buffers;

namespace VsixGallery;

internal static class SafeArchiveExtractor
{
	internal const int MaxEntries = 10_000;
	internal const long MaxExpandedBytes = 1_000_000_000;
	internal const int MaxCompressionRatio = 200;

	public static async Task ExtractAsync(string archivePath, string destinationRoot, CancellationToken cancellationToken)
	{
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		Dictionary<string, ZipArchiveEntry> entries = ValidateEntries(archive, destinationRoot, cancellationToken);
		foreach ((string path, ZipArchiveEntry entry) in entries)
		{
			await ExtractEntryAsync(entry, PackagePath.GetContainedPath(destinationRoot, path), cancellationToken);
		}
	}

	public static async Task ExtractMetadataAsync(string archivePath, string destinationRoot, CancellationToken cancellationToken)
	{
		using ZipArchive archive = ZipFile.OpenRead(archivePath);
		Dictionary<string, ZipArchiveEntry> entries = ValidateEntries(archive, destinationRoot, cancellationToken);
		if (!entries.TryGetValue("extension.vsixmanifest", out ZipArchiveEntry? manifest) || IsDirectory(manifest))
		{
			throw new InvalidDataException("The VSIX manifest was not found.");
		}

		EnsureMetadataSize(manifest, VsixManifestParser.MaxManifestBytes);
		string manifestPath = Path.Combine(destinationRoot, "extension.vsixmanifest");
		await ExtractEntryAsync(manifest, manifestPath, cancellationToken);
		(string? icon, string? license) = VsixManifestParser.GetReferencedFiles(manifestPath);
		HashSet<ZipArchiveEntry> selected = [manifest];
		await ExtractReferenceAsync(icon, null);
		await ExtractReferenceAsync(license, VsixManifestParser.MaxMetadataFileBytes);

		KeyValuePair<string, ZipArchiveEntry> extensionList = entries.FirstOrDefault(pair =>
			!IsDirectory(pair.Value) && pair.Key.EndsWith(".vsext", StringComparison.OrdinalIgnoreCase));
		if (extensionList.Value is not null)
		{
			EnsureMetadataSize(extensionList.Value, VsixManifestParser.MaxMetadataFileBytes);
			if (selected.Add(extensionList.Value))
			{
				await ExtractEntryAsync(extensionList.Value,
					PackagePath.GetContainedPath(destinationRoot, extensionList.Key), cancellationToken);
			}
		}

		async Task ExtractReferenceAsync(string? reference, long? maximumBytes)
		{
			if (string.IsNullOrEmpty(reference)) return;
			string path;
			try
			{
				string containedPath = PackagePath.GetContainedPath(destinationRoot,
					VsixManifestParser.NormalizeRelativePath(reference)!);
				path = Path.GetRelativePath(destinationRoot, containedPath);
			}
			catch (InvalidDataException)
			{
				// Missing or invalid references remain validation findings, as before.
				return;
			}

			if (entries.TryGetValue(path, out ZipArchiveEntry? entry) && !IsDirectory(entry))
			{
				if (maximumBytes.HasValue) EnsureMetadataSize(entry, maximumBytes.Value);
				if (selected.Add(entry))
				{
					await ExtractEntryAsync(entry, PackagePath.GetContainedPath(destinationRoot, path), cancellationToken);
				}
			}
		}
	}

	private static Dictionary<string, ZipArchiveEntry> ValidateEntries(
		ZipArchive archive, string destinationRoot, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (archive.Entries.Count > MaxEntries)
		{
			throw new InvalidDataException($"The VSIX contains more than {MaxEntries:N0} entries.");
		}

		Dictionary<string, ZipArchiveEntry> entries = new(StringComparer.OrdinalIgnoreCase);
		long expandedBytes = 0;
		foreach (ZipArchiveEntry entry in archive.Entries)
		{
			cancellationToken.ThrowIfCancellationRequested();
			if (entry.Length > MaxExpandedBytes - expandedBytes)
			{
				throw new InvalidDataException("The expanded VSIX is too large.");
			}
			expandedBytes += entry.Length;

			if (entry.Length > 0 && (entry.CompressedLength == 0 ||
				(double)entry.Length / entry.CompressedLength > MaxCompressionRatio))
			{
				throw new InvalidDataException("The VSIX contains an entry with an unsafe compression ratio.");
			}

			string relativePath = NormalizePath(entry.FullName);
			PackagePath.GetContainedPath(destinationRoot, relativePath);
			if (!entries.TryAdd(relativePath, entry))
			{
				throw new InvalidDataException("The VSIX contains duplicate paths.");
			}
		}

		foreach (string path in entries.Keys)
		{
			string? parent = Path.GetDirectoryName(path);
			while (!string.IsNullOrEmpty(parent))
			{
				if (entries.TryGetValue(parent, out ZipArchiveEntry? entry) && !IsDirectory(entry))
				{
					throw new InvalidDataException("A VSIX file path conflicts with a directory.");
				}
				parent = Path.GetDirectoryName(parent);
			}
		}
		return entries;
	}

	private static string NormalizePath(string path)
	{
		string normalized = path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
		if (Path.IsPathRooted(normalized)) throw new InvalidDataException("Package paths must be relative.");
		normalized = normalized.TrimEnd(Path.DirectorySeparatorChar);
		string[] segments = normalized.Split(Path.DirectorySeparatorChar);
		if (segments.Any(segment => string.IsNullOrEmpty(segment) || segment is "." or ".." ||
			segment.EndsWith('.') || segment.EndsWith(' ') || segment.Any(char.IsControl) ||
			segment.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 || IsDeviceName(segment)))
		{
			throw new InvalidDataException("The VSIX contains an unsafe path.");
		}
		return normalized;
	}

	private static bool IsDeviceName(string segment)
	{
		string name = segment.Split('.')[0];
		return name.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
			name.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
			(name.Length == 4 && name[3] is >= '1' and <= '9' &&
				(name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) || name.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)));
	}

	private static bool IsDirectory(ZipArchiveEntry entry) => entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

	private static void EnsureMetadataSize(ZipArchiveEntry entry, long maximumBytes)
	{
		if (entry.Length > maximumBytes) throw new InvalidDataException("The VSIX metadata is too large.");
	}

	private static async Task ExtractEntryAsync(ZipArchiveEntry entry, string destinationPath, CancellationToken cancellationToken)
	{
		cancellationToken.ThrowIfCancellationRequested();
		if (IsDirectory(entry))
		{
			Directory.CreateDirectory(destinationPath);
			return;
		}

		Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
		await using Stream source = entry.Open();
		await using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
		byte[] buffer = ArrayPool<byte>.Shared.Rent(81_920);
		try
		{
			long expandedBytes = 0;
			int count;
			while ((count = await source.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
			{
				if (count > entry.Length - expandedBytes) throw new InvalidDataException("A VSIX entry exceeded its declared size.");
				expandedBytes += count;
				await destination.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
			}
			if (expandedBytes != entry.Length) throw new InvalidDataException("A VSIX entry did not match its declared size.");
		}
		finally
		{
			ArrayPool<byte>.Shared.Return(buffer);
		}
	}
}
