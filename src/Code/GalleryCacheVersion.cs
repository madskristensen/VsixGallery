namespace VsixGallery;

public sealed class GalleryCacheVersion
{
	private long _value;
	private readonly Lock _lock = new();
	private readonly Dictionary<string, long> _extensions = new(StringComparer.Ordinal);
	private readonly Dictionary<string, long> _authors = new(StringComparer.OrdinalIgnoreCase);

	public long Value => Interlocked.Read(ref _value);

	public void Increment() => Interlocked.Increment(ref _value);

	internal void Initialize(IEnumerable<Package> packages)
	{
		lock (_lock)
		{
			foreach (Package package in packages)
			{
				if (package.ID is not null)
				{
					_extensions.TryAdd(package.ID, Value);
				}
				if (!package.Unlisted && !string.IsNullOrWhiteSpace(package.Author))
				{
					_authors.TryAdd(package.Author, Value);
				}
			}
		}
	}

	internal void Change(string id, string? oldAuthor, string? newAuthor, bool exists, IEnumerable<string> liveAuthors)
	{
		lock (_lock)
		{
			Increment();
			if (exists)
			{
				_extensions[id] = Value;
			}
			else
			{
				_extensions.Remove(id);
			}
			foreach (string author in new[] { oldAuthor, newAuthor }.OfType<string>())
			{
				if (liveAuthors.Contains(author, StringComparer.OrdinalIgnoreCase))
				{
					_authors[author] = Value;
				}
				else
				{
					_authors.Remove(author);
				}
			}
		}
	}

	internal long Extension(string? id)
	{
		lock (_lock)
		{
			return id is not null && _extensions.TryGetValue(id, out long version) ? version : Value;
		}
	}

	internal long Author(string? author)
	{
		lock (_lock)
		{
			return author is not null && _authors.TryGetValue(author, out long version) ? version : Value;
		}
	}

	internal static string ExtensionTag(string id) => "extension:" + id;
	internal static string AuthorTag(string author) => "author:" + author.ToUpperInvariant();
}
