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
		TrashedPackage trashed = Assert.Single(gallery.Helper.ListTrash());
		Assert.Equal("Example.Extension", trashed.Package.ID);

		Assert.True(gallery.Helper.Restore(trashed.TrashFolder));
		Assert.NotNull(gallery.Helper.GetPackage("Example.Extension"));
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
