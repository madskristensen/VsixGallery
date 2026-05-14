using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VsixGallery.Pages
{
	public class ExtensionModel : PageModel
	{
		private readonly PackageHelper _helper;

		public Package? Package { get; private set; }

		public ExtensionModel(PackageHelper helper)
		{
			_helper = helper;
		}

		public IActionResult OnGet([FromRoute] string id)
		{
			Package = _helper.GetPackage(id);
			return Package is null ? NotFound() : Page();
		}
	}
}
