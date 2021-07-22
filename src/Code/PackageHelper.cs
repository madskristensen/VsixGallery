using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VsixGallery
{
	public class PackageHelper
	{
		private readonly string _extensionRoot;
		private readonly List<Package> _cache;
		private readonly bool _canRemoveOldExtensions;

		public PackageHelper(IWebHostEnvironment env, IOptions<ExtensionsOptions> options)
		{
			_canRemoveOldExtensions = options.Value.RemoveOldExtensions;
			_extensionRoot = options.Value.Directory;

			// Default to an "extensions" directory under the web root
			// path when a directory is not specified in the options.
			if (string.IsNullOrEmpty(_extensionRoot))
			{
				_extensionRoot = Path.Combine(env.WebRootPath, "extensions");
			}
			_cache = GetAllPackages();
		}

		public IReadOnlyList<Package> PackageCache => _cache;

		private List<Package> GetAllPackages()
		{
			List<Package> packages = new List<Package>();

			if (!Directory.Exists(_extensionRoot))
			{
				return packages.ToList();
			}

			foreach (string extension in Directory.EnumerateDirectories(_extensionRoot))
			{
				string json = Path.Combine(extension, "extension.json");
				if (File.Exists(json))
				{
					string content = File.ReadAllText(json);
					Package package = JsonConvert.DeserializeObject(content, typeof(Package)) as Package;
					Validate(package);
					Sanitize(package);
					packages.Add(package);
				}
			}

			return packages.OrderByDescending(p => p.DatePublished).ToList();
		}

		private static void Sanitize(Package package)
		{
			if (string.IsNullOrWhiteSpace(package.Icon))
			{
				package.Icon = "/img/defaulticon.svg";
			}
			else
			{
				package.Icon = $"/extensions/{package.ID}/{package.Icon}";
			}

			if (!string.IsNullOrWhiteSpace(package.Repo) && !package.Repo.Contains("://"))
			{
				package.Repo = "https://" + package.Repo;
			}
		}

		public void Validate(Package package)
		{
			List<string> errors = new List<string>();

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
					using (FileStream file = new FileStream(iconFile, FileMode.Open, FileAccess.Read))
					{
						using (Image img = Image.FromStream(stream: file, useEmbeddedColorManagement: false, validateImageData: false))
						{
							float width = img.PhysicalDimension.Width;
							float height = img.PhysicalDimension.Height;

							if (width < 90 || height < 90 || width > 128 || height > 128)
							{
								errors.Add($"The icon is {width}x{height}px. It must be 90x90px for best rendering on Marketplace and in Visual Studio");
							}
						}
					}
				}
			}

			if (package.Description?.Length < 40)
			{
				errors.Add("Provide a clear description. Make sure to cover why it is great and what it does");
			}

			if (string.IsNullOrEmpty(package.License))
			{
				errors.Add("No license is specified in the .vsixmanifest");
			}

			package.Errors = errors;
		}

		public Package GetPackage(string id)
		{
			if (_cache.Any(p => p.ID == id))
			{
				return _cache.SingleOrDefault(p => p.ID == id);
			}

			string folder = Path.Combine(_extensionRoot, id);
			List<Package> packages = new List<Package>();

			return DeserializePackage(folder);
		}

		private static Package DeserializePackage(string version)
		{
			string content = File.ReadAllText(Path.Combine(version, "extension.json"));
			return JsonConvert.DeserializeObject(content, typeof(Package)) as Package;
		}

		public async Task<Package> ProcessVsix(IFormFile file, string repo, string issuetracker)
		{
			string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());

			try
			{
				string tempVsix = Path.Combine(tempFolder, "extension.vsix");

				if (!Directory.Exists(tempFolder))
				{
					Directory.CreateDirectory(tempFolder);
				}

				using (FileStream fileStream = new FileStream(tempVsix, FileMode.CreateNew))
				{
					await file.CopyToAsync(fileStream);
				}

				ZipFile.ExtractToDirectory(tempVsix, tempFolder);

				VsixManifestParser parser = new VsixManifestParser();
				Package package = parser.CreateFromManifest(tempFolder, repo, issuetracker);

				string vsixFolder = Path.Combine(_extensionRoot, package.ID);

				SavePackage(tempFolder, package, vsixFolder);
				Validate(package);
				Sanitize(package);

				File.Copy(tempVsix, Path.Combine(vsixFolder, "extension.vsix"), true);

				return package;
			}
			finally
			{
				Directory.Delete(tempFolder, true);
				RemoveOldExtensions();
			}
		}

		private void RemoveOldExtensions()
		{
			if (!_canRemoveOldExtensions)
			{
				return;
			}

			Package[] oldPackages = _cache.Where(p => p.DatePublished < DateTime.Now.AddMonths(-18)).ToArray();

			foreach (Package package in oldPackages)
			{
				try
				{
					string vsixFolder = Path.Combine(_extensionRoot, package.ID);
					Directory.Delete(vsixFolder, true);
					_cache.Remove(package);
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

			string icon = Path.Combine(tempFolder, package.Icon ?? string.Empty);
			if (File.Exists(icon))
			{
				File.Copy(icon, Path.Combine(vsixFolder, "icon-" + package.Version + ".png"), true);
				package.Icon = "icon-" + package.Version + ".png";
			}

			string json = JsonConvert.SerializeObject(package);

			File.WriteAllText(Path.Combine(vsixFolder, "extension.json"), json, Encoding.UTF8);

			Package existing = _cache.FirstOrDefault(p => p.ID == package.ID);

			if (_cache.Contains(existing))
			{
				_cache.Remove(existing);
			}

			_cache.Add(package);
		}
	}
}