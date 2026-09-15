using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

using System.Security.Cryptography;

namespace VsixGallery.Tests;

public class PackageHelperTests
{
	[Fact]
	public void ValidationRules_HaveUniqueCodesAndJustifications()
	{
		Assert.Equal(
			ValidationRules.All.Count,
			ValidationRules.All.Select(rule => rule.Code).Distinct(StringComparer.Ordinal).Count());
		Assert.All(ValidationRules.All, rule =>
		{
			Assert.False(string.IsNullOrWhiteSpace(rule.Requirement));
			Assert.False(string.IsNullOrWhiteSpace(rule.Justification));
		});
	}

	[Fact]
	public async Task ProcessVsix_PublishesPackageAndInvalidatesGalleryCache()
	{
		const string id = "Example.Extension";
		using TemporaryGallery gallery = new();
		byte[] vsix = TestVsix.Create(id, "1.2.3");
		using MemoryStream stream = new(vsix);
		FormFile file = new(stream, 0, stream.Length, "file", "example.vsix");

		Package package = await gallery.Helper.ProcessVsix(
			file,
			"https://github.com/example/project",
			"issues",
			string.Empty,
			cancellationToken: CancellationToken.None);

		Assert.Equal(id, package.ID);
		Assert.Equal("1.2.3", package.Version);
		Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(vsix)), package.Sha256);
		Assert.True(package.ManageTokenIncludedInUrl);
		Assert.StartsWith("/extension/Example.Extension/manage?token=", package.ManageUrl);
		Assert.Equal(1, gallery.CacheVersion.Value);
		Assert.Equal([PackageHelper.GalleryCacheTag], gallery.OutputCache.EvictedTags);

		string packageFolder = Path.Combine(gallery.Root, "Example.Extension");
		Assert.True(File.Exists(Path.Combine(packageFolder, "extension.vsix")));
		string metadata = File.ReadAllText(Path.Combine(packageFolder, "extension.json"));
		Assert.DoesNotContain("ManageUrl", metadata, StringComparison.Ordinal);
		Assert.DoesNotContain("ManageTokenIncludedInUrl", metadata, StringComparison.Ordinal);

		string token = Uri.UnescapeDataString(package.ManageUrl!.Split("?token=", 2)[1]);
		Assert.True(gallery.Helper.ValidateManageToken(id, token));
	}

	[Fact]
	public async Task ProcessVsix_ValidatesOriginalIconBeforeResizing()
	{
		using TemporaryGallery gallery = new();
		byte[] onePixelPng = Convert.FromBase64String(
			"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
		byte[] vsix = TestVsix.Create(
			"Small.Icon",
			"1.0",
			iconPath: "icon.png",
			iconBytes: onePixelPng);
		using MemoryStream stream = new(vsix);
		FormFile file = new(stream, 0, stream.Length, "file", "small-icon.vsix");

		Package package = await gallery.Helper.ProcessVsix(
			file,
			string.Empty,
			string.Empty,
			string.Empty,
			cancellationToken: CancellationToken.None);

		Assert.Contains(package.Validation, finding => finding.Code == "icon.invalid-dimensions");
	}

	[Fact]
	public async Task ProcessVsix_ResolvesWindowsStyleManifestIconPath()
	{
		using TemporaryGallery gallery = new();
		byte[] onePixelPng = Convert.FromBase64String(
			"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");
		byte[] vsix = TestVsix.Create(
			"Nested.Icon",
			"1.0",
			iconPath: @"Resources\Icon.png",
			iconBytes: onePixelPng);
		using MemoryStream stream = new(vsix);
		FormFile file = new(stream, 0, stream.Length, "file", "nested-icon.vsix");

		Package package = await gallery.Helper.ProcessVsix(
			file,
			string.Empty,
			string.Empty,
			string.Empty,
			cancellationToken: CancellationToken.None);

		Assert.DoesNotContain(package.Validation, finding => finding.Code == "icon.file-missing");
	}

	[Fact]
	public void Validate_AllowsHighResolutionIcon()
	{
		using TemporaryGallery gallery = new();
		string iconPath = Path.Combine(gallery.Root, "icon.png");
		byte[] pngHeader = new byte[24];
		new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(pngHeader, 0);
		pngHeader[18] = 0x01;
		pngHeader[22] = 0x01;
		File.WriteAllBytes(iconPath, pngHeader);
		Package package = new()
		{
			ID = "Example.Extension",
			Name = "Example Extension",
			Author = "Example Publisher",
			Version = "1.0",
			Description = "A sufficiently detailed extension description for validation.",
			Icon = "icon.png",
		};

		gallery.Helper.Validate(package, gallery.Root);

		Assert.Equal(256, package.IconWidth);
		Assert.Equal(256, package.IconHeight);
		Assert.DoesNotContain(package.Validation, finding =>
			finding.Code == "icon.invalid-dimensions");
	}

	[Fact]
	public void Validate_RejectsWebpIcon()
	{
		using TemporaryGallery gallery = new();
		Package package = new()
		{
			ID = "Example.Extension",
			Name = "Example Extension",
			Author = "Example Publisher",
			Version = "1.0",
			Description = "A sufficiently detailed extension description for validation.",
			Icon = "icon.webp",
		};

		gallery.Helper.Validate(package, gallery.Root);

		Assert.Contains(package.Validation, finding =>
			finding.Code == "icon.unsupported-format");
	}

	[Fact]
	public void Validate_ReturnsStructuredManifestAndUrlWarnings()
	{
		using TemporaryGallery gallery = new();
		Package package = new()
		{
			ID = "Example.Extension",
			Name = " ",
			Author = "",
			Version = "1.0",
			Description = " ",
			Repo = "http://example.com/project",
		};

		gallery.Helper.Validate(package, gallery.Root);

		Assert.Contains(package.Validation, finding =>
			finding is { Severity: "warning", Code: "manifest.name-missing" });
		Assert.Contains(package.Validation, finding => finding.Code == "manifest.publisher-missing");
		Assert.Contains(package.Validation, finding => finding.Code == "description.missing");
		Assert.Contains(package.Validation, finding => finding.Code == "url.repository-insecure");
	}

	[Fact]
	public void Validate_NormalizesRelativeIssueTrackerAndRemovesStaleWarning()
	{
		using TemporaryGallery gallery = new();
		Package package = new()
		{
			ID = "Example.Extension",
			Name = "Example Extension",
			Author = "Example Publisher",
			Version = "1.0",
			Description = "A sufficiently detailed extension description for validation.",
			Repo = "https://github.com/example/project",
			IssueTracker = "issues/",
			Validation =
			[
				ValidationFinding.Warning(
					"url.issue-tracker-invalid",
					"The issue tracker URL is invalid."),
			],
		};

		gallery.Helper.Validate(package, gallery.Root);

		Assert.Equal("https://github.com/example/project/issues/", package.IssueTracker);
		Assert.DoesNotContain(package.Validation, finding =>
			finding.Code == "url.issue-tracker-invalid");
	}

	[Fact]
	public async Task ProcessVsix_RepublishPreservesExistingManageToken()
	{
		using TemporaryGallery gallery = new();
		const string token = "publisher-secret";

		await gallery.PublishAsync("Example.Extension", "1.0", token);
		Package updated = await gallery.PublishAsync("Example.Extension", "2.0", manageToken: null);

		Assert.Equal("2.0", gallery.Helper.GetPackage("Example.Extension")!.Version);
		Assert.True(gallery.Helper.ValidateManageToken("Example.Extension", token));
		Assert.False(updated.ManageTokenIncludedInUrl);
		Assert.Equal("/extension/Example.Extension/manage", updated.ManageUrl);
		Assert.Null(gallery.Helper.GetPackage("Example.Extension")!.ManageUrl);
		Assert.Same(gallery.Helper.PackageCache, gallery.Helper.PackageCache);
		Package authorPackage = Assert.Single(gallery.Helper.GetPackagesByAuthor("example publisher"));
		Assert.Equal("Example.Extension", authorPackage.ID);
		Assert.Equal(2, gallery.CacheVersion.Value);
		Assert.Empty(Directory.EnumerateDirectories(
			Path.Combine(gallery.Root, PackageHelper.StagingFolderName)));
		Assert.Empty(Directory.EnumerateDirectories(
			Path.Combine(gallery.Root, PackageHelper.RollbackFolderName)));
	}

	[Fact]
	public async Task TrashLifecycle_RemovesRestoresAndPermanentlyDeletesPackage()
	{
		using TemporaryGallery gallery = new();
		await gallery.PublishAsync("Example.Extension", "1.0", "publisher-secret");

		gallery.Helper.SoftDelete("Example.Extension");

		Assert.Null(gallery.Helper.GetPackage("Example.Extension"));
		Assert.Empty(gallery.Helper.GetPackagesByAuthor("Example Publisher"));
		TrashedPackage trashed = Assert.Single(gallery.Helper.ListTrash());
		Assert.Equal("Example.Extension", trashed.Package.ID);

		Assert.True(gallery.Helper.Restore(trashed.TrashFolder));
		Assert.NotNull(gallery.Helper.GetPackage("Example.Extension"));
		Assert.Single(gallery.Helper.GetPackagesByAuthor("EXAMPLE PUBLISHER"));
		Assert.True(gallery.Helper.ValidateManageToken("Example.Extension", "publisher-secret"));

		gallery.Helper.SoftDelete("Example.Extension");
		TrashedPackage deletedAgain = Assert.Single(gallery.Helper.ListTrash());
		Assert.True(gallery.Helper.HardDelete(deletedAgain.TrashFolder));
		Assert.Empty(gallery.Helper.ListTrash());
		Assert.False(gallery.Helper.Restore("../Example.Extension"));
		Assert.Equal(4, gallery.CacheVersion.Value);
	}

	private sealed class TemporaryGallery : IDisposable
	{
		public TemporaryGallery()
		{
			Root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Root);
			OutputCache = new RecordingOutputCacheStore();
			CacheVersion = new GalleryCacheVersion();
			Helper = new PackageHelper(
				new TestWebHostEnvironment(Root),
				Options.Create(new ExtensionsOptions
				{
					Directory = Root,
					RemoveOldExtensions = false,
					ValidateLicenses = false,
				}),
				NullLogger<PackageHelper>.Instance,
				OutputCache,
				CacheVersion);
		}

		public string Root { get; }
		public PackageHelper Helper { get; }
		public RecordingOutputCacheStore OutputCache { get; }
		public GalleryCacheVersion CacheVersion { get; }

		public async Task<Package> PublishAsync(string id, string version, string? manageToken)
		{
			byte[] vsix = TestVsix.Create(id, version);
			using MemoryStream stream = new(vsix);
			FormFile file = new(stream, 0, stream.Length, "file", $"{id}.vsix");
			return await Helper.ProcessVsix(
				file,
				string.Empty,
				string.Empty,
				string.Empty,
				manageToken,
				CancellationToken.None);
		}

		public void Dispose()
		{
			(Helper.FileProvider as IDisposable)?.Dispose();
			Directory.Delete(Root, recursive: true);
		}
	}

	private sealed class RecordingOutputCacheStore : IOutputCacheStore
	{
		public List<string> EvictedTags { get; } = [];

		public ValueTask<byte[]?> GetAsync(string key, CancellationToken cancellationToken) =>
			ValueTask.FromResult<byte[]?>(null);

		public ValueTask SetAsync(
			string key,
			byte[] value,
			string[]? tags,
			TimeSpan validFor,
			CancellationToken cancellationToken) =>
			ValueTask.CompletedTask;

		public ValueTask EvictByTagAsync(string tag, CancellationToken cancellationToken)
		{
			EvictedTags.Add(tag);
			return ValueTask.CompletedTask;
		}
	}

	private sealed class TestWebHostEnvironment(string root) : IWebHostEnvironment
	{
		public string ApplicationName { get; set; } = "VsixGallery.Tests";
		public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
		public string ContentRootPath { get; set; } = root;
		public string EnvironmentName { get; set; } = "Test";
		public string WebRootPath { get; set; } = root;
		public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
	}
}
