using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VsixGallery.Pages
{
	public class ManageModel : PageModel
	{
		private readonly PackageHelper _helper;

		public ManageModel(PackageHelper helper)
		{
			_helper = helper;
		}

		public Package? Package { get; private set; }

		/// <summary>
		/// True when the extension has a stored manage token (i.e. it can be
		/// managed at all). Legacy uploads without a token cannot be deleted
		/// from this page.
		/// </summary>
		public bool HasManageToken { get; private set; }

		/// <summary>
		/// True when the visitor has supplied a token that matches the stored
		/// hash. The plaintext token is kept on the page only for the duration
		/// of the request so the delete handler can re-validate.
		/// </summary>
		public bool IsAuthenticated { get; private set; }

		[BindProperty]
		public string? Token { get; set; }

		public string? ErrorMessage { get; private set; }

		public bool Deleted { get; private set; }

		public IActionResult OnGet([FromRoute] string id, [FromQuery] string? token)
		{
			if (!Load(id))
			{
				return NotFound();
			}

			// Allow tokens passed in the URL for the auto-generated case.
			if (!string.IsNullOrEmpty(token) && _helper.ValidateManageToken(id, token))
			{
				IsAuthenticated = true;
				Token = token;
			}

			return Page();
		}

		public IActionResult OnPost([FromRoute] string id)
		{
			if (!Load(id))
			{
				return NotFound();
			}

			if (string.IsNullOrEmpty(Token) || !_helper.ValidateManageToken(id, Token))
			{
				ErrorMessage = "That manage token is not valid for this extension.";
				return Page();
			}

			IsAuthenticated = true;
			return Page();
		}

		public IActionResult OnPostDelete([FromRoute] string id)
		{
			if (!Load(id))
			{
				return NotFound();
			}

			if (string.IsNullOrEmpty(Token) || !_helper.ValidateManageToken(id, Token))
			{
				ErrorMessage = "That manage token is not valid for this extension.";
				return Page();
			}

			_helper.SoftDelete(id);

			// Reload state so the UI can show the success message; the Package
			// is now gone from the cache so re-resolving will return null.
			Package = null;
			HasManageToken = false;
			IsAuthenticated = false;
			Deleted = true;
			return Page();
		}

		private bool Load(string id)
		{
			Package = _helper.GetPackage(id);
			if (Package is null)
			{
				return false;
			}

			HasManageToken = _helper.HasManageToken(id);
			return true;
		}
	}
}
