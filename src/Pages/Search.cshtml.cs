
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

using System;
using System.Collections.Generic;
using System.Linq;

namespace VsixGallery.Pages
{
	public class SearchModel : PageModel
	{
		private const int _pageSize = 25;
		private readonly PackageHelper _helper;

		public IEnumerable<Package> Packages { get; private set; } = [];
		public string Term { get; set; } = string.Empty;
		public int Pages { get; private set; }
		public int CurrentPage { get; private set; }

		public SearchModel(PackageHelper helper)
		{
			_helper = helper;
		}

		public void OnGet([FromQuery] string? q, [FromQuery] int page = 1)
		{
			Term = q ?? string.Empty;

			if (string.IsNullOrWhiteSpace(q))
			{
				Packages = [];
				return;
			}

			IEnumerable<Package> listed = _helper.PackageCache.Where(p => !p.Unlisted);
			List<Package> results = [.. Lookup(q, listed).OrderByDescending(p => p.DatePublished)];

			Pages = (results.Count + _pageSize - 1) / _pageSize;
			CurrentPage = Math.Clamp(page, 1, Math.Max(1, Pages));

			Packages = results
				.Skip((CurrentPage - 1) * _pageSize)
				.Take(_pageSize);
		}

		private static IEnumerable<Package> Lookup(string q, IEnumerable<Package> packages)
		{
			// Split into tokens so "git lens" matches packages containing both words.
			string[] tokens = q.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

			Dictionary<Package, int> scores = [];
			foreach (Package package in packages)
			{
				int total = 0;
				foreach (string token in tokens)
				{
					int points = 0;

					if (package.Name.Contains(token, StringComparison.OrdinalIgnoreCase))
					{
						points += 10;
					}
					if (package.Author.Contains(token, StringComparison.OrdinalIgnoreCase))
					{
						points += 5;
					}
					if (package.Description?.Contains(token, StringComparison.OrdinalIgnoreCase) == true)
					{
						points += 3;
					}
					if (package.Tags?.Contains(token, StringComparison.OrdinalIgnoreCase) == true)
					{
						points += 1;
					}

					total += points;
				}

				if (total > 0)
				{
					scores[package] = total;
				}
			}

			return scores.OrderByDescending(e => e.Value).Select(e => e.Key);
		}
	}
}
