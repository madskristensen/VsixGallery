using System.IO.Compression;
using System.Text;

namespace VsixGallery.Tests;

internal static class TestVsix
{
	public static byte[] Create(
		string id,
		string version,
		string displayName = "Example Extension",
		string description = "A sufficiently detailed extension description for validation.",
		string publisher = "Example Publisher",
		string moreInfo = "https://github.com/example/project",
		string? iconPath = null,
		byte[]? iconBytes = null)
	{
		string iconElement = string.IsNullOrEmpty(iconPath) ? string.Empty : $"<Icon>{iconPath}</Icon>";
		string manifest =
			$"""
			<?xml version="1.0" encoding="utf-8"?>
			<PackageManifest Version="2.0.0">
			  <Metadata>
			    <Identity Id="{id}" Version="{version}" Language="en-US" Publisher="{publisher}" />
			    <DisplayName>{displayName}</DisplayName>
			    <Description>{description}</Description>
			    <MoreInfo>{moreInfo}</MoreInfo>
			    {iconElement}
			  </Metadata>
			  <Installation>
			    <InstallationTarget Id="Microsoft.VisualStudio.Community" Version="[17.0,19.0)" />
			  </Installation>
			</PackageManifest>
			""";

		using MemoryStream output = new();
		using (ZipArchive archive = new(output, ZipArchiveMode.Create, leaveOpen: true))
		{
			ZipArchiveEntry entry = archive.CreateEntry("extension.vsixmanifest");
			using (StreamWriter writer = new(entry.Open(), Encoding.UTF8))
			{
				writer.Write(manifest);
			}

			if (!string.IsNullOrEmpty(iconPath) && iconBytes is not null)
			{
				ZipArchiveEntry icon = archive.CreateEntry(iconPath);
				using Stream iconStream = icon.Open();
				iconStream.Write(iconBytes);
			}
		}

		return output.ToArray();
	}
}
