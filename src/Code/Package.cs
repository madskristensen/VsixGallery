using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace VsixGallery
{
	public class Package
	{
		public string ID { get; set; }
		public string Name { get; set; }
		public string Description { get; set; }
		public string Author { get; set; }
		public string Version { get; set; }
		public string Icon { get; set; }
		public string Tags { get; set; }
		public DateTime DatePublished { get; set; }
		public IEnumerable<string> SupportedVersions { get; set; }
		public string License { get; set; }
		public string GettingStartedUrl { get; set; }
		public string ReleaseNotesUrl { get; set; }
		public string MoreInfoUrl { get; set; }
		public string Repo { get; set; }
		public string IssueTracker { get; set; }
		public string ReadmeUrl { get; set; }
		public ExtensionList ExtensionList { get; set; }

		[JsonIgnore]
		public IEnumerable<string> Errors { get; set; }

		public string AuthorLink =>
			$"/author/{Uri.EscapeDataString(Author)}";

		public string DownloadLink =>
			$"/extensions/{ID}/{Uri.EscapeDataString(Name + " ")}v{Version}.vsix";

		public string DetailsLink =>
			$"/extension/{ID}";

		public string FeedLink =>
			$"/feed/extension/{ID}";

		public bool HasValidatorErrors =>
			Errors != null && Errors.Any();

		public bool Unlisted =>
			!string.IsNullOrEmpty(Tags) && Tags.Contains("unlisted", StringComparison.OrdinalIgnoreCase);

		public override string ToString()
		{
			return Name;
		}
	}
}