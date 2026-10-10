using System.IO.Compression;
using System.Buffers.Binary;

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

	[Fact]
	public async Task ExtractMetadataAsync_ExtractsOnlyReferencedFilesAndFirstExtensionList()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			("extension.vsixmanifest", "<PackageManifest><Metadata><Icon>Resources\\Icon.png</Icon><License>Resources\\License.txt</License></Metadata></PackageManifest>"),
			("Resources/ICON.png", "image"),
			("Resources/LICENSE.txt", "license"),
			("nested/first.vsext", "first"),
			("nested/second.vsext", "second"),
			("payload.dll", "not extracted"));

		await SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None);

		Assert.Equal(4, Directory.GetFiles(archive.Destination, "*", SearchOption.AllDirectories).Length);
		Assert.Equal("image", await File.ReadAllTextAsync(System.IO.Path.Combine(archive.Destination, "Resources", "Icon.png")));
		Assert.True(File.Exists(System.IO.Path.Combine(archive.Destination, "nested", "first.vsext")));
		Assert.False(File.Exists(System.IO.Path.Combine(archive.Destination, "payload.dll")));
		Assert.False(File.Exists(System.IO.Path.Combine(archive.Destination, "nested", "second.vsext")));
	}

	[Theory]
	[InlineData("./Resources/Icon.png", "./License.txt")]
	[InlineData(".\\Resources\\Icon.png", ".\\License.txt")]
	[InlineData("Resources/../Resources/Icon.png", "Resources/../License.txt")]
	public async Task ExtractMetadataAsync_ResolvesContainedDotRelativeReferences(string icon, string license)
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			("extension.vsixmanifest", $"""
			<Vsix xmlns="http://schemas.microsoft.com/developer/vsx-schema/2010">
			  <Identifier Id="Dot.Reference"><Name>Dot references</Name><Version>1.0</Version><Author>Publisher</Author>
				<Description>Contained metadata paths</Description><Icon>{icon}</Icon><License>{license}</License>
			  </Identifier>
			</Vsix>
			"""),
			("Resources/Icon.png", "image"), ("License.txt", "MIT license"), ("payload.dll", "unused"));

		await SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None);
		Package package = new VsixManifestParser().CreateFromManifest(archive.Destination, "", "", "");

		Assert.Equal("image", await File.ReadAllTextAsync(System.IO.Path.Combine(archive.Destination, "Resources", "Icon.png")));
		Assert.Equal("MIT license", package.License);
		Assert.NotNull(VsixManifestParser.ResolveRelativeFile(archive.Destination, package.Icon));
		Assert.Equal(3, Directory.GetFiles(archive.Destination, "*", SearchOption.AllDirectories).Length);
	}

	[Theory]
	[InlineData("../outside.txt")]
	[InlineData("Resources/../../outside.txt")]
	[InlineData("..\\outside.txt")]
	[InlineData("/outside.txt")]
	public async Task ExtractMetadataAsync_DoesNotExtractEscapingReferences(string reference)
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			("extension.vsixmanifest", $"<PackageManifest><Metadata><Icon>{reference}</Icon><License>{reference}</License></Metadata></PackageManifest>"),
			("outside.txt", "not referenced"));

		await SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None);

		Assert.Single(Directory.GetFiles(archive.Destination, "*", SearchOption.AllDirectories));
	}

	[Theory]
	[InlineData("../payload.dll")]
	[InlineData("folder/../payload.dll")]
	[InlineData("C:\\payload.dll")]
	[InlineData("/payload.dll")]
	[InlineData("folder\\..\\payload.dll")]
	[InlineData("payload.dll:stream")]
	[InlineData("payload.dll.")]
	[InlineData("CON.txt")]
	[InlineData("content/LPT1.dll")]
	public async Task ExtractMetadataAsync_RejectsUnsafeUnselectedEntryBeforeWriting(string name)
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			("extension.vsixmanifest", "<PackageManifest />"), (name, "unsafe"));

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None));

		Assert.Empty(Directory.GetFileSystemEntries(archive.Destination));
	}

	[Theory]
	[InlineData("content/file.dll", "content/file.dll")]
	[InlineData("content/file.dll", "CONTENT/FILE.dll")]
	[InlineData("content/file.dll", "content\\file.dll")]
	[InlineData("content/", "content")]
	[InlineData("content", "content/file.dll")]
	public async Task ExtractMetadataAsync_RejectsUnselectedDuplicateOrConflictingPaths(string first, string second)
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			("extension.vsixmanifest", "<PackageManifest />"), (first, "one"), (second, "two"));

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None));
		Assert.Empty(Directory.GetFileSystemEntries(archive.Destination));
	}

	[Fact]
	public async Task ExtractMetadataAsync_RejectsUnselectedCompressionBomb()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			("extension.vsixmanifest", "<PackageManifest />"), ("payload.dll", new string('a', 100_000)));

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None));
		Assert.Empty(Directory.GetFileSystemEntries(archive.Destination));
	}

	[Fact]
	public async Task ExtractMetadataAsync_PreservesLegacyNamespacedManifestAndMissingReferences()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			("extension.vsixmanifest", """
			<Vsix xmlns="http://schemas.microsoft.com/developer/vsx-schema/2010">
			  <Identifier Id="Legacy.Extension"><Name>Legacy</Name><Version>1.0</Version><Author>Publisher</Author>
				<Description>Legacy description</Description><Icon>missing.png</Icon><License>license.txt</License>
			  </Identifier>
			</Vsix>
			"""),
			("LICENSE.txt", "legacy license"), ("unused.dll", "unused"));

		await SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None);
		Package package = new VsixManifestParser().CreateFromManifest(archive.Destination, "", "", "");

		Assert.Equal("Legacy.Extension", package.ID);
		Assert.Equal("Legacy", package.Name);
		Assert.Equal("legacy license", package.License);
		Assert.Equal("missing.png", package.Icon);
		Assert.Equal(2, Directory.GetFiles(archive.Destination).Length);
	}

	[Fact]
	public async Task ExtractMetadataAsync_RejectsUnselectedExpandedSize()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			("payload.dll", "payload"), ("extension.vsixmanifest", "<PackageManifest />"));
		archive.SetFirstEntryDeclaredSize((uint)SafeArchiveExtractor.MaxExpandedBytes + 1);

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None));
		Assert.Empty(Directory.GetFileSystemEntries(archive.Destination));
	}

	[Fact]
	public async Task ExtractMetadataAsync_RejectsTooManyUnselectedEntries()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(
			Enumerable.Range(0, SafeArchiveExtractor.MaxEntries + 1).Select(index => ($"file{index}.dll", "")).ToArray());

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None));
		Assert.Empty(Directory.GetFileSystemEntries(archive.Destination));
	}

	[Fact]
	public async Task ExtractMetadataAsync_RejectsOversizedManifestBeforeWriting()
	{
		using TemporaryArchive archive = TemporaryArchive.Create(("extension.vsixmanifest", "<PackageManifest />"));
		archive.SetFirstEntryDeclaredSize((uint)VsixManifestParser.MaxManifestBytes + 1);

		await Assert.ThrowsAsync<InvalidDataException>(() =>
			SafeArchiveExtractor.ExtractMetadataAsync(archive.Path, archive.Destination, CancellationToken.None));
		Assert.Empty(Directory.GetFileSystemEntries(archive.Destination));
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

		public void SetFirstEntryDeclaredSize(uint size)
		{
			byte[] bytes = File.ReadAllBytes(Path);
			for (int offset = 0; offset <= bytes.Length - 28; offset++)
			{
				if (BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset)) == 0x02014b50)
				{
					BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 20), size);
					BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset + 24), size);
					File.WriteAllBytes(Path, bytes);
					return;
				}
			}
			throw new InvalidOperationException("ZIP central directory not found.");
		}

		public static TemporaryArchive Create(params (string Name, string Content)[] entries)
		{
			string root = System.IO.Path.Combine(AppContext.BaseDirectory, "archive-tests", Guid.NewGuid().ToString("N"));
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
