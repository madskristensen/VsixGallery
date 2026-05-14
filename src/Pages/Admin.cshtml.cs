using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace VsixGallery.Pages
{
	public class AdminModel : PageModel
	{
		// A short-lived signed cookie is enough here: the admin page is rarely
		// used and only protects already-soft-deleted data. We avoid pulling in
		// a full ASP.NET Core authentication scheme for a single-password gate.
		private const string CookieName = "vsixgallery.admin";
		private static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(8);

		private readonly PackageHelper _helper;
		private readonly IOptionsMonitor<AdminOptions> _options;

		public AdminModel(PackageHelper helper, IOptionsMonitor<AdminOptions> options)
		{
			_helper = helper;
			_options = options;
		}

		[BindProperty]
		public string? Password { get; set; }

		[BindProperty]
		public string? TrashFolder { get; set; }

		public bool IsConfigured => !string.IsNullOrEmpty(_options.CurrentValue.Password);

		public bool IsAuthenticated { get; private set; }

		public IReadOnlyList<TrashedPackage> Trash { get; private set; } = [];

		public int RetentionDays => Math.Max(1, _options.CurrentValue.TrashRetentionDays);

		public string? ErrorMessage { get; private set; }

		public string? StatusMessage { get; private set; }

		public IActionResult OnGet()
		{
			Authenticate();
			if (IsAuthenticated)
			{
				Trash = _helper.ListTrash();
			}
			return Page();
		}

		public IActionResult OnPostSignIn()
		{
			string configured = _options.CurrentValue.Password ?? string.Empty;
			if (string.IsNullOrEmpty(configured))
			{
				return Page();
			}

			if (string.IsNullOrEmpty(Password) || !FixedTimeEquals(Password, configured))
			{
				ErrorMessage = "Incorrect password.";
				return Page();
			}

			IssueCookie(configured);
			return RedirectToPage();
		}

		public IActionResult OnPostSignOut()
		{
			Response.Cookies.Delete(CookieName);
			return RedirectToPage();
		}

		public IActionResult OnPostRestore()
		{
			Authenticate();
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
			Authenticate();
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

		private void Authenticate()
		{
			string configured = _options.CurrentValue.Password ?? string.Empty;
			if (string.IsNullOrEmpty(configured))
			{
				return;
			}

			if (!Request.Cookies.TryGetValue(CookieName, out string? cookieValue) || string.IsNullOrEmpty(cookieValue))
			{
				return;
			}

			IsAuthenticated = FixedTimeEquals(cookieValue, ExpectedCookieValue(configured));
		}

		private void IssueCookie(string configuredPassword)
		{
			Response.Cookies.Append(
				CookieName,
				ExpectedCookieValue(configuredPassword),
				new CookieOptions
				{
					HttpOnly = true,
					Secure = Request.IsHttps,
					SameSite = SameSiteMode.Strict,
					Expires = DateTimeOffset.UtcNow.Add(CookieLifetime),
					IsEssential = true,
				});

			IsAuthenticated = true;
		}

		// The cookie value is derived from the configured password so that
		// rotating the password instantly invalidates outstanding sessions.
		// SHA-256 is sufficient for this trivial scenario; we are not storing
		// secrets, just deriving a stable session marker.
		private static string ExpectedCookieValue(string configuredPassword)
		{
			byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes("vsixgallery.admin|" + configuredPassword));
			return Convert.ToBase64String(hash);
		}

		private static bool FixedTimeEquals(string a, string b)
		{
			byte[] left = Encoding.UTF8.GetBytes(a);
			byte[] right = Encoding.UTF8.GetBytes(b);
			if (left.Length != right.Length)
			{
				return false;
			}
			return CryptographicOperations.FixedTimeEquals(left, right);
		}
	}
}
