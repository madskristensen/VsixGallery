using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VsixGallery.Pages
{
	[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
	public class NotFoundModel : PageModel
	{
		public void OnGet()
		{
			Response.StatusCode = 404;
		}
	}
}
