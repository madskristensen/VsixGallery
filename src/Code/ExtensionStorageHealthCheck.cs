using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

namespace VsixGallery;

public sealed class ExtensionStorageHealthCheck(
	PackageHelper packageHelper,
	IOptions<ExtensionsOptions> options) : IHealthCheck
{
	public Task<HealthCheckResult> CheckHealthAsync(
		HealthCheckContext context,
		CancellationToken cancellationToken = default) =>
		CheckStorageAsync(
			packageHelper.ExtensionRoot,
			Math.Max(0, options.Value.MinimumFreeSpaceBytes),
			packageHelper.PackageCache.Count,
			cancellationToken);

	internal static async Task<HealthCheckResult> CheckStorageAsync(
		string extensionRoot,
		long minimumFreeSpaceBytes,
		int packageCount,
		CancellationToken cancellationToken)
	{
		Dictionary<string, object> data = new()
		{
			["packageCount"] = packageCount,
			["minimumFreeSpaceBytes"] = minimumFreeSpaceBytes,
		};

		try
		{
			if (!Directory.Exists(extensionRoot))
			{
				return HealthCheckResult.Unhealthy("Extension storage is unavailable.", data: data);
			}

			_ = Directory.EnumerateFileSystemEntries(extensionRoot).Take(1).ToArray();

			string probePath = Path.Combine(extensionRoot, $".health-{Guid.NewGuid():N}.tmp");
			try
			{
				await using FileStream probe = new(
					probePath,
					FileMode.CreateNew,
					FileAccess.Write,
					FileShare.None,
					bufferSize: 1,
					FileOptions.Asynchronous | FileOptions.WriteThrough);
				await probe.WriteAsync(new byte[] { 0 }, cancellationToken);
				await probe.FlushAsync(cancellationToken);
			}
			finally
			{
				File.Delete(probePath);
			}

			try
			{
				DriveInfo[] drives = DriveInfo.GetDrives();
				string? driveRoot = FindContainingDriveRoot(
					extensionRoot,
					drives.Select(static drive => drive.RootDirectory.FullName));
				if (driveRoot is null)
				{
					return HealthCheckResult.Unhealthy("Extension storage capacity could not be determined.", data: data);
				}

				DriveInfo? drive = drives.FirstOrDefault(candidate =>
					string.Equals(
						Path.TrimEndingDirectorySeparator(candidate.RootDirectory.FullName),
						Path.TrimEndingDirectorySeparator(driveRoot),
						OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal));
				if (drive is null)
				{
					return HealthCheckResult.Unhealthy("Extension storage capacity could not be determined.", data: data);
				}

				long freeSpaceBytes = drive.AvailableFreeSpace;
				data["freeSpaceBytes"] = freeSpaceBytes;
				if (freeSpaceBytes < minimumFreeSpaceBytes)
				{
					return HealthCheckResult.Unhealthy("Extension storage is below its free-space threshold.", data: data);
				}
			}
			catch (Exception ex) when (ex is ArgumentException or IOException)
			{
				return HealthCheckResult.Unhealthy("Extension storage capacity could not be determined.", ex, data);
			}

			return HealthCheckResult.Healthy("Extension storage is readable and writable.", data);
		}
		catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
		{
			throw;
		}
		catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
		{
			return HealthCheckResult.Unhealthy("Extension storage is not readable and writable.", ex, data);
		}
	}

	internal static string? FindContainingDriveRoot(string path, IEnumerable<string> driveRoots)
	{
		string fullPath = Path.GetFullPath(path);
		StringComparison comparison = OperatingSystem.IsWindows()
			? StringComparison.OrdinalIgnoreCase
			: StringComparison.Ordinal;

		return driveRoots
			.Select(Path.GetFullPath)
			.Where(root =>
			{
				string normalizedRoot = Path.TrimEndingDirectorySeparator(root);
				string rootPrefix = Path.EndsInDirectorySeparator(root)
					? root
					: root + Path.DirectorySeparatorChar;
				return string.Equals(
						Path.TrimEndingDirectorySeparator(fullPath),
						normalizedRoot,
						comparison) ||
					fullPath.StartsWith(rootPrefix, comparison);
			})
			.OrderByDescending(static root => root.Length)
			.FirstOrDefault();
	}
}
