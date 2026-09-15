using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

using System.Text;
using System.Xml;

namespace VsixGallery.Controllers
{
	[OutputCache(PolicyName = PackageHelper.GalleryGeneratedCachePolicy)]
	[Route("sitemap.xml")]
	public class SitemapController(PackageHelper helper, PublicUrl publicUrl) : Controller
	{
		private static readonly string[] _staticPaths = ["/", "/devguide", "/feedguide"];

		[HttpGet]
		public IActionResult Index()
		{
			string baseUrl = publicUrl.GetOrigin(Request);
			StringBuilder output = new();
			XmlWriterSettings settings = new()
			{
				Encoding = Encoding.UTF8,
				OmitXmlDeclaration = false,
			};

			using (StringWriter textWriter = new Utf8StringWriter(output))
			using (XmlWriter writer = XmlWriter.Create(textWriter, settings))
			{
				writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");

				foreach (string path in _staticPaths)
				{
					WriteUrl(writer, baseUrl + path, null);
				}

				foreach (Package package in helper.ListedPackages)
				{
					WriteUrl(writer, baseUrl + package.DetailsLink, package.DatePublished);
				}

				foreach (IGrouping<string, Package> author in helper.ListedPackages
					.Where(package => !string.IsNullOrWhiteSpace(package.Author))
					.GroupBy(package => package.Author!, StringComparer.OrdinalIgnoreCase)
					.OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase))
				{
					WriteUrl(
						writer,
						baseUrl + "/author/" + Uri.EscapeDataString(author.Key),
						author.Max(package => package.DatePublished));
				}

				writer.WriteEndElement();
			}

			return Content(output.ToString(), "application/xml", Encoding.UTF8);
		}

		private static void WriteUrl(XmlWriter writer, string location, DateTime? lastModified)
		{
			writer.WriteStartElement("url");
			writer.WriteElementString("loc", location);
			if (lastModified.HasValue)
			{
				writer.WriteElementString("lastmod", lastModified.Value.ToUniversalTime().ToString("yyyy-MM-dd"));
			}
			writer.WriteEndElement();
		}

		private sealed class Utf8StringWriter(StringBuilder builder) : StringWriter(builder)
		{
			public override Encoding Encoding => Encoding.UTF8;
		}
	}
}
