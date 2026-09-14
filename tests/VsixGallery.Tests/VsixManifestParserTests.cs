using System.Xml;

namespace VsixGallery.Tests;

public class VsixManifestParserTests
{
	[Fact]
	public void CreateFromManifest_ParsesMetadataAndResolvesFilesSafely()
	{
		using TemporaryDirectory directory = new();
		string resources = Path.Combine(directory.Path, "Resources");
		Directory.CreateDirectory(resources);
		File.WriteAllText(Path.Combine(resources, "LICENSE.txt"), "MIT");
		File.WriteAllText(
			Path.Combine(directory.Path, "extension.vsixmanifest"),
			"""
			<PackageManifest Version="2.0.0">
			  <Metadata>
			    <Identity Id="Example.Extension" Version="1.2.3" Publisher="Example Publisher" />
			    <DisplayName>Example Extension</DisplayName>
			    <Description>A sufficiently detailed extension description.</Description>
			    <License>Resources\License.txt</License>
			    <MoreInfo>https://github.com/example/project</MoreInfo>
			  </Metadata>
			  <Installation>
			    <InstallationTarget Id="Microsoft.VisualStudio.Community" Version="[17.0,19.0)">
			      <ProductArchitecture>arm64</ProductArchitecture>
			    </InstallationTarget>
			  </Installation>
			</PackageManifest>
			""");

		Package package = new VsixManifestParser().CreateFromManifest(
			directory.Path,
			string.Empty,
			"issues",
			string.Empty);

		Assert.Equal("Example.Extension", package.ID);
		Assert.Equal("MIT", package.License);
		Assert.Equal("https://github.com/example/project", package.Repo);
		Assert.Equal("https://github.com/example/project/issues", package.IssueTracker);
		Assert.Equal(
			"https://raw.githubusercontent.com/example/project/refs/heads/main/README.md",
			package.ReadmeUrl);
		InstallationTarget target = Assert.Single(package.InstallationTargets!);
		Assert.Equal("arm64", target.ProductArchitecture);
	}

	[Fact]
	public void CreateFromManifest_RejectsDocumentTypeDeclarations()
	{
		using TemporaryDirectory directory = new();
		File.WriteAllText(
			Path.Combine(directory.Path, "extension.vsixmanifest"),
			"""
			<!DOCTYPE PackageManifest [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
			<PackageManifest Version="2.0.0">
			  <Metadata>
			    <Identity Id="Example.Extension" Version="1.0" Publisher="Example" />
			    <DisplayName>&xxe;</DisplayName>
			    <Description>Description long enough for this test.</Description>
			  </Metadata>
			</PackageManifest>
			""");

		Assert.Throws<XmlException>(() => new VsixManifestParser().CreateFromManifest(
			directory.Path,
			string.Empty,
			string.Empty,
			string.Empty));
	}

	[Theory]
	[InlineData("../outside.txt")]
	[InlineData("..\\outside.txt")]
	[InlineData("/absolute.txt")]
	public void ResolveRelativeFile_RejectsPathsOutsidePackage(string relativePath)
	{
		using TemporaryDirectory directory = new();

		Assert.Null(VsixManifestParser.ResolveRelativeFile(directory.Path, relativePath));
	}

	private sealed class TemporaryDirectory : IDisposable
	{
		public TemporaryDirectory()
		{
			Path = System.IO.Path.Combine(
				System.IO.Path.GetTempPath(),
				Guid.NewGuid().ToString("N"));
			Directory.CreateDirectory(Path);
		}

		public string Path { get; }

		public void Dispose() => Directory.Delete(Path, recursive: true);
	}
}
