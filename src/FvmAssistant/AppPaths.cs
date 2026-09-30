using System;
using System.IO;

namespace FvmAssistant
{
    /// <summary>
    /// Runtime data lives in the ignored .fvm-data directory at the repository root.
    /// Profile folders contain login cookies and must never be staged or shared.
    /// </summary>
    internal sealed class AppPaths
    {
        public AppPaths(string profile)
        {
            Root = DataRoot();
            ProfilesRoot = Path.Combine(Root, "profiles");
            ProfileCache = Path.Combine(ProfilesRoot, profile);
            // Keep the original main-account files in place. Other profiles must never read or
            // overwrite its learned cards, settings, captures, or logs.
            AccountRoot = profile.Equals("main", StringComparison.OrdinalIgnoreCase)
                ? Root : Path.Combine(Root, "accounts", profile);
            Captures = Path.Combine(AccountRoot, "captures");
            Logs = Path.Combine(AccountRoot, "logs");
            Cards = Path.Combine(AccountRoot, "cards");
            Tracks = Path.Combine(AccountRoot, "tracks");
            EditedTracks = Path.Combine(AccountRoot, "edited-tracks");

            foreach (var dir in new[] { ProfilesRoot, Path.Combine(Root, "accounts"), ProfileCache, AccountRoot, Captures, Logs, Cards, Tracks, EditedTracks })
                if (Directory.Exists(dir) && (File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidOperationException("Runtime directory cannot be a link: " + dir);
            foreach (var dir in new[] { ProfileCache, Captures, Logs, Cards, Tracks, EditedTracks })
                Directory.CreateDirectory(dir);

            PanelSettings = Path.Combine(AccountRoot, "panel.json");
            LogFile = Path.Combine(Logs, $"assistant-{DateTime.Now:yyyyMMdd}.log");
            CefLogFile = Path.Combine(Logs, "cef.log");
        }

        public static string DataRoot()
        {
            // Resolve from the executable, not the current working directory. The build output
            // is nested below the checkout, while a copied executable can use its own directory.
            for (var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); dir != null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "AGENTS.md")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "src", "FvmAssistant")))
                    return CheckRoot(Path.Combine(dir.FullName, ".fvm-data"));
            return CheckRoot(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".fvm-data"));
        }

        private static string CheckRoot(string path)
        {
            if (Directory.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Runtime root cannot be a link: " + path);
            return path;
        }

        public string Root { get; }
        public string AccountRoot { get; }
        public string ProfilesRoot { get; }
        public string ProfileCache { get; }
        public string Captures { get; }
        public string Logs { get; }

        /// <summary>Learned card icons of this user's cards ("日光炉.png"), used to build decks.</summary>
        public string Cards { get; }
        public string Tracks { get; }
        public string EditedTracks { get; }
        public string PanelSettings { get; }
        public string LogFile { get; }
        public string CefLogFile { get; }
    }
}
