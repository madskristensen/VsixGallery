using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VsixGallery.Controllers
{
	[Route("api")]
	public class ApiController(
		PackageHelper helper,
		IOptions<UploadOptions> uploadOptions,
		PublicUrl publicUrl,
		ILogger<ApiController> logger) : Controller
	{
		private const string AuthorizationPrefix = "Bearer ";
		private readonly string? _secretKey = uploadOptions.Value.SecretKey;

		[HttpGet("{id?}")]
		[OutputCache(PolicyName = PackageHelper.GalleryGeneratedCachePolicy)]
		public object Get(string id)
		{
			Response.Headers.CacheControl = "no-cache";

			if (string.IsNullOrWhiteSpace(id))
			{
				IReadOnlyList<Package> packages = helper.ListedPackages;

				if (this.IsConditionalGet(packages))
				{
					return Enumerable.Empty<PackageSummary>();
				}

				return packages.Select(PackageSummary.FromPackage);
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
		public async Task<IActionResult> Upload()
		{
			CancellationToken cancellationToken = HttpContext.RequestAborted;
			if (!IsAuthorized())
			{
				return Unauthorized();
			}

			try
			{
				// Avoid MVC's form value providers: even query binding can trigger
				// ReadFormAsync and spool the entire archive before this action runs.
				string? repo = Request.Query["repo"].FirstOrDefault();
				string? issuetracker = Request.Query["issuetracker"].FirstOrDefault();
				string? readmeUrl = Request.Query["readmeUrl"].FirstOrDefault();
				FormOptions limits = HttpContext.RequestServices.GetRequiredService<IOptions<FormOptions>>().Value;
				if (!MediaTypeHeaderValue.TryParse(Request.ContentType, out MediaTypeHeaderValue? contentType) ||
					!contentType.MediaType.Equals("multipart/form-data", StringComparison.OrdinalIgnoreCase))
				{
					throw new InvalidDataException("The upload must use multipart/form-data.");
				}
				string? boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;
				if (string.IsNullOrWhiteSpace(boundary) || boundary.Length > limits.MultipartBoundaryLengthLimit)
				{
					throw new InvalidDataException("The multipart boundary is missing or too long.");
				}

				MultipartReader reader = new(boundary, Request.Body)
				{
					HeadersCountLimit = limits.MultipartHeadersCountLimit,
					HeadersLengthLimit = limits.MultipartHeadersLengthLimit,
					BodyLengthLimit = limits.MultipartBodyLengthLimit,
				};
				int sectionCount = 0;
				Dictionary<string, string> fields = new(StringComparer.OrdinalIgnoreCase);
				MultipartSection? fileSection = await ReadToFileAsync();
				if (fileSection is null)
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
					fileSection.Body,
					repo ?? string.Empty,
					issuetracker ?? string.Empty,
					readmeUrl ?? string.Empty,
					manageToken,
					cancellationToken,
					async () =>
					{
						MultipartSection? extraFile;
						while ((extraFile = await ReadToFileAsync()) is not null)
						{
							await extraFile.Body.CopyToAsync(Stream.Null, cancellationToken);
						}
						return (
							Request.Query.ContainsKey("issuetracker") ? issuetracker ?? string.Empty : fields.GetValueOrDefault("issuetracker", string.Empty),
							Request.Query.ContainsKey("readmeUrl") ? readmeUrl ?? string.Empty : fields.GetValueOrDefault("readmeUrl", string.Empty));
					});

				async Task<MultipartSection?> ReadToFileAsync()
				{
					MultipartSection? section;
					while ((section = await reader.ReadNextSectionAsync(cancellationToken)) is not null)
					{
						if (++sectionCount > limits.ValueCountLimit)
						{
							throw new InvalidDataException("The upload contains too many form sections.");
						}
						if (!ContentDispositionHeaderValue.TryParse(section.ContentDisposition, out ContentDispositionHeaderValue? disposition) ||
							!disposition.DispositionType.Equals("form-data", StringComparison.OrdinalIgnoreCase))
						{
							throw new InvalidDataException("The multipart content disposition is invalid.");
						}
						if (!string.IsNullOrEmpty(HeaderUtilities.RemoveQuotes(disposition.FileName).Value) ||
							!string.IsNullOrEmpty(HeaderUtilities.RemoveQuotes(disposition.FileNameStar).Value))
						{
							return section;
						}

						string key = HeaderUtilities.RemoveQuotes(disposition.Name).Value ?? string.Empty;
						if (key.Length > limits.KeyLengthLimit) throw new InvalidDataException("The form key is too long.");
						Encoding encoding = Encoding.UTF8;
						if (MediaTypeHeaderValue.TryParse(section.ContentType, out MediaTypeHeaderValue? fieldType) && fieldType.Charset.HasValue)
						{
							try
							{
								encoding = Encoding.GetEncoding(HeaderUtilities.RemoveQuotes(fieldType.Charset).Value!);
								if (encoding.CodePage == 65000) encoding = Encoding.UTF8;
							}
							catch (ArgumentException ex)
							{
								throw new InvalidDataException("The form field encoding is invalid.", ex);
							}
						}
						using StreamReader fieldReader = new(section.Body, encoding, detectEncodingFromByteOrderMarks: true, leaveOpen: true);
						StringBuilder value = new();
						char[] buffer = new char[1_024];
						int count;
						while ((count = await fieldReader.ReadAsync(buffer.AsMemory(), cancellationToken)) != 0)
						{
							if (count > limits.ValueLengthLimit - value.Length) throw new InvalidDataException("The form value is too long.");
							value.Append(buffer, 0, count);
						}
						if (key.Equals("issuetracker", StringComparison.OrdinalIgnoreCase) || key.Equals("readmeUrl", StringComparison.OrdinalIgnoreCase))
						{
							fields.TryAdd(key, value.ToString());
						}
					}
					return null;
				}

				// Surface the absolute manage URL so non-Actions publishers can
				// see it directly in the upload response body.
				if (!string.IsNullOrEmpty(package.ManageUrl))
				{
					string baseUrl = publicUrl.GetOrigin(Request);
					if (package.ManageUrl.StartsWith('/'))
					{
						package.ManageUrl = baseUrl + package.ManageUrl;
					}
				}

				return Created(package.DetailsLink, package);
			}
			catch (InvalidDataException ex)
			{
				logger.LogWarning(ex, "Rejected invalid VSIX upload.");
				return InvalidVsixProblem(ex.Message);
			}
			catch (IOException ex) when (ex.Message.StartsWith("Unexpected end of Stream", StringComparison.Ordinal))
			{
				logger.LogWarning(ex, "Rejected truncated multipart upload.");
				return InvalidVsixProblem("The multipart upload ended unexpectedly.");
			}
			catch (BadHttpRequestException ex)
			{
				return Problem(statusCode: ex.StatusCode, title: "Invalid upload", detail: ex.Message);
			}
			catch (System.Xml.XmlException ex)
			{
				logger.LogWarning(ex, "Rejected VSIX upload with an invalid manifest.");
				return InvalidVsixProblem("The .vsixmanifest file is not valid XML.");
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

		private ObjectResult InvalidVsixProblem(string message)
		{
			ProblemDetails problem = new()
			{
				Status = StatusCodes.Status400BadRequest,
				Title = "Invalid VSIX",
				Detail = "The uploaded file could not be published because it is not a valid or safely extractable VSIX package.",
			};
			problem.Extensions["validation"] = new[]
			{
				ValidationFinding.Error("package.invalid", message),
			};

			return BadRequest(problem);
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