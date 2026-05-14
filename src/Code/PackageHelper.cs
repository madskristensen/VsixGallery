using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SkiaSharp;

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
		internal const string ManageFileName = "manage.json";

		private readonly string _extensionRoot;
		private readonly List<Package> _cache;
		private readonly bool _canRemoveOldExtensions;
		private readonly bool _canValidateLicenses;
		private readonly ILogger<PackageHelper> _logger;

		// Serializes upload/mutation work. PackageHelper is registered as a
		// singleton, but the cache and the on-disk extension folders are shared
		// mutable state. Concurrent CI uploads (very common, since many
		// extension repos publish to the same gallery) would otherwise race on
		// the _cache list and on Directory.Delete/Create of the same folder.
		private readonly SemaphoreSlim _uploadLock = new(1, 1);
		private readonly Lock _cacheLock = new();

		public PackageHelper(IWebHostEnvironment env, IOptions<ExtensionsOptions> options, ILogger<PackageHelper> logger)
		{
			_logger = logger;
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
			FileProvider = new PhysicalFileProvider(_extensionRoot);
			_cache = GetAllPackages();
		}

		public bool IsCustomExtensionPath { get; }

		public IFileProvider FileProvider { get; }

		public IReadOnlyList<Package> PackageCache
		{
			get {
				lock (_cacheLock)
				{
					return [.. _cache];
				}
			}
		}

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

		public void Validate(Package package)
		{
			List<string> errors = [];

			if (string.IsNullOrWhiteSpace(package.Icon))
				{
					errors.Add("Icon is missing. Must be 90x90 pixel PNG, GIF, JPEG, or WebP");
				}
				else if (!package.Icon.ToLowerInvariant().EndsWith(".png") &&
						 !package.Icon.ToLowerInvariant().EndsWith(".jpg") &&
						 !package.Icon.ToLowerInvariant().EndsWith(".gif") &&
						 !package.Icon.ToLowerInvariant().EndsWith(".webp"))
				{
					errors.Add("The icon must be 90x90 pixel PNG, GIF, JPEG, or WebP");
				}
			else
			{
				string iconFile = Path.Combine(_extensionRoot, package.ID!, package.Icon!);

				if (File.Exists(iconFile))
				{
					if (ImageDimensionReader.TryGetDimensions(iconFile, out int width, out int height))
					{
						package.IconWidth = width;
						package.IconHeight = height;

						if (width < 90 || height < 90 || width > 200 || height > 200)
						{
							errors.Add($"The icon is {width}x{height}px. It must be between 90x90 and 200x200 pixels");
						}
					}
				}
			}

			if (package.Description?.Length < 40)
			{
				errors.Add("Provide a clear description. Make sure to cover why it is great and what it does");
			}

			if (_canValidateLicenses && string.IsNullOrEmpty(package.License))
			{
				errors.Add("No license is specified in the .vsixmanifest");
			}

			package.Errors = errors;
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
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}

			lock (_cacheLock)
			{
				Package? cached = _cache.FirstOrDefault(p => p.ID == id);
				if (cached is not null)
				{
					return cached;
				}
			}

			string folder = Path.Combine(_extensionRoot, id);

			Package? package = DeserializePackage(folder);
			if (package is not null)
			{
				SetFileSize(package, folder);
			}
			return package;
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
			if (string.IsNullOrEmpty(id))
			{
				return null;
			}

			string folder = Path.Combine(_extensionRoot, id);
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

		public async Task<Package> ProcessVsix(IFormFile file, string repo, string issuetracker, string readmeUrl, string? manageToken = null)
		{
			if (file == null || file.Length == 0)
			{
				throw new InvalidOperationException("No .vsix file was included in the upload request.");
			}

			string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

			await _uploadLock.WaitAsync();
			try
			{
				string tempVsix = Path.Combine(tempFolder, "extension.vsix");

				if (!Directory.Exists(tempFolder))
				{
					Directory.CreateDirectory(tempFolder);
				}

				using (FileStream fileStream = new(tempVsix, FileMode.CreateNew))
				{
					await file.CopyToAsync(fileStream);
				}

				ZipFile.ExtractToDirectory(tempVsix, tempFolder);

				VsixManifestParser parser = new();
				Package package = parser.CreateFromManifest(tempFolder, repo, issuetracker, readmeUrl);

				string vsixFolder = Path.Combine(_extensionRoot, package.ID!);

				// Determine which manage token to use:
				//   - Re-publishing an existing extension: keep the existing token unless the
				//     publisher provides one that matches (rotation is an admin task).
				//   - New extension and publisher supplied a token: use it as-is.
				//   - New extension and no token supplied: auto-generate one and surface it
				//     to the publisher embedded in the manage URL so they can save it.
				ManageInfo? existing = LoadManageInfo(vsixFolder);
				bool tokenAutoGenerated = false;
				string effectiveToken;
				string tokenHashToPersist;

				if (existing?.TokenHash is not null)
				{
					if (!string.IsNullOrWhiteSpace(manageToken) && !TokenMatches(manageToken, existing.TokenHash))
					{
						throw new UnauthorizedAccessException(
							"This extension is already managed with a different token. " +
							"Provide the original manage token to re-publish.");
					}
					// Keep the original token; do not embed in the URL on republish.
					effectiveToken = string.Empty;
					tokenHashToPersist = existing.TokenHash;
				}
				else if (!string.IsNullOrWhiteSpace(manageToken))
				{
					effectiveToken = manageToken;
					tokenHashToPersist = HashToken(effectiveToken);
				}
				else
				{
					effectiveToken = GenerateToken();
					tokenAutoGenerated = true;
					tokenHashToPersist = HashToken(effectiveToken);
				}

				SavePackage(tempFolder, package, vsixFolder);
				Validate(package);
				Sanitize(package);

				File.Copy(tempVsix, Path.Combine(vsixFolder, "extension.vsix"), true);
				SetFileSize(package, vsixFolder);

				// Persist the manage token hash. SavePackage wipes the folder on
				// republish, so we always re-write manage.json afterwards.
				SaveManageInfo(vsixFolder, new ManageInfo { TokenHash = tokenHashToPersist });

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

				return package;
			}
			finally
			{
				try
				{
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
					string vsixFolder = Path.Combine(_extensionRoot, package.ID!);
					if (Directory.Exists(vsixFolder))
					{
						Directory.Delete(vsixFolder, true);
					}
					lock (_cacheLock)
					{
						_cache.Remove(package);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Failed to delete extension folder for package: {PackageId}", package.ID);
				}
			}
		}

		private void SavePackage(string tempFolder, Package package, string vsixFolder)
		{
			if (Directory.Exists(vsixFolder))
			{
				Directory.Delete(vsixFolder, true);
			}

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

			string json = JsonSerializer.Serialize(package, PackageJsonContext.Default.Package);

			File.WriteAllText(Path.Combine(vsixFolder, "extension.json"), json, Encoding.UTF8);

			lock (_cacheLock)
			{
				_cache.RemoveAll(p => p.ID == package.ID);
				_cache.Add(package);
			}
		}

		// Resizes the icon to 135x135 (1.5× the 90px display size) and encodes it as
		// lossless WebP so the image is sharp on 1.5× DPR screens without excessive
		// overhead on 1× screens.
		private static string? ProcessAndSaveIcon(string sourceIconPath, string vsixFolder, string version)
		{
			try
			{
				using SKBitmap source = SKBitmap.Decode(sourceIconPath);
				if (source == null) return null;

				const int IconSize = 135;
				SKImageInfo targetInfo = new(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul);
				using SKBitmap resized = source.Resize(targetInfo, new SKSamplingOptions(SKCubicResampler.Mitchell));
				if (resized == null) return null;

				using SKImage image = SKImage.FromBitmap(resized);
				using SKData data = image.Encode(SKEncodedImageFormat.Webp, 90); // lossy q90 — visually lossless for icons
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
			if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(token))
			{
				return false;
			}

			string folder = Path.Combine(_extensionRoot, id);
			ManageInfo? info = LoadManageInfo(folder);
			return info?.TokenHash is not null && TokenMatches(token, info.TokenHash);
		}

		/// <summary>
		/// Returns true when the given extension has a recorded manage token
		/// (i.e. it was uploaded after the manage feature shipped).
		/// </summary>
		public bool HasManageToken(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				return false;
			}

			string folder = Path.Combine(_extensionRoot, id);
			return LoadManageInfo(folder)?.TokenHash is not null;
		}

		/// <summary>
		/// Soft-deletes an extension by moving its folder into the <c>.trash</c>
		/// bin under the extension root. The package is removed from the
		/// in-memory cache so it disappears from listings immediately.
		/// </summary>
		public void SoftDelete(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				return;
			}

			string source = Path.Combine(_extensionRoot, id);
			if (!Directory.Exists(source))
			{
				return;
			}

			string trashRoot = Path.Combine(_extensionRoot, TrashFolderName);
			Directory.CreateDirectory(trashRoot);

			// Use a timestamp suffix so repeated deletions of the same id don't collide.
			string suffix = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
			string destination = Path.Combine(trashRoot, $"{id}__{suffix}");

			_uploadLock.Wait();
			try
			{
				Directory.Move(source, destination);

				lock (_cacheLock)
				{
					_cache.RemoveAll(p => p.ID == id);
				}
			}
			finally
			{
				_uploadLock.Release();
			}
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
					}
				}
			}
			finally
			{
				_uploadLock.Release();
			}

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