namespace VsixGallery.Tests;

public class PackageTests
{
	[Fact]
	public void TruncatedDescription_PreservesShortDescription()
	{
		Package package = new() { Description = "A short description" };

		Assert.Equal("A short description", package.TruncatedDescription());
	}

	[Fact]
	public void TruncatedDescription_TruncatesAtWordBoundary()
	{
		Package package = new() { Description = "Alpha beta gamma delta" };

		Assert.Equal("Alpha beta…", package.TruncatedDescription(12));
	}

	[Fact]
	public void TimeAgo_UsesProvidedClock()
	{
		Package package = new() { DatePublished = new DateTime(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc) };
		ManualTimeProvider clock = new(new DateTimeOffset(2026, 9, 13, 12, 0, 0, TimeSpan.Zero));

		Assert.Equal("3 days ago", package.TimeAgo(clock));
	}

	private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
	{
		public override DateTimeOffset GetUtcNow() => utcNow;
	}
}
