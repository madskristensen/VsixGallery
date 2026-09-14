namespace VsixGallery.Tests;

public class AtomicDirectoryTests
{
	[Fact]
	public void Replace_SwapsPreparedContentAndRemovesRollback()
	{
		string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string prepared = Path.Combine(root, "prepared");
		string destination = Path.Combine(root, "live");
		string rollback = Path.Combine(root, "rollback", "extension");

		try
		{
			Directory.CreateDirectory(prepared);
			Directory.CreateDirectory(destination);
			File.WriteAllText(Path.Combine(prepared, "version.txt"), "new");
			File.WriteAllText(Path.Combine(destination, "version.txt"), "old");

			bool cleanupSucceeded = AtomicDirectory.Replace(prepared, destination, rollback);

			Assert.True(cleanupSucceeded);
			Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "version.txt")));
			Assert.False(Directory.Exists(prepared));
			Assert.False(Directory.Exists(rollback));
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}

	[Fact]
	public void Replace_RestoresExistingRollbackBeforeStartingSwap()
	{
		string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string prepared = Path.Combine(root, "prepared");
		string destination = Path.Combine(root, "live");
		string rollback = Path.Combine(root, "rollback", "extension");

		try
		{
			Directory.CreateDirectory(prepared);
			Directory.CreateDirectory(rollback);
			File.WriteAllText(Path.Combine(prepared, "version.txt"), "new");
			File.WriteAllText(Path.Combine(rollback, "version.txt"), "recoverable");

			bool cleanupSucceeded = AtomicDirectory.Replace(prepared, destination, rollback);

			Assert.True(cleanupSucceeded);
			Assert.Equal("new", File.ReadAllText(Path.Combine(destination, "version.txt")));
			Assert.False(Directory.Exists(rollback));
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}

	[Fact]
	public void Replace_RestoresDestinationWhenPreparedMoveFails()
	{
		string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		string missingPrepared = Path.Combine(root, "missing");
		string destination = Path.Combine(root, "live");
		string rollback = Path.Combine(root, "rollback", "extension");

		try
		{
			Directory.CreateDirectory(destination);
			File.WriteAllText(Path.Combine(destination, "version.txt"), "old");

			Assert.Throws<DirectoryNotFoundException>(() =>
				AtomicDirectory.Replace(missingPrepared, destination, rollback));

			Assert.Equal("old", File.ReadAllText(Path.Combine(destination, "version.txt")));
			Assert.False(Directory.Exists(rollback));
		}
		finally
		{
			if (Directory.Exists(root))
			{
				Directory.Delete(root, recursive: true);
			}
		}
	}
}
