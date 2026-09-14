using System.IO.Compression;
using System.Text;

namespace VsixGallery.Tests;

internal static class TestVsix
{
	public static byte[] Create(
		string id,
		string version,
		string displayName = "Example Extension")
	{
		const string description = "A sufficiently detailed extension description for validation.";
		string manifest =
			$"""
			<?xml version="1.0" encoding="utf-8"?>
			<PackageManifest Version="2.0.0">
			  <Metadata>
			    <Identity Id="{id}" Version="{version}" Language="en-US" Publisher="Example Publisher" />
			    <DisplayName>{displayName}</DisplayName>
			    <Description>{description}</Description>
			    <MoreInfo>https://github.com/example/project</MoreInfo>
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
			using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
			writer.Write(manifest);
		}

		return output.ToArray();
	}
}
