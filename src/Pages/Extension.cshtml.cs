using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VsixGallery.Pages
{
	public class ExtensionModel : PageModel
	{
		private readonly PackageHelper _helper;
		private readonly ReadmeService _readmeService;

		public Package? Package { get; private set; }
		public string? ReadmeHtml { get; private set; }

		public ExtensionModel(PackageHelper helper, ReadmeService readmeService)
		{
			_helper = helper;
			_readmeService = readmeService;
		}

		public async Task<IActionResult> OnGetAsync([FromRoute] string id, CancellationToken cancellationToken)
		{
			Package = _helper.GetPackage(id);
			if (Package is null)
			{
				return NotFound();
			}

			ReadmeHtml = await _readmeService.GetSanitizedHtmlAsync(Package.ReadmeUrl, cancellationToken);
			return Page();
		}
	}
}
