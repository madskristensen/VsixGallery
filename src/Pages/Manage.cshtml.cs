using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VsixGallery.Pages
{
	public class ManageModel : PageModel
	{
		private readonly PackageHelper _helper;
		private readonly AdminAuth _auth;

		public ManageModel(PackageHelper helper, AdminAuth auth)
		{
			_helper = helper;
			_auth = auth;
		}

		public Package? Package { get; private set; }

		/// <summary>
		/// True when the extension has a stored manage token (i.e. it can be
		/// managed at all). Legacy uploads without a token cannot be deleted
		/// from this page using the per-extension token, but an admin can
		/// still delete it via <see cref="IsAdmin"/>.
		/// </summary>
		public bool HasManageToken { get; private set; }

		/// <summary>
		/// True when the visitor has supplied a token that matches the stored
		/// hash, or supplied / has a session for the configured admin password.
		/// </summary>
		public bool IsAuthenticated { get; private set; }

		/// <summary>
		/// True when the visitor authenticated as the site admin (via cookie
		/// or by typing the admin password into the manage-token prompt).
		/// </summary>
		public bool IsAdmin { get; private set; }

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

			// An admin session unlocks every extension's manage page.
			if (_auth.IsSignedIn(Request))
			{
				IsAuthenticated = true;
				IsAdmin = true;
				return Page();
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

			// An admin session always wins, regardless of the posted token.
			if (_auth.IsSignedIn(Request))
			{
				IsAuthenticated = true;
				IsAdmin = true;
				return Page();
			}

			// Admin password typed into the manage-token field unlocks the
			// page (and any other extension) for the next 8 hours.
			if (!string.IsNullOrEmpty(Token) && _auth.ValidatePassword(Token))
			{
				_auth.IssueCookie(Request, Response);
				IsAuthenticated = true;
				IsAdmin = true;
				Token = null;
				return Page();
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

			bool authorized =
				_auth.IsSignedIn(Request) ||
				(!string.IsNullOrEmpty(Token) && _auth.ValidatePassword(Token)) ||
				(!string.IsNullOrEmpty(Token) && _helper.ValidateManageToken(id, Token));

			if (!authorized)
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
			IsAdmin = _auth.IsSignedIn(Request);
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
