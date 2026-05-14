using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace VsixGallery
{
	/// <summary>
	/// Represents a single VSIX installation target (e.g. VS Community, Pro, Enterprise) with its version range and optional architecture.
	/// </summary>
	public record InstallationTarget(string Identifier, string VersionRange, string? ProductArchitecture = null);

	public class Package
	{
		private readonly static Dictionary<int, string> _majorVersionToProduct = new()
		{
			{ 10, "VS 2010" },
			{ 11, "VS 2012" },
			{ 12, "VS 2013" },
			{ 14, "VS 2015" },
			{ 15, "VS 2017" },
			{ 16, "VS 2019" },
			{ 17, "VS 2022" },
			{ 18, "VS 2026" },
		};

		private readonly static Dictionary<string, string> _identifierToProduct = new(StringComparer.OrdinalIgnoreCase)
		{
			{ "Microsoft.VisualStudio.Community", "Visual Studio" },
			{ "Microsoft.VisualStudio.Pro", "Visual Studio" },
			{ "Microsoft.VisualStudio.Enterprise", "Visual Studio" },
			{ "Microsoft.VisualStudio.IntegratedShell", "Visual Studio" },
			{ "Microsoft.VisualStudio.Community.Arm64", "Visual Studio (ARM64)" },
			{ "Microsoft.VisualStudio.Pro.Arm64", "Visual Studio (ARM64)" },
			{ "Microsoft.VisualStudio.Enterprise.Arm64", "Visual Studio (ARM64)" },
			{ "SSMS.Microsoft.SQL", "SSMS" },
			{ "Microsoft.SSMS", "SSMS" },
		};
		public string? ID { get; set; }
		public string? Name { get; set; }
		public string? Description { get => field?.Trim(); set; }
		public string? Author { get; set; }
		public string? Version { get; set; }
		public string? Icon { get; set; }
		public string? Tags { get => field?.Trim(); set; }
		public int IconWidth { get; set; }
		public int IconHeight { get; set; }
		public DateTime DatePublished { get; set; }
		public IEnumerable<string>? SupportedVersions { get; set; }
		public IEnumerable<InstallationTarget>? InstallationTargets { get; set; }
		public string? License { get; set; }
		public string? GettingStartedUrl { get; set; }
		public string? ReleaseNotesUrl { get; set; }
		public string? MoreInfoUrl { get; set; }
		public string? Repo { get; set; }
		public string? IssueTracker { get; set; }
		public string? ReadmeUrl { get; set; }
		public ExtensionList? ExtensionList { get; set; }

			[JsonIgnore]
			public IEnumerable<string>? Errors { get; set; }

			/// <summary>
			/// The size of the VSIX file in bytes.
			/// </summary>
			[JsonIgnore]
			public long FileSize { get; set; }

			/// <summary>
			/// Returns the file size formatted as KB or MB depending on the size.
			/// </summary>
			public string FormattedFileSize
			{
				get
				{
					if (FileSize <= 0)
					{
						return string.Empty;
					}

					const long kb = 1024;
					const long mb = kb * 1024;

					if (FileSize >= mb)
					{
						double sizeMb = FileSize / (double)mb;
						return $"{sizeMb:F1} MB";
					}
					else
					{
						double sizeKb = FileSize / (double)kb;
						return $"{sizeKb:F0} KB";
					}
				}
			}

		public string AuthorLink =>
			$"/author/{Uri.EscapeDataString(Author ?? string.Empty)}";

		/// <summary>
		/// Returns a human-readable relative time string (e.g. "3 days ago", "2 months ago").
		/// </summary>
		public string TimeAgo(TimeProvider? timeProvider = null)
		{
			TimeSpan elapsed = (timeProvider ?? TimeProvider.System).GetUtcNow() - DatePublished.ToUniversalTime();

			if (elapsed.TotalMinutes < 1)
			{
				return "just now";
			}
			if (elapsed.TotalHours < 1)
			{
				int minutes = (int)elapsed.TotalMinutes;
				return $"{minutes} {(minutes == 1 ? "minute" : "minutes")} ago";
			}
			if (elapsed.TotalDays < 1)
			{
				int hours = (int)elapsed.TotalHours;
				return $"{hours} {(hours == 1 ? "hour" : "hours")} ago";
			}
			if (elapsed.TotalDays < 30)
			{
				int days = (int)elapsed.TotalDays;
				return $"{days} {(days == 1 ? "day" : "days")} ago";
			}
			if (elapsed.TotalDays < 365)
			{
				int months = (int)(elapsed.TotalDays / 30);
				return $"{months} {(months == 1 ? "month" : "months")} ago";
			}

			int years = (int)(elapsed.TotalDays / 365);
			return $"{years} {(years == 1 ? "year" : "years")} ago";
		}

		/// <summary>
		/// Returns the description truncated at a word boundary with an ellipsis appended.
		/// </summary>
		public string TruncatedDescription(int maxLength = 120)
		{
			if (string.IsNullOrEmpty(Description) || Description.Length <= maxLength)
			{
				return Description ?? string.Empty;
			}

			int cutAt = Description.LastIndexOf(' ', maxLength);
			if (cutAt <= 0)
			{
				cutAt = maxLength;
			}

			return Description[..cutAt] + "\u2026";
		}

		public string DownloadLink =>
			$"/extensions/{ID}/{Uri.EscapeDataString((Name ?? string.Empty) + " ")}v{Version}.vsix";

		public string DetailsLink =>
			$"/extension/{ID}";

		public string FeedLink =>
			$"/feed/extension/{ID}";

		public bool HasValidatorErrors =>
			Errors?.Any() == true;

		public bool Unlisted =>
			!string.IsNullOrEmpty(Tags) && Tags.Contains("unlisted", StringComparison.OrdinalIgnoreCase);

		/// <summary>
		/// Returns deduplicated friendly names for the installation targets (e.g. "VS 2022", "VS 2026 (ARM64)", "SSMS 21").
		/// </summary>
		public IEnumerable<string> FriendlyTargets => GetFriendlyTargets();

		private IEnumerable<string> GetFriendlyTargets()
		{
			if (InstallationTargets is null || !InstallationTargets.Any())
			{
				return [];
			}

			return GetFriendlyTargetsFromInstallationTargets();
		}

		private IEnumerable<string> GetFriendlyTargetsFromInstallationTargets()
		{
			HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

			foreach (InstallationTarget target in InstallationTargets)
			{
				bool isArm = target.Identifier.Contains("Arm64", StringComparison.OrdinalIgnoreCase) ||
							 string.Equals(target.ProductArchitecture, "arm64", StringComparison.OrdinalIgnoreCase);
				bool isSsms = target.Identifier.Contains("SSMS", StringComparison.OrdinalIgnoreCase) ||
							  target.Identifier.Contains("SQL", StringComparison.OrdinalIgnoreCase);

				foreach (int major in ParseMajorVersions(target.VersionRange))
				{
					if (isSsms)
					{
						names.Add($"SSMS {major}");
					}
					else if (_majorVersionToProduct.TryGetValue(major, out string? product))
					{
						string display = isArm ? product + " (ARM64)" : product;
						names.Add(display);
					}
					else
					{
						string label = isArm ? $"VS (v{major}, ARM64)" : $"VS (v{major})";
						names.Add(label);
					}
				}
			}

			return names;
		}

		/// <summary>
		/// Parses a version range string (e.g. "[14.0, 17.0)", "[17.0,)") and returns
		/// all known VS major versions that fall within the range.
		/// See https://devblogs.microsoft.com/visualstudio/visual-studio-extensions-and-version-ranges-demystified/
		/// </summary>
		private static IEnumerable<int> ParseMajorVersions(string? versionRange)
		{
			if (string.IsNullOrWhiteSpace(versionRange))
			{
				yield break;
			}

			string trimmed = versionRange.Trim();

			// Handle simple version string without brackets (legacy format, e.g. "16.0")
			if (!trimmed.StartsWith('[') && !trimmed.StartsWith('('))
			{
				if (System.Version.TryParse(trimmed, out Version? simple))
				{
					yield return simple.Major;
				}
				yield break;
			}

			bool fromInclusive = trimmed.StartsWith('[');
			bool toInclusive = trimmed.EndsWith(']');

			string inner = trimmed.TrimStart('[', '(').TrimEnd(']', ')');
			string[] parts = inner.Split(',');

			// Parse the from-version
			Version? fromVersion = null;
			if (parts.Length >= 1 && !string.IsNullOrWhiteSpace(parts[0]))
			{
				System.Version.TryParse(parts[0].Trim(), out fromVersion);
			}

			// Parse the to-version (may be empty for open-ended ranges like [17.0,))
			Version? toVersion = null;
			if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
			{
				System.Version.TryParse(parts[1].Trim(), out toVersion);
			}

			if (fromVersion == null)
			{
				yield break;
			}

			// Determine the effective minimum major version
			int minMajor = fromInclusive ? fromVersion.Major : fromVersion.Major + 1;

			// Determine the effective maximum major version
			int maxMajor;
			if (toVersion == null)
			{
				// Open-ended range: include all known versions
				maxMajor = _majorVersionToProduct.Keys.Max();
			}
			else
			{
				// For exclusive upper bound like [14.0, 17.0): 17.0 excluded means max is 16
				// For [14.0, 17.1): 17.1 excluded still includes major 17
				if (toInclusive)
				{
					maxMajor = toVersion.Major;
				}
				else
				{
					// Exclusive: if the minor/build are exactly 0, the major itself is excluded
					bool exactMajor = (toVersion.Minor <= 0) &&
									  (toVersion.Build <= 0 || toVersion.Build == -1);
					maxMajor = exactMajor ? toVersion.Major - 1 : toVersion.Major;
				}
			}

			foreach (int major in _majorVersionToProduct.Keys)
			{
				if (major >= minMajor && major <= maxMajor)
				{
					yield return major;
				}
			}
		}

		public override string ToString()
		{
			return Name ?? string.Empty;
		}
	}
}