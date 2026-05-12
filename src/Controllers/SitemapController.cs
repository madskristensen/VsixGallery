using Microsoft.AspNetCore.Mvc;

using System.Text;

namespace VsixGallery.Controllers
{
	[Route("sitemap.xml")]
	public class SitemapController(PackageHelper helper) : Controller
	{
		private static readonly string[] _staticPaths = ["/", "/devguide", "/feedguide"];

		[HttpGet]
		public IActionResult Index()
		{
			string baseUrl = $"{Request.Scheme}://{Request.Host}";

			StringBuilder sb = new();
			sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
			sb.Append("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

			foreach (string path in _staticPaths)
			{
				sb.Append("<url><loc>").Append(baseUrl).Append(path).Append("</loc></url>");
			}

			foreach (Package package in helper.PackageCache.Where(p => !p.Unlisted))
			{
				sb.Append("<url>");
				sb.Append("<loc>").Append(baseUrl).Append(package.DetailsLink).Append("</loc>");
				sb.Append("<lastmod>").Append(package.DatePublished.ToUniversalTime().ToString("yyyy-MM-dd")).Append("</lastmod>");
				sb.Append("</url>");
			}

			sb.Append("</urlset>");

			return Content(sb.ToString(), "application/xml", Encoding.UTF8);
		}
	}
}
