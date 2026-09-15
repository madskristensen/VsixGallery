using Microsoft.AspNetCore.WebUtilities;

namespace VsixGallery
{
	public sealed class PaginationModel
	{
		public required int CurrentPage { get; init; }
		public required int TotalPages { get; init; }
		public required string Path { get; init; }
		public string? SearchTerm { get; init; }
		public string? Fragment { get; init; }

		public string GetUrl(int page)
		{
			Dictionary<string, string?> query = [];

			if (!string.IsNullOrWhiteSpace(SearchTerm))
			{
				query["q"] = SearchTerm;
			}

			if (page > 1)
			{
				query["page"] = page.ToString(System.Globalization.CultureInfo.InvariantCulture);
			}

			string url = query.Count == 0 ? Path : QueryHelpers.AddQueryString(Path, query);
			return string.IsNullOrWhiteSpace(Fragment) ? url : $"{url}#{Fragment}";
		}

		public IEnumerable<int> VisiblePages()
		{
			int first = Math.Max(1, CurrentPage - 2);
			int last = Math.Min(TotalPages, CurrentPage + 2);

			if (last - first < 4)
			{
				first = Math.Max(1, last - 4);
				last = Math.Min(TotalPages, first + 4);
			}

			return Enumerable.Range(first, last - first + 1);
		}
	}
}
