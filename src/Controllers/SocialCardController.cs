using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

using System;
using System.IO;

namespace VsixGallery.Controllers
{
	[Route("social")]
	public class SocialCardController : Controller
	{
		// Bump when the renderer output changes to invalidate cached cards.
		private const int CardVersion = 2;
		private static readonly string CardFileName = $"social-v{CardVersion}.png";
		private static readonly string DefaultCardFileName = $"default-social-v{CardVersion}.png";

		private readonly PackageHelper _helper;
		private readonly SocialCardRenderer _renderer;
		private readonly DisplayOptions _display;
		private readonly string _logoPath;

		public SocialCardController(PackageHelper helper, SocialCardRenderer renderer, IOptions<DisplayOptions> display, IWebHostEnvironment env)
		{
			_helper = helper;
			_renderer = renderer;
			_display = display.Value;
			_logoPath = Path.Combine(env.WebRootPath, "img", "icon-192x192.png");
		}

		[HttpGet("extension/{id}.png")]
		public IActionResult Extension(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				return NotFound();
			}

			Package package = _helper.GetPackage(id);
			if (package == null)
			{
				return NotFound();
			}

			string folder = _helper.GetExtensionFolder(id);
			if (folder == null)
			{
				return NotFound();
			}

			string cardPath = Path.Combine(folder, CardFileName);

			if (!System.IO.File.Exists(cardPath) ||
				System.IO.File.GetLastWriteTimeUtc(cardPath) < package.DatePublished.ToUniversalTime())
			{
				try
				{
					string iconPath = _helper.GetIconDiskPath(package);
					byte[] bytes = _renderer.RenderExtensionCard(package, iconPath, _display.SiteName, _logoPath);
					System.IO.File.WriteAllBytes(cardPath, bytes);
				}
				catch (Exception)
				{
					return BuildResult(_renderer.RenderExtensionCard(package, _helper.GetIconDiskPath(package), _display.SiteName, _logoPath));
				}
			}

			Response.Headers.CacheControl = "public, max-age=86400";
			return PhysicalFile(cardPath, "image/png");
		}

		[HttpGet("default.png")]
		public IActionResult Default()
		{
			string cachePath = Path.Combine(Path.GetTempPath(), "vsixgallery-" + DefaultCardFileName);

			if (!System.IO.File.Exists(cachePath))
			{
				byte[] bytes = _renderer.RenderDefaultCard(_display.SiteName, null, _logoPath);
				System.IO.File.WriteAllBytes(cachePath, bytes);
			}

			Response.Headers.CacheControl = "public, max-age=86400";
			return PhysicalFile(cachePath, "image/png");
		}

		private IActionResult BuildResult(byte[] bytes)
		{
			Response.Headers.CacheControl = "public, max-age=86400";
			return File(bytes, "image/png");
		}
	}
}
