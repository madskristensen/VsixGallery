using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

using System.Linq;

namespace VsixGallery.Controllers
{
	public class FeedController : Controller
	{
		private readonly PackageHelper _helper;
		private readonly FeedWriter _feed;

		public FeedController(PackageHelper helper)
		{
			_helper = helper;
			_feed = new FeedWriter();
		}

		public IActionResult Index()
		{
			Response.ContentType = "text/xml";
			Package[] packages = _helper.PackageCache
				.Where(p => !p.Unlisted)
				.OrderByDescending(p => p.DatePublished).ToArray();

			if (this.IsConditionalGet(packages))
			{
				return new EmptyResult();
			}

			string baseUrl = Request.Scheme + "://" + Request.Host;
			return Content(_feed.GetFeed(baseUrl, packages));
		}

		public IActionResult Extension(string id)
		{
			Response.ContentType = "text/xml";

			if (!string.IsNullOrEmpty(id))
			{
				Package package = _helper.GetPackage(id);

				if (this.IsConditionalGet(package))
				{
					return new EmptyResult();
				}

				string baseUrl = Request.Scheme + "://" + Request.Host;
				return Content(_feed.GetFeed(baseUrl, package));
			}

			return new RedirectResult("/", true);
		}

		public IActionResult Author(string id)
		{
			Response.ContentType = "text/xml";
			string baseUrl = Request.Scheme + "://" + Request.Host;

			if (!string.IsNullOrEmpty(id))
			{
				IOrderedEnumerable<Package> packages = _helper.PackageCache
									  .Where(p => p.Author.Equals(id, System.StringComparison.OrdinalIgnoreCase))
									  .OrderByDescending(p => p.DatePublished);

				if (this.IsConditionalGet(packages))
				{
					return new EmptyResult();
				}

				return Content(_feed.GetFeed(baseUrl, packages.ToArray()));
			}

			return new RedirectResult("/", true);
		}

	}
}