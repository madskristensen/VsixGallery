using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using System;
using System.Threading;
using System.Threading.Tasks;

namespace VsixGallery
{
	/// <summary>
	/// Periodically purges soft-deleted extensions from the <c>.trash</c> bin
	/// once they exceed <see cref="AdminOptions.TrashRetentionDays"/>.
	/// Runs once at startup (after a short delay) and then once per day.
	/// </summary>
	public class TrashCleanupService : BackgroundService
	{
		private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);
		private static readonly TimeSpan SweepInterval = TimeSpan.FromHours(24);

		private readonly PackageHelper _helper;
		private readonly IOptionsMonitor<AdminOptions> _options;
		private readonly ILogger<TrashCleanupService> _logger;

		public TrashCleanupService(
			PackageHelper helper,
			IOptionsMonitor<AdminOptions> options,
			ILogger<TrashCleanupService> logger)
		{
			_helper = helper;
			_options = options;
			_logger = logger;
		}

		protected override async Task ExecuteAsync(CancellationToken stoppingToken)
		{
			try
			{
				await Task.Delay(StartupDelay, stoppingToken);
			}
			catch (OperationCanceledException)
			{
				return;
			}

			while (!stoppingToken.IsCancellationRequested)
			{
				try
				{
					int retentionDays = Math.Max(1, _options.CurrentValue.TrashRetentionDays);
					DateTime cutoff = DateTime.UtcNow.AddDays(-retentionDays);

					int purged = _helper.PurgeOlderThan(cutoff);
					if (purged > 0)
					{
						_logger.LogInformation(
							"Trash cleanup purged {Count} extension folder(s) older than {RetentionDays} day(s).",
							purged,
							retentionDays);
					}
				}
				catch (Exception ex)
				{
					_logger.LogError(ex, "Trash cleanup sweep failed.");
				}

				try
				{
					await Task.Delay(SweepInterval, stoppingToken);
				}
				catch (OperationCanceledException)
				{
					return;
				}
			}
		}
	}
}
