namespace VsixGallery;

public sealed record PackageSummary(
	string? Id,
	string? Name,
	string? Description,
	string? Author,
	string? Version,
	string? Icon,
	string? Tags,
	DateTime DatePublished,
	string DetailsLink,
	string DownloadLink)
{
	public static PackageSummary FromPackage(Package package) =>
		new(
			package.ID,
			package.Name,
			package.Description,
			package.Author,
			package.Version,
			package.Icon,
			package.Tags,
			package.DatePublished,
			package.DetailsLink,
			package.DownloadLink);
}
