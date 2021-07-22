using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Primitives;
using System;
using System.IO;

namespace VsixGallery
{
	public class ExtensionsFileProvider : IFileProvider
	{
		private static readonly char[] _pathSeparators = new[] { '/', '\\' };

		private readonly IFileProvider _underlyingFileProvider;

		public ExtensionsFileProvider(string extensionsDirectory)
		{
			_underlyingFileProvider = new PhysicalFileProvider(extensionsDirectory);
		}

		public IDirectoryContents GetDirectoryContents(string subpath)
		{
			throw new NotSupportedException();
		}

		public IFileInfo GetFileInfo(string subpath)
		{
			if (string.IsNullOrEmpty(subpath))
			{
				return new NotFoundFileInfo(subpath);
			}

			subpath = subpath.TrimStart(_pathSeparators);

			// If the sub-path starts with the default extensions path,
			// then trim that segment from the path and pass the
			// remainder down to the underlying file system provider.
			foreach (char separator in _pathSeparators)
			{
				if (subpath.StartsWith(PackageHelper.DefaultExtensionsPath + separator))
				{
					return _underlyingFileProvider.GetFileInfo(subpath.Substring(PackageHelper.DefaultExtensionsPath.Length + 1));
				}
			}

			return new NotFoundFileInfo(subpath);
		}

		public IChangeToken Watch(string filter)
		{
			return NullChangeToken.Singleton;
		}
	}
}
