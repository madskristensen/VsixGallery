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
		private const int _pageSize = 24;

		private readonly PackageHelper _helper;
		public IEnumerable<Package> Packages { get; private set; }
		public int Pages { get; private set; }
		public int CurrentPage { get; private set; }

		public IndexModel(IWebHostEnvironment env)
		{
			_helper = new PackageHelper(env.WebRootPath);
		}

		public void OnGet([FromQuery] int page = 1)
		{
			HttpContext.EnableOutputCaching(TimeSpan.FromDays(7), fileDependencies: "wwwroot/extensions", varyByParam: "page");
			IEnumerable<Package> packages = _helper.PackageCache.Where(p => !p.Unlisted);

			int skip = (page - 1) * _pageSize;
			int take = page * _pageSize;
			Packages = packages.OrderByDescending(p => p.DatePublished)
							  .Skip(skip)
							  .Take(_pageSize);

			Pages = packages.Count() / _pageSize;
			CurrentPage = page;
		}
	}
}
