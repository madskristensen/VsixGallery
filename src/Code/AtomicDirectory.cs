namespace VsixGallery;

internal static class AtomicDirectory
{
	public static bool Replace(string preparedDirectory, string destinationDirectory, string rollbackDirectory)
	{
		if (Directory.Exists(rollbackDirectory))
		{
			if (Directory.Exists(destinationDirectory))
			{
				Directory.Delete(rollbackDirectory, recursive: true);
			}
			else
			{
				Directory.CreateDirectory(Path.GetDirectoryName(destinationDirectory)!);
				Directory.Move(rollbackDirectory, destinationDirectory);
			}
		}

		Directory.CreateDirectory(Path.GetDirectoryName(rollbackDirectory)!);
		bool destinationMoved = false;

		if (Directory.Exists(destinationDirectory))
		{
			Directory.Move(destinationDirectory, rollbackDirectory);
			destinationMoved = true;
		}

		try
		{
			Directory.Move(preparedDirectory, destinationDirectory);
		}
		catch
		{
			if (!Directory.Exists(destinationDirectory) && Directory.Exists(rollbackDirectory))
			{
				Directory.Move(rollbackDirectory, destinationDirectory);
			}

			throw;
		}

		if (!destinationMoved)
		{
			return true;
		}

		try
		{
			Directory.Delete(rollbackDirectory, recursive: true);
			return true;
		}
		catch
		{
			return false;
		}
	}
}
