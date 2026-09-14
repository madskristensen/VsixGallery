namespace VsixGallery;

public sealed class GalleryCacheVersion
{
	private long _value;

	public long Value => Interlocked.Read(ref _value);

	public void Increment() => Interlocked.Increment(ref _value);
}
