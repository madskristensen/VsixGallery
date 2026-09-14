using System.Diagnostics.CodeAnalysis;

namespace VsixGallery;

internal static class PackagePath
{
	private static readonly StringComparison _pathComparison =
		OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

	public static bool IsValidExtensionId([NotNullWhen(true)] string? id)
	{
		if (string.IsNullOrWhiteSpace(id) || id.Length > 200 || id is "." or "..")
		{
			return false;
		}

		if (id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
			id.IndexOfAny(['<', '>', ':', '"', '|', '?', '*']) >= 0 ||
			id.IndexOfAny(['/', '\\']) >= 0 ||
			id.Any(char.IsControl) ||
			id.EndsWith('.') ||
			id.EndsWith(' '))
		{
			return false;
		}

		return !id.Contains("..", StringComparison.Ordinal);
	}

	public static string GetContainedPath(string root, string relativePath)
	{
		if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
		{
			throw new InvalidDataException("Package paths must be relative.");
		}

		string fullRoot = Path.GetFullPath(root);
		string fullPath = Path.GetFullPath(Path.Combine(fullRoot, relativePath));
		string rootPrefix = Path.TrimEndingDirectorySeparator(fullRoot) + Path.DirectorySeparatorChar;

		if (!fullPath.StartsWith(rootPrefix, _pathComparison))
		{
			throw new InvalidDataException("A package path escaped its allowed directory.");
		}

		return fullPath;
	}
}
