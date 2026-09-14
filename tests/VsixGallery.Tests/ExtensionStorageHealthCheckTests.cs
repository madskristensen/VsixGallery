using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace VsixGallery.Tests;

public class ExtensionStorageHealthCheckTests
{
	[Fact]
	public async Task CheckStorageAsync_ReportsHealthyWritableStorage()
	{
		string root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
		Directory.CreateDirectory(root);

		try
		{
			HealthCheckResult result = await ExtensionStorageHealthCheck.CheckStorageAsync(
				root,
				minimumFreeSpaceBytes: 0,
				packageCount: 3,
				CancellationToken.None);

			Assert.Equal(HealthStatus.Healthy, result.Status);
			Assert.Equal(3, result.Data["packageCount"]);
			Assert.Empty(Directory.EnumerateFiles(root, ".health-*.tmp"));
		}
		finally
		{
			Directory.Delete(root, recursive: true);
		}
	}

	[Fact]
	public async Task CheckStorageAsync_ReportsMissingStorageAsUnhealthy()
	{
		string missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

		HealthCheckResult result = await ExtensionStorageHealthCheck.CheckStorageAsync(
			missing,
			minimumFreeSpaceBytes: 0,
			packageCount: 0,
			CancellationToken.None);

		Assert.Equal(HealthStatus.Unhealthy, result.Status);
	}

	[Fact]
	public void FindContainingDriveRoot_UsesLongestContainingMount()
	{
		string result = ExtensionStorageHealthCheck.FindContainingDriveRoot(
			Path.Combine(Path.DirectorySeparatorChar.ToString(), "mnt", "extensions", "packages"),
			[
				Path.DirectorySeparatorChar.ToString(),
				Path.Combine(Path.DirectorySeparatorChar.ToString(), "mnt", "extensions"),
				Path.Combine(Path.DirectorySeparatorChar.ToString(), "mnt", "other"),
			])!;

		Assert.Equal(
			Path.GetFullPath(Path.Combine(Path.DirectorySeparatorChar.ToString(), "mnt", "extensions")),
			result);
	}
}
