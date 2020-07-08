
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using System;
using System.Collections.Generic;
using System.Linq;

namespace VsixGallery.Pages
{
	public class SearchModel : PageModel
	{
		private readonly PackageHelper _helper;
		public IEnumerable<Package> Packages { get; private set; }
		public string Term { get; set; }

		public SearchModel(IWebHostEnvironment env)
		{
			_helper = new PackageHelper(env.WebRootPath);
		}


		public void OnGet([FromQuery] string q)
		{
			IOrderedEnumerable<Package> packages = _helper.PackageCache
								  .Where(p => p.Unlisted == false)
								  .OrderByDescending(p => p.DatePublished);

			Packages = Lookup(q, packages);
			Term = q;
		}

		private IEnumerable<Package> Lookup(string q, IEnumerable<Package> packages)
		{
			Dictionary<Package, int> list = new Dictionary<Package, int>();
			foreach (Package package in packages)
			{
				int points = 0;

				if (package.Name.Contains(q, StringComparison.OrdinalIgnoreCase))
				{
					points += 10;
				}
				else if (package.Author.Contains(q, StringComparison.OrdinalIgnoreCase))
				{
					points += 5;
				}
				else if ((package.Tags ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
				{
					points += 1;
				}

				list.Add(package, points);
			}

			IOrderedEnumerable<KeyValuePair<Package, int>> sorted = list.Where(e => e.Value > 0)
							 .OrderByDescending(e => e.Value);

			return sorted.Select(e => e.Key);
		}
	}
}
