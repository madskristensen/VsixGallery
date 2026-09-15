using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VsixGallery.Pages
{
	[OutputCache(PolicyName = PackageHelper.GalleryPageCachePolicy)]
	public class AuthorModel : PageModel
	{
		private readonly PackageHelper _helper;
		public IEnumerable<Package> Packages { get; private set; } = [];
		public string Author { get; set; } = string.Empty;

		public AuthorModel(PackageHelper helper)
		{
			_helper = helper;
		}


		public void OnGet([FromRoute] string author)
		{
			IReadOnlyList<Package> packages = _helper.GetPackagesByAuthor(author);
			Packages = packages;

			if (packages.Count > 0)
			{
				Author = packages[0].Author ?? string.Empty;
			}
			else
			{
				Author = author;
			}
		}
	}
}
