using Microsoft.Extensions.Caching.Memory;

namespace VsixGallery;

public sealed class ReadmeCache : IDisposable
{
	private readonly SemaphoreSlim[] _locks = Enumerable.Range(0, 64)
		.Select(_ => new SemaphoreSlim(1, 1)).ToArray();

	public ReadmeCache() : this(16 * 1024 * 1024)
	{
	}

	internal ReadmeCache(long sizeLimit)
	{
		Cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = sizeLimit });
	}

	internal MemoryCache Cache { get; }

	internal SemaphoreSlim GetLock(string key) =>
		_locks[(uint)StringComparer.Ordinal.GetHashCode(key) % (uint)_locks.Length];

	public void Dispose()
	{
		Cache.Dispose();
		foreach (SemaphoreSlim gate in _locks)
		{
			gate.Dispose();
		}
	}
}