using System.IO.Compression;

namespace VsixGallery.Tests;

public class SafeArchiveExtractorTests
{
	[Fact]
	public async Task ExtractAsync_ExtractsContainedEntry()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(("content/file.txt", "safe"));

		await SafeArchiveExtractor.ExtractAsync(archive.Path, archive.Destination, CancellationToken.None);

		Assert.Equal("safe", await File.ReadAllTextAsync(
			System.IO.Path.Combine(archive.Destination, "content", "file.txt"),
			CancellationToken.None));
	}

	[Fact]
	public async Task ExtractAsync_RejectsTraversalEntry()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(("../outside.txt", "unsafe"));

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			SafeArchiveExtractor.ExtractAsync(archive.Path, archive.Destination, CancellationToken.None));
	}

	[Fact]
	public async Task ExtractAsync_RejectsUnsafeCompressionRatio()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(("content.txt", new string('a', 100_000)));

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			SafeArchiveExtractor.ExtractAsync(archive.Path, archive.Destination, CancellationToken.None));
	}

	[Fact]
	public async Task ExtractAsync_ObservesCancellation()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(("content.txt", "safe"));
		using CancellationTokenSource cancellation = new();
		cancellation.Cancel();

		await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
			SafeArchiveExtractor.ExtractAsync(archive.Path, archive.Destination, cancellation.Token));
	}

	private sealed class TemporaryArchive : IDisposable
	{
		private readonly string _root;

		private TemporaryArchive(string root, string path, string destination)
		{
			_root = root;
			Path = path;
			Destination = destination;
		}

		public string Path { get; }
		public string Destination { get; }

		public static TemporaryArchive Create(params (string Name, string Content)[] entries)
		{
			string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(root);
			string path = System.IO.Path.Combine(root, "package.vsix");
			string destination = System.IO.Path.Combine(root, "extracted");
			Directory.CreateDirectory(destination);

			using (ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create))
			{
				foreach ((string name, string content) in entries)
				{
					ZipArchiveEntry entry = zip.CreateEntry(name);
					using StreamWriter writer = new(entry.Open());
					writer.Write(content);
				}
			}

			return new TemporaryArchive(root, path, destination);
		}

		public void Dispose()
		{
			Directory.Delete(_root, recursive: true);
		}
	}
}
