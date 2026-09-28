using System;
using System.IO;

namespace FvmAssistant
{
    /// <summary>
    /// Runtime data lives under %LOCALAPPDATA%\FvmAssistant, never in the repo:
    /// the profile folders hold login cookies and captures can show account names.
    /// </summary>
    internal sealed class AppPaths
    {
        public AppPaths(string profile)
        {
            Root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FvmAssistant");
            ProfilesRoot = Path.Combine(Root, "profiles");
            ProfileCache = Path.Combine(ProfilesRoot, profile);
            // Keep the original main-account files in place. Other profiles must never read or
            // overwrite its learned cards, settings, captures, or logs.
            AccountRoot = profile.Equals("main", StringComparison.OrdinalIgnoreCase)
                ? Root : Path.Combine(Root, "accounts", profile);
            Captures = Path.Combine(AccountRoot, "captures");
            Logs = Path.Combine(AccountRoot, "logs");
            Cards = Path.Combine(AccountRoot, "cards");

            foreach (var dir in new[] { ProfileCache, Captures, Logs, Cards })
                Directory.CreateDirectory(dir);

            PanelSettings = Path.Combine(AccountRoot, "panel.json");
            LogFile = Path.Combine(Logs, $"assistant-{DateTime.Now:yyyyMMdd}.log");
            CefLogFile = Path.Combine(Logs, "cef.log");
        }

        public string Root { get; }
        public string AccountRoot { get; }
        public string ProfilesRoot { get; }
        public string ProfileCache { get; }
        public string Captures { get; }
        public string Logs { get; }

        /// <summary>Learned card icons of this user's cards ("日光炉.png"), used to build decks.</summary>
        public string Cards { get; }
        public string PanelSettings { get; }
        public string LogFile { get; }
        public string CefLogFile { get; }
    }
}
