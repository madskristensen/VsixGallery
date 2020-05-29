using Microsoft.AspNetCore.Http;

using Newtonsoft.Json;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VsixGallery
{
	public class PackageHelper
	{
		private readonly string _webroot;
		private readonly string _extensionRoot;
		public static List<Package> _cache;

		public PackageHelper(string webroot)
		{
			_webroot = webroot;
			_extensionRoot = Path.Combine(webroot, "extensions");
		}

		public List<Package> PackageCache
		{
			get
			{
				if (_cache == null)
				{
					_cache = GetAllPackages();
				}

				return _cache;
			}
		}

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
				package.Icon = "~/img/defaulticon.svg";
			}
			else
			{
				package.Icon = $"~/extensions/{package.ID}/{package.Icon}";
			}

			if (!string.IsNullOrWhiteSpace(package.Repo) && !package.Repo.Contains("://"))
			{
				package.Repo = "https://" + package.Repo;
			}
		}

		public Package GetPackage(string id)
		{
			if (PackageCache.Any(p => p.ID == id))
			{
				return PackageCache.SingleOrDefault(p => p.ID == id);
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
			string tempFolder = Path.Combine(_webroot, "temp", Guid.NewGuid().ToString());

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

				Sanitize(package);
				SavePackage(tempFolder, package, vsixFolder);

				File.Copy(tempVsix, Path.Combine(vsixFolder, "extension.vsix"), true);

				return package;
			}
			catch (Exception ex)
			{
				Debug.Write(ex);
				return null;
			}
			finally
			{
				Directory.Delete(tempFolder, true);
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

			Package existing = PackageCache.FirstOrDefault(p => p.ID == package.ID);

			if (PackageCache.Contains(existing))
			{
				PackageCache.Remove(existing);
			}

			PackageCache.Add(package);
		}
	}
}