using System;

namespace FvmAssistant
{
    internal static class UrlText
    {
        public static bool IsWeb(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps);

        /// <summary>Drops query strings, which on the game pages carry login/session tokens.</summary>
        public static string Redact(string url, bool full = false)
        {
            if (full || string.IsNullOrEmpty(url)) return url;
            if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || !IsWeb(url)) return url.Length > 64 ? url.Substring(0, 64) + "…" : url;
            return u.GetLeftPart(UriPartial.Path) + (u.Query.Length > 1 ? "?…" : "");
        }

        public static string HostOf(string url) =>
            Uri.TryCreate(url, UriKind.Absolute, out var u) && IsWeb(url) ? u.Host : null;
    }
}
