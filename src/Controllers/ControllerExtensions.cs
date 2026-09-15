using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace VsixGallery.Controllers
{
	public static class ControllerExtensions
	{
		public static bool IsConditionalGet(this Controller controller, IEnumerable<Package> packages)
		{
			Package[] snapshot = packages as Package[] ?? [.. packages];
			DateTime lastModified = snapshot.Length == 0
				? DateTime.UnixEpoch
				: snapshot.Max(p => p.DatePublished);

			using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
			foreach (Package package in snapshot)
			{
				string value = $"{package.ID}\0{package.Version}\0{package.DatePublished.ToUniversalTime().Ticks}\0{package.Unlisted}\n";
				hash.AppendData(Encoding.UTF8.GetBytes(value));
			}
			string etag = $"\"{Convert.ToHexStringLower(hash.GetHashAndReset())}\"";

			return IsConditionalGet(controller, lastModified, etag);
		}

		public static bool IsConditionalGet(this Controller controller, Package? package)
		{
			if (package == null)
			{
				return false;
			}

			DateTime published = package.DatePublished.ToUniversalTime();
			string etag = $"\"{published.Ticks.ToString(CultureInfo.InvariantCulture)}\"";
			return IsConditionalGet(controller, published, etag);
		}

		private static bool IsConditionalGet(Controller controller, DateTime modified, string etag)
		{
			DateTime lastModified = modified.ToUniversalTime();
			lastModified = new DateTime(
				lastModified.Year,
				lastModified.Month,
				lastModified.Day,
				lastModified.Hour,
				lastModified.Minute,
				lastModified.Second,
				DateTimeKind.Utc);

			controller.Response.Headers.LastModified = lastModified.ToString("r", CultureInfo.InvariantCulture);
			controller.Response.Headers.ETag = etag;

			IList<EntityTagHeaderValue>? requestedEtags = controller.Request.GetTypedHeaders().IfNoneMatch;
			if (requestedEtags is { Count: > 0 })
			{
				bool matches = requestedEtags.Any(candidate =>
					candidate == EntityTagHeaderValue.Any ||
					string.Equals(candidate.Tag.ToString(), etag, StringComparison.Ordinal));

				if (!matches)
				{
					return false;
				}

				controller.Response.StatusCode = StatusCodes.Status304NotModified;
				return true;
			}

			if (!DateTimeOffset.TryParse(
					controller.Request.Headers.IfModifiedSince,
					CultureInfo.InvariantCulture,
					DateTimeStyles.AssumeUniversal,
					out DateTimeOffset ifModifiedSince) ||
				lastModified > ifModifiedSince.UtcDateTime)
			{
				return false;
			}

			controller.Response.StatusCode = StatusCodes.Status304NotModified;
			return true;
		}
	}
}
