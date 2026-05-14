namespace VsixGallery
{
	public class AdminOptions
	{
		/// <summary>
		/// When set, enables the <c>/admin</c> page. Set via User Secrets in
		/// development and via App Service configuration in production.
		/// When null or empty the admin page returns 404.
		/// </summary>
		public string? Password { get; set; }

		/// <summary>
		/// Number of days a soft-deleted extension remains in the trash
		/// before <see cref="TrashCleanupService"/> permanently removes it.
		/// </summary>
		public int TrashRetentionDays { get; set; } = 30;
	}
}
