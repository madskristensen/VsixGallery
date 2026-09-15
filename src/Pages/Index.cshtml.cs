using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Mvc.RazorPages;

using System;
using System.Collections.Generic;
using System.Linq;

namespace VsixGallery.Pages
{
	[OutputCache(PolicyName = "Gallery")]
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
			IReadOnlyList<Package> packages = _helper.ListedPackages;

			int totalCount = packages.Count;
			Pages = Math.Max(1, (totalCount + _pageSize - 1) / _pageSize);
			CurrentPage = Math.Clamp(page, 1, Pages);
			int skip = (CurrentPage - 1) * _pageSize;

			Packages = packages.Skip(skip).Take(_pageSize);

		}
	}
}
