using Microsoft.AspNetCore.Mvc;

using System.Text;

namespace VsixGallery.Controllers
{
	[Route("badge")]
	public class BadgeController(PackageHelper helper) : Controller
	{
		private const int LabelWidth = 64;
		private const int BadgeHeight = 20;
		private const int Padding = 6;
		private const int LogoX = 6;
		private const int LogoY = 4;
		private const int LogoSize = 12;
		private const int LabelTextX = 22;
		private const int Radius = 3;
		private const string LabelBg = "#555";
		private const string ValueBg = "#007ec6";
		private const string TextColor = "#fff";
		private const string FontFamily = "DejaVu Sans,Verdana,Geneva,sans-serif";
		private const int FontSize = 11;

		[HttpGet("{id}.svg")]
		[ResponseCache(Duration = 3600)]
		public IActionResult Badge(string id)
		{
			if (string.IsNullOrWhiteSpace(id))
			{
				return NotFound();
			}

			Package? package = helper.GetPackage(id);
			if (package is null)
			{
				return NotFound();
			}

			string version = package.Version ?? "unknown";
			int valueWidth = EstimateTextWidth(version) + (Padding * 2);
			int totalWidth = LabelWidth + valueWidth;

			string svg = BuildSvg(version, totalWidth, valueWidth);

			return Content(svg, "image/svg+xml; charset=utf-8");
		}

		private static string BuildSvg(string version, int totalWidth, int valueWidth)
		{
			var sb = new StringBuilder();
			sb.Append($"""
				<svg xmlns="http://www.w3.org/2000/svg" width="{totalWidth}" height="{BadgeHeight}" role="img" aria-label="version: {version}">
				  <title>version: {version}</title>
				  <linearGradient id="s" x2="0" y2="100%">
					<stop offset="0" stop-color="#bbb" stop-opacity=".1"/>
					<stop offset="1" stop-opacity=".1"/>
				  </linearGradient>
				  <clipPath id="r">
					<rect width="{totalWidth}" height="{BadgeHeight}" rx="{Radius}" fill="{TextColor}"/>
				  </clipPath>
				  <g clip-path="url(#r)">
					<rect width="{LabelWidth}" height="{BadgeHeight}" fill="{LabelBg}"/>
					<rect x="{LabelWidth}" width="{valueWidth}" height="{BadgeHeight}" fill="{ValueBg}"/>
					<rect width="{totalWidth}" height="{BadgeHeight}" fill="url(#s)"/>
				  </g>
				  <svg x="{LogoX}" y="{LogoY}" width="{LogoSize}" height="{LogoSize}" viewBox="0 0 256 256" aria-hidden="true">
					<rect x="30" y="30" width="90" height="90" fill="#09F"/>
					<rect x="30" y="136" width="90" height="90" fill="#09F"/>
					<rect x="136" y="136" width="90" height="90" fill="#09F"/>
					<rect x="151" y="15" width="90" height="90" fill="#A8D4FF" transform="rotate(45 196 60)"/>
				  </svg>
				  <g fill="{TextColor}" font-family="{FontFamily}" font-size="{FontSize}">
					<text x="{LabelTextX}" y="15" fill="#010101" fill-opacity=".3">version</text>
					<text x="{LabelTextX}" y="14">version</text>
					<text x="{LabelWidth + valueWidth / 2}" y="15" fill="#010101" fill-opacity=".3" text-anchor="middle">{version}</text>
					<text x="{LabelWidth + valueWidth / 2}" y="14" text-anchor="middle">{version}</text>
				  </g>
				</svg>
				""");
			return sb.ToString();
		}

		// Approximate pixel width for a string rendered at FontSize 11 in DejaVu Sans.
		// Average character width is ~6.5px at this size.
		private static int EstimateTextWidth(string text) =>
			(int)Math.Ceiling(text.Length * 6.5);
	}
}
