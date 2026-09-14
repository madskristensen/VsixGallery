using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

using System;
using System.Linq;
using System.Threading.Tasks;

namespace VsixGallery.Controllers
{
	[Route("api")]
	public class ApiController(
		PackageHelper helper,
		IOptions<UploadOptions> uploadOptions,
		ILogger<ApiController> logger) : Controller
	{
		private const string AuthorizationPrefix = "Bearer ";
		private readonly string? _secretKey = uploadOptions.Value.SecretKey;

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

		[HttpPost("upload"), RequestSizeLimit(500_000_000)]
		public async Task<IActionResult> Upload(
			[FromQuery] string? repo,
			string? issuetracker,
			string? readmeUrl,
			CancellationToken cancellationToken)
		{
			if (!IsAuthorized())
			{
				return Unauthorized();
			}

			try
			{
				IFormCollection form = await Request.ReadFormAsync(cancellationToken);
				if (form.Files.Count == 0)
				{
					return Problem(
						statusCode: StatusCodes.Status400BadRequest,
						title: "Invalid upload",
						detail: "No .vsix file was included in the upload request.");
				}

				// Optional manage token supplied by the publisher. When omitted the
				// server generates one and returns it embedded in the manage URL.
				string? manageToken = null;
				if (Request.Headers.TryGetValue("X-Manage-Token", out StringValues tokenValues) && tokenValues.Count == 1)
				{
					manageToken = tokenValues[0];
				}

				Package package = await helper.ProcessVsix(
					form.Files[0],
					repo ?? string.Empty,
					issuetracker ?? string.Empty,
					readmeUrl ?? string.Empty,
					manageToken,
					cancellationToken);

				// Surface the absolute manage URL so non-Actions publishers can
				// see it directly in the upload response body.
				if (!string.IsNullOrEmpty(package.ManageUrl))
				{
					string baseUrl = $"{Request.Scheme}://{Request.Host}";
					if (package.ManageUrl.StartsWith('/'))
					{
						package.ManageUrl = baseUrl + package.ManageUrl;
					}
				}

				return Json(package);
			}
			catch (InvalidDataException ex)
			{
				logger.LogWarning(ex, "Rejected invalid VSIX upload.");
				return Problem(
					statusCode: StatusCodes.Status400BadRequest,
					title: "Invalid VSIX",
					detail: "The uploaded file is not a valid or safely extractable VSIX package.");
			}
			catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
			{
				throw;
			}
			catch (Exception ex)
			{
				logger.LogError(ex, "VSIX upload failed.");
				return Problem(
					statusCode: StatusCodes.Status500InternalServerError,
					title: "Upload failed",
					detail: $"The upload could not be processed. Trace ID: {HttpContext.TraceIdentifier}");
			}
		}

		[HttpDelete("extension/{id}")]
		public IActionResult Delete(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				return BadRequest();
			}

			if (helper.GetPackage(id) is null)
			{
				return NotFound();
			}

			if (!Request.Headers.TryGetValue("X-Manage-Token", out StringValues tokenValues) || tokenValues.Count != 1)
			{
				return Unauthorized();
			}

			if (!helper.ValidateManageToken(id, tokenValues[0]))
			{
				return Unauthorized();
			}

			helper.SoftDelete(id);
			return NoContent();
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
					string? authorization = values[0];
					if (authorization?.StartsWith(AuthorizationPrefix, StringComparison.OrdinalIgnoreCase) == true)
					{
						return string.Equals(_secretKey, authorization.Substring(AuthorizationPrefix.Length).Trim());
					}
				}
			}

			return false;
		}
	}
}