using Microsoft.AspNetCore.Mvc;

namespace VsixGallery.Controllers
{
	[Route("badge")]
	public class BadgeController(PackageHelper helper) : Controller
	{
		[HttpGet("{id}.svg")]
		[ResponseCache(Duration = 3600)]
		public IActionResult BadgeSvg(string id)
		{
			string? version = ResolveVersion(id);
			if (version is null)
			{
				return NotFound();
			}

			return Content(BadgeRenderer.RenderSvg(version), "image/svg+xml; charset=utf-8");
		}

		[HttpGet("{id}.png")]
		[ResponseCache(Duration = 3600)]
		public IActionResult BadgePng(string id)
		{
			string? version = ResolveVersion(id);
			if (version is null)
			{
				return NotFound();
			}

			return File(BadgeRenderer.RenderPng(version), "image/png");
		}

		private string? ResolveVersion(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				return null;
			}

			Package? package = helper.GetPackage(id);
			if (package is null)
			{
				return null;
			}

			return package.Version ?? "unknown";
		}
	}
}
