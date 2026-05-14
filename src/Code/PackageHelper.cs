using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using SkiaSharp;

using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace VsixGallery
{
	public class PackageHelper
	{
		internal const string DefaultExtensionsPath = "extensions";

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

		public async Task<Package> ProcessVsix(IFormFile file, string repo, string issuetracker, string readmeUrl)
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

				SavePackage(tempFolder, package, vsixFolder);
				Validate(package);
				Sanitize(package);

				File.Copy(tempVsix, Path.Combine(vsixFolder, "extension.vsix"), true);
				SetFileSize(package, vsixFolder);

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
	}
}