using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;

namespace VsixGallery.Pages
{
	public class AdminModel : PageModel
	{
		private readonly PackageHelper _helper;
		private readonly AdminAuth _auth;
		private readonly IOptionsMonitor<AdminOptions> _options;

		public AdminModel(PackageHelper helper, AdminAuth auth, IOptionsMonitor<AdminOptions> options)
		{
			_helper = helper;
			_auth = auth;
			_options = options;
		}

		[BindProperty]
		public string? Password { get; set; }

		[BindProperty]
		public string? TrashFolder { get; set; }

		public bool IsConfigured => _auth.IsConfigured;

		public bool IsAuthenticated { get; private set; }

		public IReadOnlyList<TrashedPackage> Trash { get; private set; } = [];

		public int RetentionDays => Math.Max(1, _options.CurrentValue.TrashRetentionDays);

		public string? ErrorMessage { get; private set; }

		public string? StatusMessage { get; private set; }

		public IActionResult OnGet()
		{
			IsAuthenticated = _auth.IsSignedIn(Request);
			if (IsAuthenticated)
			{
				Trash = _helper.ListTrash();
			}
			return Page();
		}

		public IActionResult OnPostSignIn()
		{
			if (!_auth.IsConfigured)
			{
				return Page();
			}

			if (!_auth.ValidatePassword(Password))
			{
				ErrorMessage = "Incorrect password.";
				return Page();
			}

			_auth.IssueCookie(Request, Response);
			return RedirectToPage();
		}

		public IActionResult OnPostSignOut()
		{
			Response.Cookies.Delete(AdminAuth.CookieName);
			return RedirectToPage();
		}

		public IActionResult OnPostRestore()
		{
			IsAuthenticated = _auth.IsSignedIn(Request);
			if (!IsAuthenticated)
			{
				return Forbid();
			}

			if (!string.IsNullOrEmpty(TrashFolder) && _helper.Restore(TrashFolder))
			{
				StatusMessage = $"Restored '{TrashFolder}'.";
			}
			else
			{
				ErrorMessage = $"Could not restore '{TrashFolder}'.";
			}

			Trash = _helper.ListTrash();
			return Page();
		}

		public IActionResult OnPostHardDelete()
		{
			IsAuthenticated = _auth.IsSignedIn(Request);
			if (!IsAuthenticated)
			{
				return Forbid();
			}

			if (!string.IsNullOrEmpty(TrashFolder) && _helper.HardDelete(TrashFolder))
			{
				StatusMessage = $"Permanently deleted '{TrashFolder}'.";
			}
			else
			{
				ErrorMessage = $"Could not delete '{TrashFolder}'.";
			}

			Trash = _helper.ListTrash();
			return Page();
		}
	}
}
