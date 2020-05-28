using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace VsixGallery.Controllers
{
	public class ApiController : Controller
	{
		private readonly IWebHostEnvironment _env;
		private readonly PackageHelper _helper;

		public ApiController(IWebHostEnvironment env)
		{
			_env = env;
			_helper = new PackageHelper(env.WebRootPath);
		}

		public object Get(string id)
		{
			Response.Headers["Cache-Control"] = "no-cache";

			if (string.IsNullOrWhiteSpace(id))
			{
				IOrderedEnumerable<Package> packages = _helper.PackageCache.OrderByDescending(p => p.DatePublished);

				if (this.IsConditionalGet(packages))
				{
					return Enumerable.Empty<Package>();
				}

				return packages;
			}

			Package package = _helper.GetPackage(id);

			if (this.IsConditionalGet(package))
			{
				return new EmptyResult();
			}

			return package;
		}

		[HttpPost]
		public async Task<IActionResult> Upload([FromQuery] string repo, string issuetracker)
		{
			try
			{
				Stream bodyStream = Request.Body;
				Package package = await _helper.ProcessVsix(bodyStream, repo, issuetracker);

				return Json(package);
			}
			catch (Exception ex)
			{
				Response.StatusCode = 500;
				Response.Headers["x-error"] = ex.Message;
				return Content(ex.Message);
			}
		}
	}
}