namespace VsixGallery
{
	public class ExtensionsOptions
	{
		public string? Directory { get; set; }

		public bool RemoveOldExtensions { get; set; } = true;

		public bool ValidateLicenses { get; set; } = true;

		public long MinimumFreeSpaceBytes { get; set; } = 1_073_741_824;
	}
}
