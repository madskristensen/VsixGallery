using System.Runtime.Serialization.Json;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace VsixGallery
{
	public class VsixManifestParser
	{
		public Package CreateFromManifest(string tempFolder, string repo, string issuetracker, string readmeUrl)
		{
			string xml = File.ReadAllText(Path.Combine(tempFolder, "extension.vsixmanifest"));
			xml = Regex.Replace(xml, "( xmlns(:\\w+)?)=\"([^\"]+)\"", string.Empty);

			XmlDocument doc = new();
			doc.LoadXml(xml);

			Package package = new()
			{
				Repo = repo,
				IssueTracker = issuetracker,
			};

			if (doc.GetElementsByTagName("DisplayName").Count > 0)
			{
				Vs2012Format(doc, package);
			}
			else
			{
				Vs2010Format(doc, package);
			}

			ApplyRepoFallback(package);

			// An explicit readmeUrl from the upload always wins over whatever
			// ApplyRepoFallback inferred.
			if (!string.IsNullOrWhiteSpace(readmeUrl))
			{
				package.ReadmeUrl = BuildReadmeUrl(package.Repo, readmeUrl);
			}

			string license = ParseNode(doc, "License", false);
			if (!string.IsNullOrEmpty(license))
			{
				string path = ResolveRelativeFile(tempFolder, license);
				if (path != null)
				{
					package.License = File.ReadAllText(path);
				}
			}

			AddExtensionList(package, tempFolder);

			return package;
		}

		// Fills in Repo, IssueTracker, and ReadmeUrl when the uploader didn't
		// supply a `repo` query parameter, by falling back to the manifest's
		// <MoreInfo> URL when it points at a GitHub repository. Idempotent:
		// any value already set is preserved.
		public static void ApplyRepoFallback(Package package)
		{
			if (package == null)
			{
				return;
			}

			if (string.IsNullOrWhiteSpace(package.Repo))
			{
				string inferred = InferGitHubRepo(package.MoreInfoUrl);
				if (!string.IsNullOrEmpty(inferred))
				{
					package.Repo = inferred;
				}
			}

			// Resolve a relative issue tracker (e.g. "issues/") against the
			// repo so it renders as a usable absolute URL.
			if (!string.IsNullOrWhiteSpace(package.IssueTracker)
				&& !Regex.IsMatch(package.IssueTracker, "^https?://")
				&& !string.IsNullOrEmpty(package.Repo))
			{
				package.IssueTracker = package.Repo.TrimEnd('/') + "/" + package.IssueTracker.TrimStart('/');
			}

			// Backfill a missing ReadmeUrl for legacy cached packages whose
			// stored JSON was written before the fallback existed.
			if (string.IsNullOrEmpty(package.ReadmeUrl) && !string.IsNullOrEmpty(package.Repo))
			{
				package.ReadmeUrl = BuildReadmeUrl(package.Repo, null);
			}
		}

		private static string InferGitHubRepo(string moreInfoUrl)
		{
			if (string.IsNullOrWhiteSpace(moreInfoUrl))
			{
				return null;
			}

			// Match https://github.com/<owner>/<name> with optional trailing
			// path/slash. Anything deeper (e.g. a tree/blob URL) is rejected
			// so we don't construct nonsense raw URLs later.
			Match match = Regex.Match(
				moreInfoUrl.Trim(),
				@"^https?://github\.com/([^/\s]+)/([^/\s#?]+?)(?:\.git)?/?$",
				RegexOptions.IgnoreCase);

			if (!match.Success)
			{
				return null;
			}

			return "https://github.com/" + match.Groups[1].Value + "/" + match.Groups[2].Value;
		}

		private static string BuildReadmeUrl(string repo, string readmeUrl)
		{
			// Default to `main/README.md` if a URL was not specified.
			if (string.IsNullOrWhiteSpace(readmeUrl))
			{
				readmeUrl = "main/README.md";
			}

			// If the provided URL is absolute, then use it
			// as is; otherwise, assume it's a GitHub URL.
			if (Regex.IsMatch(readmeUrl, "^https?://"))
			{
				return readmeUrl;
			}

			if (string.IsNullOrEmpty(repo))
			{
				return "";
			}

			string baseUrl = repo.Replace("https://github.com", "https://raw.githubusercontent.com").TrimEnd('/');
			string path = readmeUrl.TrimStart('/');

			// Insert /refs/heads/ before the branch name for reliable resolution.
			if (!path.StartsWith("refs/", StringComparison.OrdinalIgnoreCase))
			{
				path = "refs/heads/" + path;
			}

			return baseUrl + "/" + path;
		}

		private static void AddExtensionList(Package package, string tempFolder)
		{
			string vsext = Directory.EnumerateFiles(tempFolder, "*.vsext", SearchOption.AllDirectories).FirstOrDefault();

			if (!string.IsNullOrEmpty(vsext))
			{
				string json = File.ReadAllText(vsext);

				using (MemoryStream ms = new(Encoding.UTF8.GetBytes(json)))
				{
					DataContractJsonSerializer serializer = new(typeof(ExtensionList));
					ExtensionList list = (ExtensionList)serializer.ReadObject(ms);
					package.ExtensionList = list;
				}
			}
		}

		private static void Vs2012Format(XmlDocument doc, Package package)
		{
			package.ID = ParseNode(doc, "Identity", true, "Id");
			package.Name = ParseNode(doc, "DisplayName", true);
			package.Description = ParseNode(doc, "Description", true);
			package.Version = new Version(ParseNode(doc, "Identity", true, "Version")).ToString();
			package.Author = ParseNode(doc, "Identity", true, "Publisher");
			package.Icon = ParseNode(doc, "Icon", false);
			package.Tags = ParseNode(doc, "Tags", false);
			package.DatePublished = DateTime.UtcNow;
			package.SupportedVersions = GetSupportedVersions(doc);
			package.InstallationTargets = GetInstallationTargets(doc);
			package.ReleaseNotesUrl = ParseNode(doc, "ReleaseNotes", false);
			package.GettingStartedUrl = ParseNode(doc, "GettingStartedGuide", false);
			package.MoreInfoUrl = ParseNode(doc, "MoreInfo", false);
		}

		private static void Vs2010Format(XmlDocument doc, Package package)
		{
			package.ID = ParseNode(doc, "Identifier", true, "Id");
			package.Name = ParseNode(doc, "Name", true);
			package.Description = ParseNode(doc, "Description", true);
			package.Version = new Version(ParseNode(doc, "Version", true)).ToString();
			package.Author = ParseNode(doc, "Author", true);
			package.Icon = ParseNode(doc, "Icon", false);
			package.DatePublished = DateTime.UtcNow;
			package.SupportedVersions = GetSupportedVersions(doc);
			package.InstallationTargets = GetInstallationTargets(doc);
			package.ReleaseNotesUrl = ParseNode(doc, "ReleaseNotes", false);
			package.GettingStartedUrl = ParseNode(doc, "GettingStartedGuide", false);
			package.MoreInfoUrl = ParseNode(doc, "MoreInfo", false);
		}

		private static List<string> GetSupportedVersions(XmlDocument doc)
		{
			XmlNodeList list = doc.GetElementsByTagName("InstallationTarget");

			if (list.Count == 0)
			{
				list = doc.GetElementsByTagName("VisualStudio");
			}

			List<string> versions = [];

			foreach (XmlNode node in list)
			{
				string raw = node.Attributes["Version"].Value.Trim('[', '(', ']', ')');
				string[] entries = raw.Split(',');

				foreach (string entry in entries)
				{
					if (Version.TryParse(entry, out Version v) && !versions.Contains(v.ToString()))
					{
						versions.Add(v.ToString());
					}
				}
			}

			return versions;
		}

		private static List<InstallationTarget> GetInstallationTargets(XmlDocument doc)
		{
			XmlNodeList list = doc.GetElementsByTagName("InstallationTarget");

			if (list.Count == 0)
			{
				list = doc.GetElementsByTagName("VisualStudio");
			}

			List<InstallationTarget> targets = [];

			foreach (XmlNode node in list)
			{
				string identifier = node.Attributes?["Id"]?.Value;
				string versionRange = node.Attributes?["Version"]?.Value;

				if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(versionRange))
				{
					continue;
				}

				string architecture = node["ProductArchitecture"]?.InnerText;
				targets.Add(new InstallationTarget(identifier, versionRange, architecture));
			}

			return targets;
		}

		// VSIX manifests generated by Microsoft tooling use Windows-style
		// backslash separators in paths (e.g. "Resources\Icon.png"). On Linux,
		// backslash is a valid filename character, so Path.Combine produces a
		// literal path that doesn't match what ZipFile.ExtractToDirectory wrote
		// to disk. Normalize to the host's separator before combining.
		static internal string NormalizeRelativePath(string path)
		{
			return string.IsNullOrEmpty(path) ? path : path.Replace('\\', Path.DirectorySeparatorChar);
		}

		// Resolves a manifest-relative file path against an extracted VSIX folder.
		// Manifest paths use Windows-style separators and Windows-style casing
		// (e.g. "Resources\License.txt"), but the actual zip entry casing can
		// differ (e.g. "Resources\LICENSE.txt"). On Linux the file system is
		// case-sensitive, so we fall back to a case-insensitive segment lookup
		// when the literal path doesn't exist on disk. Returns null when no
		// matching file can be found.
		static internal string ResolveRelativeFile(string root, string relativePath)
		{
			if (string.IsNullOrEmpty(relativePath))
			{
				return null;
			}

			string normalized = NormalizeRelativePath(relativePath);
			string direct = Path.Combine(root, normalized);
			if (File.Exists(direct))
			{
				return direct;
			}

			string[] segments = normalized.Split([Path.DirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
			string current = root;
			foreach (string segment in segments)
			{
				if (!Directory.Exists(current))
				{
					return null;
				}

				string match = Directory
					.EnumerateFileSystemEntries(current)
					.FirstOrDefault(entry => string.Equals(Path.GetFileName(entry), segment, StringComparison.OrdinalIgnoreCase));

				if (match == null)
				{
					return null;
				}

				current = match;
			}

			return File.Exists(current) ? current : null;
		}

		private static string ParseNode(XmlDocument doc, string name, bool required, string attribute = "")
		{
			XmlNodeList list = doc.GetElementsByTagName(name);

			if (list.Count > 0)
			{
				XmlNode node = list[0];

				if (string.IsNullOrEmpty(attribute))
				{
					return node.InnerText;
				}

				XmlAttribute attr = node.Attributes[attribute];

				if (attr != null)
				{
					return attr.Value;
				}
			}

			if (required)
			{
				string message = string.Format("Attribute '{0}' could not be found on the '{1}' element in the .vsixmanifest file.", attribute, name);
				throw new Exception(message);
			}

			return null;
		}

	}
}