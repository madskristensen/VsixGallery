using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;

using System.Linq;

namespace VsixGallery.Controllers
{
	[OutputCache(PolicyName = PackageHelper.GalleryGeneratedCachePolicy)]
	[Route("feed")]
	public class FeedController : Controller
	{
		private readonly PackageHelper _helper;
		private readonly FeedWriter _feed;
		private readonly PublicUrl _publicUrl;

		public FeedController(PackageHelper helper, PublicUrl publicUrl)
		{
			_helper = helper;
			_publicUrl = publicUrl;
			_feed = new FeedWriter();
		}

		[HttpGet("")]
		public IActionResult Index()
		{
			Response.ContentType = "application/atom+xml; charset=utf-8";
			IReadOnlyList<Package> packages = _helper.ListedPackages;

			if (this.IsConditionalGet(packages))
			{
				return new EmptyResult();
			}

			string baseUrl = _publicUrl.GetOrigin(Request);
			return Content(_feed.GetFeed(baseUrl, [.. packages]));
		}

		[HttpGet("extension/{id}")]
		public IActionResult Extension(string id)
		{
			Response.ContentType = "application/atom+xml; charset=utf-8";

			if (!string.IsNullOrEmpty(id))
			{
				Package? package = _helper.GetPackage(id);

				if (package is null)
				{
					return NotFound();
				}

				if (this.IsConditionalGet(package))
				{
					return new EmptyResult();
				}

				string baseUrl = _publicUrl.GetOrigin(Request);
				return Content(_feed.GetFeed(baseUrl, package));
			}

			return new RedirectResult("/", true);
		}

		[HttpGet("author/{id}")]
		public IActionResult Author(string id)
		{
			Response.ContentType = "application/atom+xml; charset=utf-8";
			string baseUrl = _publicUrl.GetOrigin(Request);

			if (!string.IsNullOrEmpty(id))
			{
				IReadOnlyList<Package> packages = _helper.GetPackagesByAuthor(id);

				if (this.IsConditionalGet(packages))
				{
					return new EmptyResult();
				}

				return Content(_feed.GetFeed(baseUrl, [.. packages]));
			}

			return new RedirectResult("/", true);
		}

	}
}