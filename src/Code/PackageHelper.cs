using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.OutputCaching;

using SkiaSharp;

using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VsixGallery
{
	public class PackageHelper
	{
		internal const string DefaultExtensionsPath = "extensions";
		internal const string TrashFolderName = ".trash";
		internal const string StagingFolderName = ".staging";
		internal const string RollbackFolderName = ".rollback";
		internal const string ManageFileName = "manage.json";
		internal const string GalleryCacheTag = "gallery";
		internal const string GalleryPageCachePolicy = "GalleryPage";
		internal const string GalleryGeneratedCachePolicy = "GalleryGenerated";

		private readonly string _extensionRoot;
		private readonly List<Package> _cache;
		private Package[] _packageSnapshot = [];
		private Package[] _listedPackageSnapshot = [];
		private Dictionary<string, Package> _packagesById = new(StringComparer.Ordinal);
		private Dictionary<string, Package[]> _packagesByAuthor = new(StringComparer.OrdinalIgnoreCase);
		private readonly bool _canRemoveOldExtensions;
		private readonly bool _canValidateLicenses;
		private readonly ILogger<PackageHelper> _logger;
		private readonly IOutputCacheStore _outputCacheStore;
		private readonly GalleryCacheVersion _galleryCacheVersion;

		// Serializes upload/mutation work. PackageHelper is registered as a
		// singleton, but the cache and the on-disk extension folders are shared
		// mutable state. Concurrent CI uploads (very common, since many
		// extension repos publish to the same gallery) would otherwise race on
		// the _cache list and on Directory.Delete/Create of the same folder.
		private readonly SemaphoreSlim _uploadLock = new(1, 1);
		private readonly Lock _cacheLock = new();

		public PackageHelper(
			IWebHostEnvironment env,
			IOptions<ExtensionsOptions> options,
			ILogger<PackageHelper> logger,
			IOutputCacheStore outputCacheStore,
			GalleryCacheVersion galleryCacheVersion)
		{
			_logger = logger;
			_outputCacheStore = outputCacheStore;
			_galleryCacheVersion = galleryCacheVersion;
			_canRemoveOldExtensions = options.Value.RemoveOldExtensions;
			_canValidateLicenses = options.Value.ValidateLicenses;
			_extensionRoot = options.Value.Directory ?? string.Empty;

			// Default to an "extensions" directory under the web root
			// path when a directory is not specified in the options.
			if (string.IsNullOrEmpty(_extensionRoot))
			{
				_extensionRoot = Path.Combine(env.WebRootPath, DefaultExtensionsPath);
			}
			else
			{
				IsCustomExtensionPath = true;
			}

			Directory.CreateDirectory(_extensionRoot);
			Directory.CreateDirectory(Path.Combine(_extensionRoot, TrashFolderName));
			Directory.CreateDirectory(Path.Combine(_extensionRoot, StagingFolderName));
			Directory.CreateDirectory(Path.Combine(_extensionRoot, RollbackFolderName));
			RecoverInterruptedChanges();
			FileProvider = new PhysicalFileProvider(_extensionRoot);
			_cache = GetAllPackages();
			lock (_cacheLock)
			{
				RebuildCacheIndexesLocked();
			}
			_logger.LogInformation(
				"Extension storage initialized with {PackageCount} package(s). Package mutations require a single application instance.",
				_cache.Count);
		}

		public bool IsCustomExtensionPath { get; }

		public IFileProvider FileProvider { get; }

		internal string ExtensionRoot => _extensionRoot;

		public IReadOnlyList<Package> PackageCache => Volatile.Read(ref _packageSnapshot);

		public IReadOnlyList<Package> ListedPackages => Volatile.Read(ref _listedPackageSnapshot);

		private List<Package> GetAllPackages()
		{
			List<Package> packages = [];

			if (!Directory.Exists(_extensionRoot))
			{
				return [.. packages];
			}

			foreach (string extension in Directory.EnumerateDirectories(_extensionRoot))
			{
				// Skip the soft-delete trash bin and any other dot-folders.
				string folderName = Path.GetFileName(extension);
				if (folderName.StartsWith('.'))
				{
					continue;
				}

				string json = Path.Combine(extension, "extension.json");
				if (File.Exists(json))
				{
					string content = File.ReadAllText(json);
					Package? package = JsonSerializer.Deserialize(content, PackageJsonContext.Default.Package);
					if (package is null)
					{
						continue;
					}
					Validate(package);
					Sanitize(package);
					SetFileSize(package, extension);
					packages.Add(package);
				}
			}

			return [.. packages.OrderByDescending(p => p.DatePublished)];
		}

		private static void Sanitize(Package package)
		{
			if (string.IsNullOrWhiteSpace(package.Icon))
			{
				package.Icon = "/img/defaulticon.svg";
			}
			else
			{
				package.Icon = $"/extensions/{package.ID}/{Uri.EscapeDataString(package.Icon ?? string.Empty)}";
			}

			if (!string.IsNullOrWhiteSpace(package.Repo) && !package.Repo.Contains("://"))
			{
				package.Repo = "https://" + package.Repo;
			}

			// Backfill Repo/IssueTracker/ReadmeUrl for legacy cached packages
			// whose extension.json was written before MoreInfoUrl-based
			// inference existed.
			VsixManifestParser.ApplyRepoFallback(package);
		}

		public void Validate(Package package, string? extensionFolder = null)
		{
			VsixManifestParser.ApplyRepoFallback(package);
			List<ValidationFinding> findings =
			[
				.. package.Validation.Where(finding =>
					finding.Code.StartsWith("url.input-", StringComparison.Ordinal)),
			];

			AddRequiredTextFinding(findings, package.Name, 200, "name", "display name");
			AddRequiredTextFinding(findings, package.Author, 200, "publisher", "publisher");
			AddRequiredTextFinding(findings, package.Version, 100, "version", "version");

			if (string.IsNullOrWhiteSpace(package.Icon))
			{
				AddFinding(findings, ValidationFinding.Warning(
					"icon.missing",
					"Icon is missing. Include a square PNG, GIF, or JPEG image. The preferred display size is 128x128 pixels; larger images are supported."));
			}
			else if (!package.Icon.ToLowerInvariant().EndsWith(".png") &&
					 !package.Icon.ToLowerInvariant().EndsWith(".jpg") &&
					 !package.Icon.ToLowerInvariant().EndsWith(".gif"))
			{
				AddFinding(findings, ValidationFinding.Warning(
					"icon.unsupported-format",
					"The icon must be a PNG, GIF, or JPEG image."));
			}
			else
			{
				string iconRoot = extensionFolder is null
					? PackagePath.GetContainedPath(_extensionRoot, package.ID!)
					: extensionFolder;
				string? iconFile = VsixManifestParser.ResolveRelativeFile(iconRoot, package.Icon);

				if (iconFile is null)
				{
					AddFinding(findings, ValidationFinding.Warning(
						"icon.file-missing",
						"The icon referenced by the manifest was not found in the VSIX package."));
				}
				else if (new FileInfo(iconFile).Length > 10_000_000)
				{
					AddFinding(findings, ValidationFinding.Warning(
						"icon.file-too-large",
						"The source icon is larger than 10 MB. Use a smaller optimized image."));
				}
				else
				{
					if (ImageDimensionReader.TryGetDimensions(iconFile, out int width, out int height))
					{
						package.IconWidth = width;
						package.IconHeight = height;

						if (width < 128 || height < 128)
						{
							AddFinding(findings, ValidationFinding.Warning(
								"icon.invalid-dimensions",
								$"The source icon is {width}x{height}px. For best results, use 128x128 pixels or larger. Larger images are supported but are never displayed above 128x128 pixels."));
						}

						if (width != height)
						{
							AddFinding(findings, ValidationFinding.Warning(
								"icon.not-square",
								$"The source icon is {width}x{height}px. Use a square image to avoid distortion."));
						}
					}
					else
					{
						AddFinding(findings, ValidationFinding.Warning(
							"icon.invalid-image",
							"The icon referenced by the manifest could not be decoded as an image."));
					}
				}
			}

			if (string.IsNullOrWhiteSpace(package.Description))
			{
				AddFinding(findings, ValidationFinding.Warning(
					"description.missing",
					"Provide a description that explains what the extension does."));
			}
			else if (package.Description.Length < 40)
			{
				AddFinding(findings, ValidationFinding.Warning(
					"description.too-short",
					"Provide a clearer description of at least 40 characters that explains what the extension does."));
			}
			else if (package.Description.Length > 4_000)
			{
				AddFinding(findings, ValidationFinding.Warning(
					"description.too-long",
					"The description exceeds 4,000 characters. Move detailed documentation into the README."));
			}

			if (_canValidateLicenses && string.IsNullOrEmpty(package.License))
			{
				AddFinding(findings, ValidationFinding.Warning(
					"license.missing",
					"No license is specified in the .vsixmanifest."));
			}

			ValidateHttpsUrl(findings, package.Repo, "repository");
			ValidateHttpsUrl(findings, package.IssueTracker, "issue-tracker");
			ValidateHttpsUrl(findings, package.ReadmeUrl, "readme");
			ValidateHttpsUrl(findings, package.MoreInfoUrl, "more-info");

			package.Validation = findings;
		}

		private static void AddRequiredTextFinding(
			List<ValidationFinding> findings,
			string? value,
			int maximumLength,
			string code,
			string label)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				AddFinding(findings, ValidationFinding.Warning(
					$"manifest.{code}-missing",
					$"The manifest {label} is missing or blank."));
			}
			else if (value.Length > maximumLength)
			{
				AddFinding(findings, ValidationFinding.Warning(
					$"manifest.{code}-too-long",
					$"The manifest {label} exceeds {maximumLength:N0} characters."));
			}
		}

		private static void ValidateHttpsUrl(
			List<ValidationFinding> findings,
			string? value,
			string code)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return;
			}

			if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
				(uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
				!string.IsNullOrEmpty(uri.UserInfo))
			{
				AddFinding(findings, ValidationFinding.Warning(
					$"url.{code}-invalid",
					$"The {code.Replace('-', ' ')} URL is invalid."));
			}
			else if (uri.Scheme != Uri.UriSchemeHttps)
			{
				AddFinding(findings, ValidationFinding.Warning(
					$"url.{code}-insecure",
					$"The {code.Replace('-', ' ')} URL uses HTTP. Use HTTPS instead."));
			}
		}

		private static void AddFinding(List<ValidationFinding> findings, ValidationFinding finding)
		{
			if (!findings.Any(existing =>
				string.Equals(existing.Code, finding.Code, StringComparison.Ordinal) &&
				string.Equals(existing.Message, finding.Message, StringComparison.Ordinal)))
			{
				findings.Add(finding);
			}
		}

		private static void SetFileSize(Package package, string extensionFolder)
		{
			string vsixPath = Path.Combine(extensionFolder, "extension.vsix");
			if (File.Exists(vsixPath))
			{
				package.FileSize = new FileInfo(vsixPath).Length;
			}
		}

		public Package? GetPackage(string? id)
		{
			if (!PackagePath.IsValidExtensionId(id))
			{
				return null;
			}

			lock (_cacheLock)
			{
				if (_packagesById.TryGetValue(id, out Package? cached))
				{
					return cached;
				}
			}

			string folder = PackagePath.GetContainedPath(_extensionRoot, id);

			Package? package = DeserializePackage(folder);
			if (package is not null)
			{
				SetFileSize(package, folder);
			}
			return package;
		}

		public IReadOnlyList<Package> GetPackagesByAuthor(string? author)
		{
			if (string.IsNullOrWhiteSpace(author))
			{
				return [];
			}

			lock (_cacheLock)
			{
				return _packagesByAuthor.TryGetValue(author, out Package[]? packages)
					? packages
					: [];
			}
		}

		private void RebuildCacheIndexesLocked()
		{
			Package[] packages = [.. _cache.OrderByDescending(p => p.DatePublished)];
			Package[] listedPackages = [.. packages.Where(p => !p.Unlisted)];
			_packagesById = packages
				.Where(p => p.ID is not null)
				.GroupBy(p => p.ID!, StringComparer.Ordinal)
				.ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
			_packagesByAuthor = listedPackages
				.Where(p => !string.IsNullOrWhiteSpace(p.Author))
				.GroupBy(p => p.Author!, StringComparer.OrdinalIgnoreCase)
				.ToDictionary(
					group => group.Key,
					group => group.ToArray(),
					StringComparer.OrdinalIgnoreCase);
			Volatile.Write(ref _listedPackageSnapshot, listedPackages);
			Volatile.Write(ref _packageSnapshot, packages);
		}

		public string? GetIconDiskPath(Package? package)
		{
			if (package == null || string.IsNullOrEmpty(package.Icon))
			{
				return null;
			}

			const string prefix = "/extensions/";
			if (!package.Icon.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
			{
				return null;
			}

			string rest = package.Icon.Substring(prefix.Length);
			int slash = rest.IndexOf('/');
			if (slash < 0)
			{
				return null;
			}

			string id = rest.Substring(0, slash);
			string fileName = Uri.UnescapeDataString(rest.Substring(slash + 1));
			string path = Path.Combine(_extensionRoot, id, fileName);
			return File.Exists(path) ? path : null;
		}

		public string? GetExtensionFolder(string? id)
		{
			if (!PackagePath.IsValidExtensionId(id))
			{
				return null;
			}

			string folder = PackagePath.GetContainedPath(_extensionRoot, id);
			return Directory.Exists(folder) ? folder : null;
		}

		private static Package? DeserializePackage(string folder)
		{
			string jsonPath = Path.Combine(folder, "extension.json");
			if (!File.Exists(jsonPath))
			{
				return null;
			}

			string content = File.ReadAllText(jsonPath);
			return JsonSerializer.Deserialize(content, PackageJsonContext.Default.Package);
		}

		public async Task<Package> ProcessVsix(
			IFormFile file,
			string repo,
			string issuetracker,
			string readmeUrl,
			string? manageToken = null,
			CancellationToken cancellationToken = default)
		{
			if (file == null || file.Length == 0)
			{
				throw new InvalidDataException("No .vsix file was included in the upload request.");
			}

			string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
			string? stagingFolder = null;
			long startedAt = Stopwatch.GetTimestamp();
			_logger.LogInformation("VSIX upload processing started for a {UploadBytes}-byte package.", file.Length);

			await _uploadLock.WaitAsync(cancellationToken);
			try
			{
				string tempVsix = Path.Combine(tempFolder, "extension.vsix");

				if (!Directory.Exists(tempFolder))
				{
					Directory.CreateDirectory(tempFolder);
				}

				await using (FileStream fileStream = new(tempVsix, FileMode.CreateNew))
				{
					await file.CopyToAsync(fileStream, cancellationToken);
				}

				await SafeArchiveExtractor.ExtractAsync(tempVsix, tempFolder, cancellationToken);

				VsixManifestParser parser = new();
				Package package = parser.CreateFromManifest(tempFolder, repo, issuetracker, readmeUrl);
				if (!PackagePath.IsValidExtensionId(package.ID))
				{
					throw new InvalidDataException("The extension ID contains unsupported characters.");
				}

				string vsixFolder = PackagePath.GetContainedPath(_extensionRoot, package.ID!);
				stagingFolder = Path.Combine(_extensionRoot, StagingFolderName, Guid.NewGuid().ToString("N"));

				// Determine which manage token to use:
				//   - Publisher supplied a token: store its hash. This (re)sets the
				//     manage password for the extension, even if a different token
				//     was previously on file. The token acts as a soft "don't let
				//     randos delete my listing" speed bump, not real auth — anyone
				//     who can publish under this ID can already overwrite it.
				//   - No token supplied and the extension already has one: keep the
				//     existing token so the publisher's saved value still works.
				//   - No token supplied and no token on file: auto-generate one and
				//     surface it embedded in the manage URL so the publisher can
				//     save it from the upload response.
				ManageInfo? existing = LoadManageInfo(vsixFolder);
				bool tokenAutoGenerated = false;
				string effectiveToken;
				string tokenHashToPersist;

				if (!string.IsNullOrWhiteSpace(manageToken))
				{
					effectiveToken = manageToken;
					tokenHashToPersist = HashToken(effectiveToken);
				}
				else if (existing?.TokenHash is not null)
				{
					// Keep the original token; do not embed in the URL on republish.
					effectiveToken = string.Empty;
					tokenHashToPersist = existing.TokenHash;
				}
				else
				{
					effectiveToken = GenerateToken();
					tokenAutoGenerated = true;
					tokenHashToPersist = HashToken(effectiveToken);
				}

				Validate(package, tempFolder);
				PreparePackageFolder(tempFolder, package, stagingFolder);
				await CopyFileAsync(tempVsix, Path.Combine(stagingFolder, "extension.vsix"), cancellationToken);
				SetFileSize(package, stagingFolder);
				package.Sha256 = await ComputeSha256Async(tempVsix, cancellationToken);

				// Persist the manage token hash in the prepared replacement folder.
				SaveManageInfo(stagingFolder, new ManageInfo { TokenHash = tokenHashToPersist });
				SavePackageMetadata(stagingFolder, package);

				string rollbackFolder = Path.Combine(_extensionRoot, RollbackFolderName, package.ID!);
				if (!AtomicDirectory.Replace(stagingFolder, vsixFolder, rollbackFolder))
				{
					_logger.LogWarning(
						"Published extension {ExtensionId}, but its rollback folder could not be removed.",
						package.ID);
				}
				stagingFolder = null;

				Sanitize(package);
				Package cachedPackage = DeserializePackage(vsixFolder)
					?? throw new InvalidDataException("The published extension metadata could not be loaded.");
				Sanitize(cachedPackage);
				SetFileSize(cachedPackage, vsixFolder);
				lock (_cacheLock)
				{
					_cache.RemoveAll(p => p.ID == package.ID);
					_cache.Add(cachedPackage);
					RebuildCacheIndexesLocked();
				}
				_galleryCacheVersion.Increment();
				await _outputCacheStore.EvictByTagAsync(GalleryCacheTag, CancellationToken.None);

				// Build the manage URL that the upload response will surface.
				if (tokenAutoGenerated)
				{
					package.ManageUrl = $"{package.ManagePageLink}?token={Uri.EscapeDataString(effectiveToken)}";
					package.ManageTokenIncludedInUrl = true;
				}
				else
				{
					package.ManageUrl = package.ManagePageLink;
					package.ManageTokenIncludedInUrl = false;
				}

				_logger.LogInformation(
					"Published extension {ExtensionId} version {Version} ({UploadBytes} bytes) in {ElapsedMilliseconds} ms.",
					package.ID,
					package.Version,
					file.Length,
					Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
				return package;
			}
			finally
			{
				try
				{
					if (stagingFolder is not null && Directory.Exists(stagingFolder))
					{
						Directory.Delete(stagingFolder, true);
					}

					if (Directory.Exists(tempFolder))
					{
						Directory.Delete(tempFolder, true);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to delete temp folder: {TempFolder}", tempFolder);
				}

				try
				{
					RemoveOldExtensions();
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to remove old extensions");
				}

				_uploadLock.Release();
			}
		}

		private void RemoveOldExtensions()
		{
			if (!_canRemoveOldExtensions)
			{
				return;
			}

			Package[] oldPackages;
			lock (_cacheLock)
			{
				oldPackages = [.. _cache.Where(p => p.DatePublished < DateTime.UtcNow.AddMonths(-18))];
			}

			foreach (Package package in oldPackages)
			{
				try
				{
					if (!PackagePath.IsValidExtensionId(package.ID))
					{
						continue;
					}

					string vsixFolder = PackagePath.GetContainedPath(_extensionRoot, package.ID!);
					if (Directory.Exists(vsixFolder))
					{
						MoveToTrash(package.ID!, vsixFolder);
					}
					lock (_cacheLock)
					{
						_cache.Remove(package);
						RebuildCacheIndexesLocked();
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to delete extension folder for package: {PackageId}", package.ID);
				}
			}
		}

		private void RecoverInterruptedChanges()
		{
			DateTime abandonedBefore = DateTime.UtcNow.AddMinutes(-5);
			string stagingRoot = Path.Combine(_extensionRoot, StagingFolderName);
			foreach (string folder in Directory.EnumerateDirectories(stagingRoot))
			{
				if (Directory.GetLastWriteTimeUtc(folder) > abandonedBefore)
				{
					continue;
				}

				try
				{
					Directory.Delete(folder, recursive: true);
				}
				catch (Exception ex)
				{
					_logger.LogWarning(ex, "Could not remove abandoned staging folder {Folder}.", folder);
				}
			}

			string rollbackRoot = Path.Combine(_extensionRoot, RollbackFolderName);
			foreach (string rollbackFolder in Directory.EnumerateDirectories(rollbackRoot))
			{
				string id = Path.GetFileName(rollbackFolder);
				if (!PackagePath.IsValidExtensionId(id))
				{
					_logger.LogError("Ignoring rollback folder with invalid extension ID: {Folder}.", rollbackFolder);
					continue;
				}

				string liveFolder = PackagePath.GetContainedPath(_extensionRoot, id);
				try
				{
					if (!Directory.Exists(liveFolder))
					{
						Directory.Move(rollbackFolder, liveFolder);
						_logger.LogWarning("Recovered interrupted publication for extension {ExtensionId}.", id);
					}
					else if (Directory.GetLastWriteTimeUtc(rollbackFolder) <= abandonedBefore)
					{
						Directory.Delete(rollbackFolder, recursive: true);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Could not recover interrupted publication for extension {ExtensionId}.", id);
				}
			}
		}

		private static void PreparePackageFolder(string tempFolder, Package package, string vsixFolder)
		{
			Directory.CreateDirectory(vsixFolder);

			string? icon = VsixManifestParser.ResolveRelativeFile(tempFolder, package.Icon);
			if (icon != null)
			{
				string? processedIcon = ProcessAndSaveIcon(icon, vsixFolder, package.Version!);
				if (processedIcon != null)
				{
					package.Icon = processedIcon;
				}
				else
				{
					// Fallback: copy original if SkiaSharp processing fails.
					File.Copy(icon, Path.Combine(vsixFolder, "icon-" + package.Version + ".png"), true);
					package.Icon = "icon-" + package.Version + ".png";
				}
			}

		}

		private static void SavePackageMetadata(string vsixFolder, Package package)
		{
			string json = JsonSerializer.Serialize(package, PackageJsonContext.Default.Package);
			File.WriteAllText(Path.Combine(vsixFolder, "extension.json"), json, Encoding.UTF8);
		}

		private static async Task CopyFileAsync(string sourcePath, string destinationPath, CancellationToken cancellationToken)
		{
			await using FileStream source = new(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read);
			await using FileStream destination = new(destinationPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
			await source.CopyToAsync(destination, cancellationToken);
		}

		private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
		{
			await using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
			byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
			return Convert.ToHexStringLower(hash);
		}

		// Resizes the icon to 135x135 (1.5× the 90px display size) and encodes it as
		// lossless WebP so the image is sharp on 1.5× DPR screens without excessive
		// overhead on 1× screens.
		private static string? ProcessAndSaveIcon(string sourceIconPath, string vsixFolder, string version)
		{
			try
			{
				const int MaxIconFileBytes = 10_000_000;
				const int MaxIconDimension = 4_096;
				if (new FileInfo(sourceIconPath).Length > MaxIconFileBytes)
				{
					return null;
				}

				using SKCodec codec = SKCodec.Create(sourceIconPath);
				if (codec == null ||
					codec.Info.Width <= 0 ||
					codec.Info.Height <= 0 ||
					codec.Info.Width > MaxIconDimension ||
					codec.Info.Height > MaxIconDimension)
				{
					return null;
				}

				using SKBitmap source = SKBitmap.Decode(codec);
				if (source == null)
				{
					return null;
				}

				const int IconSize = 135;
				SKImageInfo targetInfo = new(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul);
				using SKBitmap resized = source.Resize(targetInfo, new SKSamplingOptions(SKCubicResampler.Mitchell));
				if (resized == null) return null;

				using SKImage image = SKImage.FromBitmap(resized);
				using SKData data = image.Encode(SKEncodedImageFormat.Webp, 80); // lossy q80 — smaller files, still visually good for icons
				if (data == null) return null;

				string fileName = $"icon-{version}.webp";
				File.WriteAllBytes(Path.Combine(vsixFolder, fileName), data.ToArray());
				return fileName;
			}
			catch
			{
				return null;
			}
		}

		// ---- Manage token / soft delete ----

		/// <summary>
		/// Generates a URL-safe random token (192 bits of entropy) suitable
		/// for use as a manage password.
		/// </summary>
		private static string GenerateToken()
		{
			Span<byte> bytes = stackalloc byte[24];
			RandomNumberGenerator.Fill(bytes);
			return Convert.ToBase64String(bytes)
				.Replace('+', '-')
				.Replace('/', '_')
				.TrimEnd('=');
		}

		private static string HashToken(string token)
		{
			byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
			return Convert.ToBase64String(hash);
		}

		private static bool TokenMatches(string token, string storedHash)
		{
			if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(storedHash))
			{
				return false;
			}

			byte[] expected = Convert.FromBase64String(storedHash);
			byte[] actual = SHA256.HashData(Encoding.UTF8.GetBytes(token));
			return CryptographicOperations.FixedTimeEquals(expected, actual);
		}

		private static ManageInfo? LoadManageInfo(string vsixFolder)
		{
			string path = Path.Combine(vsixFolder, ManageFileName);
			if (!File.Exists(path))
			{
				return null;
			}

			try
			{
				string content = File.ReadAllText(path);
				return JsonSerializer.Deserialize(content, PackageJsonContext.Default.ManageInfo);
			}
			catch
			{
				return null;
			}
		}

		private static void SaveManageInfo(string vsixFolder, ManageInfo info)
		{
			string path = Path.Combine(vsixFolder, ManageFileName);
			string json = JsonSerializer.Serialize(info, PackageJsonContext.Default.ManageInfo);
			File.WriteAllText(path, json, Encoding.UTF8);
		}

		/// <summary>
		/// Returns true when the supplied token matches the stored hash for
		/// the given extension. Returns false when the extension does not
		/// exist or has no manage token recorded (legacy uploads).
		/// </summary>
		public bool ValidateManageToken(string id, string? token)
		{
			if (!PackagePath.IsValidExtensionId(id) || string.IsNullOrWhiteSpace(token))
			{
				return false;
			}

			string folder = PackagePath.GetContainedPath(_extensionRoot, id);
			ManageInfo? info = LoadManageInfo(folder);
			return info?.TokenHash is not null && TokenMatches(token, info.TokenHash);
		}

		/// <summary>
		/// Returns true when the given extension has a recorded manage token
		/// (i.e. it was uploaded after the manage feature shipped).
		/// </summary>
		public bool HasManageToken(string id)
		{
			if (!PackagePath.IsValidExtensionId(id))
			{
				return false;
			}

			string folder = PackagePath.GetContainedPath(_extensionRoot, id);
			return LoadManageInfo(folder)?.TokenHash is not null;
		}

		/// <summary>
		/// Soft-deletes an extension by moving its folder into the <c>.trash</c>
		/// bin under the extension root. The package is removed from the
		/// in-memory cache so it disappears from listings immediately.
		/// </summary>
		public void SoftDelete(string id)
		{
			if (!PackagePath.IsValidExtensionId(id))
			{
				return;
			}

			string source = PackagePath.GetContainedPath(_extensionRoot, id);
			if (!Directory.Exists(source))
			{
				return;
			}

			_uploadLock.Wait();
			try
			{
				MoveToTrash(id, source);
			}
			finally
			{
				_uploadLock.Release();
			}
		}

		private void MoveToTrash(string id, string source)
		{
			string trashRoot = Path.Combine(_extensionRoot, TrashFolderName);
			Directory.CreateDirectory(trashRoot);

			string suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
			string destination = Path.Combine(trashRoot, $"{id}__{Guid.NewGuid():N}__{suffix}");

			Directory.Move(source, destination);

			lock (_cacheLock)
			{
				_cache.RemoveAll(p => p.ID == id);
				RebuildCacheIndexesLocked();
			}
			_galleryCacheVersion.Increment();
			EvictGalleryCache();
			_logger.LogInformation("Moved extension {ExtensionId} to trash.", id);
		}

		// ---- Admin: trash inspection, restore, hard-delete, purge ----

		/// <summary>
		/// Returns the soft-deleted extensions currently sitting in the trash bin.
		/// The folder name encodes both the original extension id and the deletion
		/// timestamp ("{id}__yyyyMMddHHmmss"), so admins can act on each entry
		/// individually.
		/// </summary>
		public IReadOnlyList<TrashedPackage> ListTrash()
		{
			string trashRoot = Path.Combine(_extensionRoot, TrashFolderName);
			if (!Directory.Exists(trashRoot))
			{
				return [];
			}

			List<TrashedPackage> result = [];

			foreach (string folder in Directory.EnumerateDirectories(trashRoot))
			{
				string folderName = Path.GetFileName(folder);
				DateTime? deletedAt = TryParseTrashTimestamp(folderName);

				Package? package = DeserializePackage(folder);
				if (package is null)
				{
					// Surface the folder name even if extension.json is missing,
					// so admins can still hard-delete it.
					string id = folderName.Contains("__", StringComparison.Ordinal)
						? folderName[..folderName.IndexOf("__", StringComparison.Ordinal)]
						: folderName;
					package = new Package { ID = id, Name = id };
				}
				else
				{
					Sanitize(package);
					SetFileSize(package, folder);
				}

				result.Add(new TrashedPackage(package, deletedAt) { TrashFolder = folderName });
			}

			return [.. result.OrderByDescending(t => t.DeletedAt ?? DateTime.MinValue)];
		}

		/// <summary>
		/// Restores a soft-deleted extension by moving its trash folder back to
		/// the live extension root. Refuses to overwrite a live extension that
		/// already exists with the same id.
		/// </summary>
		public bool Restore(string trashFolderName)
		{
			if (string.IsNullOrWhiteSpace(trashFolderName) || !IsSafeTrashFolderName(trashFolderName))
			{
				return false;
			}

			string source = Path.Combine(_extensionRoot, TrashFolderName, trashFolderName);
			if (!Directory.Exists(source))
			{
				return false;
			}

			string id = trashFolderName.Contains("__", StringComparison.Ordinal)
				? trashFolderName[..trashFolderName.IndexOf("__", StringComparison.Ordinal)]
				: trashFolderName;

			string destination = Path.Combine(_extensionRoot, id);
			if (Directory.Exists(destination))
			{
				return false;
			}

			_uploadLock.Wait();
			try
			{
				Directory.Move(source, destination);

				Package? restored = DeserializePackage(destination);
				if (restored is not null)
				{
					Validate(restored);
					Sanitize(restored);
					SetFileSize(restored, destination);

					lock (_cacheLock)
					{
						_cache.RemoveAll(p => p.ID == restored.ID);
						_cache.Add(restored);
						RebuildCacheIndexesLocked();
					}
				}
			}
			finally
			{
				_uploadLock.Release();
			}

			_galleryCacheVersion.Increment();
			EvictGalleryCache();
			_logger.LogInformation("Restored extension {ExtensionId} from trash.", id);
			return true;
		}

		/// <summary>
		/// Permanently deletes a folder from the trash bin. No-op if the
		/// folder doesn't exist or the name escapes the trash directory.
		/// </summary>
		public bool HardDelete(string trashFolderName)
		{
			if (string.IsNullOrWhiteSpace(trashFolderName) || !IsSafeTrashFolderName(trashFolderName))
			{
				return false;
			}

			string folder = Path.Combine(_extensionRoot, TrashFolderName, trashFolderName);
			if (!Directory.Exists(folder))
			{
				return false;
			}

			_uploadLock.Wait();
			try
			{
				Directory.Delete(folder, true);
			}
			finally
			{
				_uploadLock.Release();
			}

			_logger.LogInformation("Permanently deleted trash entry {TrashEntry}.", trashFolderName);
			return true;
		}

		/// <summary>
		/// Permanently deletes trash entries older than the supplied cutoff.
		/// Returns the number of folders removed. Used by the cleanup service.
		/// </summary>
		public int PurgeOlderThan(DateTime cutoffUtc)
		{
			string trashRoot = Path.Combine(_extensionRoot, TrashFolderName);
			if (!Directory.Exists(trashRoot))
			{
				return 0;
			}

			int purged = 0;

			foreach (string folder in Directory.EnumerateDirectories(trashRoot))
			{
				string folderName = Path.GetFileName(folder);
				DateTime? deletedAt = TryParseTrashTimestamp(folderName);

				// If we can't parse the timestamp, fall back to the directory's
				// last-write time so corrupt entries still age out eventually.
				DateTime effectiveDeletedAt = deletedAt ?? Directory.GetLastWriteTimeUtc(folder);

				if (effectiveDeletedAt > cutoffUtc)
				{
					continue;
				}

				try
				{
					Directory.Delete(folder, true);
					purged++;
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to purge trash folder: {Folder}", folder);
				}
			}

			return purged;
		}

		private static bool IsSafeTrashFolderName(string folderName)
		{
			// Reject anything with directory separators or relative segments
			// so an admin can never escape the trash directory via this API.
			return folderName.IndexOfAny(['/', '\\']) < 0
				&& folderName != "."
				&& folderName != "..";
		}

		private void EvictGalleryCache()
		{
			_outputCacheStore
				.EvictByTagAsync(GalleryCacheTag, CancellationToken.None)
				.AsTask()
				.GetAwaiter()
				.GetResult();
		}

		private static DateTime? TryParseTrashTimestamp(string folderName)
		{
			int separator = folderName.LastIndexOf("__", StringComparison.Ordinal);
			if (separator < 0 || separator + 2 >= folderName.Length)
			{
				return null;
			}

			string timestamp = folderName[(separator + 2)..];
			if (DateTime.TryParseExact(
					timestamp,
					"yyyyMMddHHmmss",
					System.Globalization.CultureInfo.InvariantCulture,
					System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
					out DateTime parsed))
			{
				return DateTime.SpecifyKind(parsed, DateTimeKind.Utc);
			}

			return null;
		}
	}
}