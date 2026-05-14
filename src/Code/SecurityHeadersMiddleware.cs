using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace VsixGallery;

public static class SecurityHeadersMiddleware
{
public const string ScriptHashesKey = "CspScriptHashes";

private const string BaseCsp =
"default-src 'self'; " +
"style-src 'self'; " +
"img-src 'self' data: https:; " +
"font-src 'self'; " +
"connect-src 'self' https://markdownservice.azurewebsites.net; " +
"frame-ancestors 'none'; " +
"base-uri 'self'; " +
"form-action 'self'; " +
"object-src 'none'; " +
"require-trusted-types-for 'script'; " +
"trusted-types markdown-html; " +
"upgrade-insecure-requests";

public static IApplicationBuilder UseSecurityHeaders(this IApplicationBuilder app)
{
return app.Use(async (context, next) =>
{
context.Items[ScriptHashesKey] = new List<string>();

context.Response.OnStarting(() =>
{
IHeaderDictionary headers = context.Response.Headers;

if (!headers.ContainsKey("Content-Security-Policy"))
{
List<string> hashes = context.Items[ScriptHashesKey] as List<string> ?? new();
string hashPart = hashes.Count > 0
? " " + string.Join(" ", hashes.ConvertAll(h => $"'sha256-{h}'"))
: string.Empty;

headers["Content-Security-Policy"] = $"script-src 'self'{hashPart}; {BaseCsp}";
}

headers["X-Content-Type-Options"] = "nosniff";
				headers["X-Frame-Options"] = "DENY";
				headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
				headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=(), interest-cohort=()";
				headers["Cross-Origin-Opener-Policy"] = "same-origin";
// Replaces the Arr-Disable-Session-Affinity custom header from web.config.
headers["Arr-Disable-Session-Affinity"] = "true";

return Task.CompletedTask;
});

await next();
});
}

/// <summary>
/// Computes the SHA-256 hash of an inline script body and adds it to the CSP allow-list
/// for the current response, so that the script will be permitted by the Content-Security-Policy header.
/// Call this from a Razor page BEFORE the inline script is rendered.
/// </summary>
public static string AddInlineScriptHash(this HttpContext context, string scriptContent)
{
if (context.Items[ScriptHashesKey] is not List<string> hashes)
{
return string.Empty;
}

byte[] bytes = Encoding.UTF8.GetBytes(scriptContent);
string hash = Convert.ToBase64String(SHA256.HashData(bytes));
hashes.Add(hash);
return hash;
}
}
