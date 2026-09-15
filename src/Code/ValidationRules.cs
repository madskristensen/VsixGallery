namespace VsixGallery
{
	public sealed record ValidationRule(
		string Code,
		string Category,
		string Requirement,
		string Justification);

	public static class ValidationRules
	{
		public static IReadOnlyList<ValidationRule> All { get; } = BuildRules();

		private static readonly IReadOnlyDictionary<string, ValidationRule> _byCode =
			All.ToDictionary(rule => rule.Code, StringComparer.Ordinal);

		public static ValidationRule Get(string code) =>
			_byCode.TryGetValue(code, out ValidationRule? rule)
				? rule
				: throw new ArgumentException($"Validation warning code '{code}' is not documented.", nameof(code));

		private static IReadOnlyList<ValidationRule> BuildRules()
		{
			List<ValidationRule> rules =
			[
				new("manifest.name-missing", "Manifest metadata", "Provide a non-blank display name.", "The name identifies the extension in gallery search results, feeds, and its details page."),
				new("manifest.name-too-long", "Manifest metadata", "Keep the display name at or below 200 characters.", "A bounded name remains usable in gallery navigation, search results, feeds, and API clients."),
				new("manifest.publisher-missing", "Manifest metadata", "Provide a non-blank publisher.", "The publisher identifies who maintains the extension and powers the gallery's author pages."),
				new("manifest.publisher-too-long", "Manifest metadata", "Keep the publisher at or below 200 characters.", "A bounded publisher name remains usable in gallery pages, feeds, and API clients."),
				new("manifest.version-missing", "Manifest metadata", "Provide a non-blank version.", "Visual Studio and gallery clients need a version to determine which package is current."),
				new("manifest.version-too-long", "Manifest metadata", "Keep the version at or below 100 characters.", "A bounded version remains usable in feeds and clients that compare and display package versions."),

				new("icon.missing", "Icon", "Include a square PNG, GIF, or JPEG icon. The preferred display size is 128x128 pixels, and larger source images are supported.", "An icon makes the extension recognizable. Extension galleries display it at no more than 128x128 pixels."),
				new("icon.unsupported-format", "Icon", "Use PNG, GIF, or JPEG for the source icon packaged inside the VSIX.", "These are the icon formats supported by Visual Studio extension manifests. The gallery separately converts the source icon to WebP for display on this website."),
				new("icon.file-missing", "Icon", "Package the icon at the path referenced by the manifest.", "The gallery cannot display an icon that is declared but absent from the VSIX."),
				new("icon.file-too-large", "Icon", "Keep the source icon at or below 10 MB.", "Optimized icons reduce upload processing, storage, and page-transfer costs."),
				new("icon.invalid-dimensions", "Icon", "For best results, use a 128x128 pixel icon or a larger source image.", "Extension galleries display icons at no more than 128x128 pixels. Larger images are supported and scaled down, while smaller images can appear blurry when enlarged."),
				new("icon.not-square", "Icon", "Use an icon with equal width and height.", "Extension galleries display icons in a square area; a square source avoids cropping or distortion."),
				new("icon.invalid-image", "Icon", "Provide a valid, decodable image file.", "The gallery must be able to decode the source image to verify and display it reliably."),
				new("icon.low-contrast-dark-theme", "Icon", "Use colors that remain visible on dark backgrounds.", "Visual Studio's Extension Manager and the gallery show icons directly on dark theme backgrounds. An icon whose visible pixels are almost entirely dark can blend into the page."),
				new("icon.low-contrast-light-theme", "Icon", "Use colors that remain visible on light backgrounds.", "Visual Studio's Extension Manager and the gallery show icons directly on light theme backgrounds. An icon whose visible pixels are almost entirely light can blend into the page."),

				new("description.missing", "Description", "Provide a description that explains what the extension does.", "The description helps users understand the extension before deciding whether to install it."),
				new("description.too-short", "Description", "Write a description of at least 40 characters.", "A meaningful sentence gives users enough context to understand the extension's purpose."),
				new("description.too-long", "Description", "Keep the manifest description at or below 4,000 characters and put detailed documentation in the README.", "The manifest description is a summary used by gallery surfaces and clients; the README is designed for long-form documentation."),

				new("license.missing", "License", "Specify a license in the VSIX manifest.", "A license tells users the terms under which they may use the extension and is shown by extension distribution and installation surfaces."),
			];

			AddUrlRules(rules, "repository", "repository");
			AddUrlRules(rules, "issue-tracker", "issue tracker");
			AddUrlRules(rules, "readme", "README");
			AddUrlRules(rules, "more-info", "more information");

			return rules;
		}

		private static void AddUrlRules(List<ValidationRule> rules, string code, string label)
		{
			const string validUrlReason = "A valid web URL keeps gallery links usable and prevents unsupported schemes or embedded credentials.";
			const string httpsReason = "HTTPS protects users from links whose content or destination could be altered in transit.";
			string requirement = $"Provide a valid HTTP or HTTPS {label} URL without embedded credentials.";
			string secureRequirement = $"Use HTTPS for the {label} URL.";

			rules.Add(new($"url.{code}-invalid", "Links", requirement, validUrlReason));
			rules.Add(new($"url.{code}-insecure", "Links", secureRequirement, httpsReason));
			rules.Add(new($"url.input-{code}-invalid", "Links", requirement, validUrlReason));
			rules.Add(new($"url.input-{code}-insecure", "Links", secureRequirement, httpsReason));
		}
	}
}
