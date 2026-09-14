using System.Xml.Linq;

namespace VsixGallery.Tests;

public class FeedWriterTests
{
	[Fact]
	public void GetFeed_UsesUtf8AndUtcTimestamps()
	{
		Package package = new()
		{
			ID = "example",
			Name = "Example",
			Description = "Example extension",
			Author = "Publisher",
			Version = "1.0",
			DatePublished = new DateTime(2026, 9, 13, 12, 0, 0, DateTimeKind.Local),
		};
		FeedWriter writer = new();

		string xml = writer.GetFeed("https://www.vsixgallery.com", package);
		XDocument document = XDocument.Parse(xml);

		Assert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml, StringComparison.OrdinalIgnoreCase);
		Assert.Equal(
			package.DatePublished.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ"),
			document.Root!.Elements().First(e => e.Name.LocalName == "updated").Value);
		Assert.EndsWith(
			"Z",
			document.Descendants().First(e => e.Name.LocalName == "published").Value);
	}

}
