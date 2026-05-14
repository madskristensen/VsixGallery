using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

using System;
using System.Security.Cryptography;
using System.Text;

namespace VsixGallery
{
	/// <summary>
	/// Centralizes admin password validation and the short-lived signed cookie
	/// used by the <c>/admin</c> page. The <c>Manage</c> page also consults this
	/// service so an authenticated admin can manage any extension regardless of
	/// the per-extension manage token, and so that typing the admin password
	/// into the manage-token prompt also unlocks the page.
	/// </summary>
	public class AdminAuth
	{
		public const string CookieName = "vsixgallery.admin";
		public static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(8);

		private readonly IOptionsMonitor<AdminOptions> _options;

		public AdminAuth(IOptionsMonitor<AdminOptions> options)
		{
			_options = options;
		}

		public bool IsConfigured => !string.IsNullOrEmpty(_options.CurrentValue.Password);

		/// <summary>
		/// Returns true when the supplied plaintext matches the configured
		/// admin password. Uses a fixed-time comparison.
		/// </summary>
		public bool ValidatePassword(string? candidate)
		{
			string configured = _options.CurrentValue.Password ?? string.Empty;
			if (string.IsNullOrEmpty(configured) || string.IsNullOrEmpty(candidate))
			{
				return false;
			}

			return FixedTimeEquals(candidate, configured);
		}

		/// <summary>
		/// Returns true when the request carries a valid admin session cookie.
		/// </summary>
		public bool IsSignedIn(HttpRequest request)
		{
			string configured = _options.CurrentValue.Password ?? string.Empty;
			if (string.IsNullOrEmpty(configured))
			{
				return false;
			}

			if (!request.Cookies.TryGetValue(CookieName, out string? cookieValue) || string.IsNullOrEmpty(cookieValue))
			{
				return false;
			}

			return FixedTimeEquals(cookieValue, ExpectedCookieValue(configured));
		}

		/// <summary>
		/// Issues the session cookie for the current request.
		/// </summary>
		public void IssueCookie(HttpRequest request, HttpResponse response)
		{
			string configured = _options.CurrentValue.Password ?? string.Empty;
			if (string.IsNullOrEmpty(configured))
			{
				return;
			}

			response.Cookies.Append(
				CookieName,
				ExpectedCookieValue(configured),
				new CookieOptions
				{
					HttpOnly = true,
					Secure = request.IsHttps,
					SameSite = SameSiteMode.Strict,
					Expires = DateTimeOffset.UtcNow.Add(CookieLifetime),
					IsEssential = true,
				});
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
