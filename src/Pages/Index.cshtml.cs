using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using System;
using System.Collections.Generic;
using System.Linq;

using WebEssentials.AspNetCore.OutputCaching;

namespace VsixGallery.Pages
{
	public class IndexModel : PageModel
	{
		private const int _pageSize = 18;

		private readonly PackageHelper _helper;
		public IEnumerable<Package> Packages { get; private set; } = [];
		public int Pages { get; private set; }
		public int CurrentPage { get; private set; }

		public IndexModel(PackageHelper helper)
		{
			_helper = helper;
		}

		public void OnGet([FromQuery] int page = 1)
		{
			HttpContext.EnableOutputCaching(TimeSpan.FromDays(7), fileProvider: _helper.FileProvider, fileDependencies: "*", varyByParam: "page");
			List<Package> packages = [.. _helper.PackageCache.Where(p => !p.Unlisted)];

			int totalCount = packages.Count;
			Pages = Math.Max(1, (totalCount + _pageSize - 1) / _pageSize);
			CurrentPage = Math.Clamp(page, 1, Pages);
			int skip = (CurrentPage - 1) * _pageSize;

			Packages = packages.OrderByDescending(p => p.DatePublished)
							  .Skip(skip)
							  .Take(_pageSize);

		}
	}
}
