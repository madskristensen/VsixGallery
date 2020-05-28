using Microsoft.AspNetCore.Mvc;

using System;
using System.Collections.Generic;
using System.Linq;

namespace VsixGallery.Controllers
{
	public static class ControllerExtensions
	{
		public static bool IsConditionalGet(this Controller controller, IEnumerable<Package> packages)
		{
			Package package = packages.FirstOrDefault();
			return controller.IsConditionalGet(package);
		}

		public static bool IsConditionalGet(this Controller controller, Package package)
		{
			if (package == null)
				return false;

			string lastmod = package.DatePublished.ToString("r");
			string etag = "\"" + package.DatePublished.Ticks.ToString() + "\"";

			controller.Response.Headers["Last-Modified"] = lastmod;
			controller.Response.Headers["ETag"] = etag;

			// Test If-None-Match
			if (controller.Request.Headers["If-None-Match"] != etag)
			{
				return false;
			}

			// Test Is-Modified-Since
			DateTime lm = package.DatePublished;
			lm = new DateTime(lm.Year, lm.Month, lm.Day, lm.Hour, lm.Minute, lm.Second, DateTimeKind.Utc);

			if (!DateTime.TryParse(controller.Request.Headers["If-Modified-Since"], out DateTime ifModifiedSince) || lm != ifModifiedSince)
			{
				return false;
            }

			controller.Response.StatusCode = 304;

			return true;
		}
	}
}