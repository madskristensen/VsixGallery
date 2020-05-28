using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System;
using System.Collections.Generic;
using System.Linq;

namespace VsixGallery.Pages
{
	public class AuthorModel : PageModel
	{
		private readonly PackageHelper _helper;
		public IEnumerable<Package> Packages { get; private set; }
		public string Author { get; set; }

		public AuthorModel(IWebHostEnvironment env)
		{
			_helper = new PackageHelper(env.WebRootPath);
		}


		public void OnGet([FromRoute] string author)
		{
			Packages = _helper.PackageCache.OrderByDescending(p => p.DatePublished)
							  .Where(p => p.Author.Equals(author, StringComparison.OrdinalIgnoreCase));

			if (Packages.Any())
			{
				Author = Packages.First().Author;
			}
			else
			{
				Author = author;
			}
		}
	}
}
