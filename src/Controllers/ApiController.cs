using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace VsixGallery.Controllers
{
	public class ApiController : Controller
	{
		private const string AuthorizationPrefix = "Bearer ";

		private readonly PackageHelper _helper;
		private readonly string _secretKey;

		public ApiController(PackageHelper helper, IOptions<UploadOptions> uploadOptions)
		{
			_helper = helper;
			_secretKey = uploadOptions.Value.SecretKey;
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

		[HttpPost, DisableRequestSizeLimit]
		public async Task<IActionResult> Upload([FromQuery] string repo, string issuetracker, string readmeUrl)
		{
			if (!IsAuthorized())
			{
				return Unauthorized();
			}

			try
			{
				HttpContext.Request.EnableBuffering();

				Package package = await _helper.ProcessVsix(Request.Form.Files[0], repo, issuetracker, readmeUrl);

				return Json(package);
			}
			catch (Exception ex)
			{
				Response.StatusCode = 500;
				Response.Headers["x-error"] = ex.Message;
				return Content(ex.Message);
			}
		}

		private bool IsAuthorized()
		{
			if (string.IsNullOrEmpty(_secretKey))
			{
				// No secret key means anyone can upload.
				return true;
			}

			if (Request.Headers.TryGetValue("Authorization", out StringValues values))
			{
				if (values.Count == 1)
				{
					string authorization = values[0];
					if (authorization.StartsWith(AuthorizationPrefix, StringComparison.OrdinalIgnoreCase))
					{
						return string.Equals(_secretKey, authorization.Substring(AuthorizationPrefix.Length).Trim());
					}
				}
			}

			return false;
		}
	}
}