using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace VsixGallery.Controllers
{
	[Route("api")]
	public class ApiController(PackageHelper helper, IOptions<UploadOptions> uploadOptions) : Controller
	{
		private const string AuthorizationPrefix = "Bearer ";
		private readonly string _secretKey = uploadOptions.Value.SecretKey;

		[HttpGet("{id?}")]
		public object Get(string id)
		{
			Response.Headers.CacheControl = "no-cache";

			if (string.IsNullOrWhiteSpace(id))
			{
				IOrderedEnumerable<Package> packages = helper.PackageCache.OrderByDescending(p => p.DatePublished);

				if (this.IsConditionalGet(packages))
				{
					return Enumerable.Empty<Package>();
				}

				return packages;
			}

			Package? package = helper.GetPackage(id);

				if (package is null)
				{
					return NotFound();
				}

			if (this.IsConditionalGet(package))
			{
				return new EmptyResult();
			}

			return package;
		}

		[HttpPost("upload"), DisableRequestSizeLimit]
		public async Task<IActionResult> Upload([FromQuery] string repo, string issuetracker, string readmeUrl)
		{
			if (!IsAuthorized())
			{
				return Unauthorized();
			}

			try
			{
				if (Request.Form.Files.Count == 0)
				{
					Response.StatusCode = 400;
					return Content("No .vsix file was included in the upload request.");
				}

				Package package = await helper.ProcessVsix(Request.Form.Files[0], repo, issuetracker, readmeUrl);

				return Json(package);
			}
			catch (Exception ex)
			{
				Response.StatusCode = 500;
				// HTTP headers cannot contain CR/LF. Exception messages from
				// ZipFile, IO, etc. frequently do, and attempting to set an
				// invalid header value would abort the response and surface as
				// a connection reset to the CI client.
				Response.Headers["x-error"] = SanitizeHeaderValue(ex.Message);
				return Content(ex.Message);
			}
		}

		private static string SanitizeHeaderValue(string value)
		{
			if (string.IsNullOrEmpty(value))
			{
				return string.Empty;
			}

			return value
				.Replace("\r", " ")
				.Replace("\n", " ");
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