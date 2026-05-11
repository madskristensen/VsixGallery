using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VsixGallery
{
	public class PackageHelper
	{
		internal const string DefaultExtensionsPath = "extensions";

		private readonly string _extensionRoot;
		private readonly List<Package> _cache;
		private readonly bool _canRemoveOldExtensions;
		private readonly bool _canValidateLicenses;

		// Serializes upload/mutation work. PackageHelper is registered as a
		// singleton, but the cache and the on-disk extension folders are shared
		// mutable state. Concurrent CI uploads (very common, since many
		// extension repos publish to the same gallery) would otherwise race on
		// the _cache list and on Directory.Delete/Create of the same folder.
		private readonly SemaphoreSlim _uploadLock = new(1, 1);
		private readonly Lock _cacheLock = new();

		public PackageHelper(IWebHostEnvironment env, IOptions<ExtensionsOptions> options)
		{
			_canRemoveOldExtensions = options.Value.RemoveOldExtensions;
			_canValidateLicenses = options.Value.ValidateLicenses;
			_extensionRoot = options.Value.Directory;

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
					Package package = JsonSerializer.Deserialize<Package>(content);
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
				package.Icon = $"/extensions/{package.ID}/{Uri.EscapeDataString(package.Icon)}";
			}

			if (!string.IsNullOrWhiteSpace(package.Repo) && !package.Repo.Contains("://"))
			{
				package.Repo = "https://" + package.Repo;
			}
		}

		public void Validate(Package package)
		{
			List<string> errors = [];

			if (string.IsNullOrWhiteSpace(package.Icon))
			{
				errors.Add("Icon is missing. Must be 90x90 pixel PNG, GIF, or JPEG");
			}
			else if (!package.Icon.ToLowerInvariant().EndsWith(".png") &&
					 !package.Icon.ToLowerInvariant().EndsWith(".jpg") &&
					 !package.Icon.ToLowerInvariant().EndsWith(".gif"))
			{
				errors.Add("The icon must be 90x90 pixel PNG, GIF, or JPEG");
			}
			else
			{
				string iconFile = Path.Combine(_extensionRoot, package.ID, package.Icon);

				if (File.Exists(iconFile))
				{
					if (ImageDimensionReader.TryGetDimensions(iconFile, out int width, out int height))
					{
						if (width < 90 || height < 90 || width > 128 || height > 128)
						{
							errors.Add($"The icon is {width}x{height}px. It must be 90x90px for best rendering on Marketplace and in Visual Studio");
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

		public Package GetPackage(string id)
		{
			lock (_cacheLock)
			{
				Package cached = _cache.FirstOrDefault(p => p.ID == id);
				if (cached != null)
				{
					return cached;
				}
			}

			string folder = Path.Combine(_extensionRoot, id);

			Package package = DeserializePackage(folder);
			SetFileSize(package, folder);
			return package;
		}

		private static Package DeserializePackage(string version)
		{
			string content = File.ReadAllText(Path.Combine(version, "extension.json"));
			return JsonSerializer.Deserialize<Package>(content);
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

				string vsixFolder = Path.Combine(_extensionRoot, package.ID);

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
					Debug.Write(ex);
				}

				try
				{
					RemoveOldExtensions();
				}
				catch (Exception ex)
				{
					Debug.Write(ex);
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
				oldPackages = [.. _cache.Where(p => p.DatePublished < DateTime.Now.AddMonths(-18))];
			}

			foreach (Package package in oldPackages)
			{
				try
				{
					string vsixFolder = Path.Combine(_extensionRoot, package.ID);
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
					Debug.Write(ex);
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

			string icon = Path.Combine(tempFolder, VsixManifestParser.NormalizeRelativePath(package.Icon ?? string.Empty));
			if (File.Exists(icon))
			{
				File.Copy(icon, Path.Combine(vsixFolder, "icon-" + package.Version + ".png"), true);
				package.Icon = "icon-" + package.Version + ".png";
			}

			string json = JsonSerializer.Serialize(package);

			File.WriteAllText(Path.Combine(vsixFolder, "extension.json"), json, Encoding.UTF8);

			lock (_cacheLock)
			{
				_cache.RemoveAll(p => p.ID == package.ID);
				_cache.Add(package);
			}
		}
	}
}