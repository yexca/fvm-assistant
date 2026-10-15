using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FvmAssistant.Controller
{
    internal sealed class AccountEntry
    {
        public string Profile { get; set; } = "main";
        public int Port { get; set; } = 18765;
        public string Server { get; set; } = "游戏入口";
        public string EntryUrl { get; set; } = AssistantOptions.DefaultUrl;
        public override string ToString() => Profile + " · " + Server;
    }

    internal sealed class QueueEntry
    {
        public string Profile { get; set; }
        public string Level { get; set; }
        public string Track { get; set; }
        public string Cards { get; set; }
        public int Rounds { get; set; } = 1;
        public bool Shovel { get; set; }
        public bool Boss { get; set; }
        public bool AutoFlame { get; set; }
    }

    /// <summary>Controller metadata only. Login state remains opaque in CEF's per-profile directory.</summary>
    internal sealed class ControllerState
    {
        public List<AccountEntry> Accounts { get; set; } = new List<AccountEntry>();
        public List<QueueEntry> Queue { get; set; } = new List<QueueEntry>();
        public Dictionary<string, bool> SuccessfulTracks { get; set; } = new Dictionary<string, bool>();
        public Dictionary<string, string> TrackLevels { get; set; } = new Dictionary<string, string>();

        public static string FilePath => Path.Combine(AppPaths.DataRoot(), "controller.json");

        public static ControllerState Load()
        {
            ControllerState state = null;
            if (File.Exists(FilePath))
            {
                try { state = Json.Deserialize<ControllerState>(File.ReadAllText(FilePath, Encoding.UTF8)); }
                catch (Exception e) { throw new InvalidDataException("无法读取控制台设置：" + FilePath, e); }
            }
            if (state == null) state = new ControllerState();
            if (state.Accounts == null) state.Accounts = new List<AccountEntry>();
            if (state.Queue == null) state.Queue = new List<QueueEntry>();
            if (state.SuccessfulTracks == null) state.SuccessfulTracks = new Dictionary<string, bool>();
            if (state.TrackLevels == null) state.TrackLevels = new Dictionary<string, string>();
            if (state.Accounts.Count == 0) state.Accounts.Add(new AccountEntry());
            foreach (var account in state.Accounts) Validate(account);
            if (state.Accounts.GroupBy(a => a.Profile, StringComparer.OrdinalIgnoreCase).Any(g => g.Count() > 1) ||
                state.Accounts.GroupBy(a => a.Port).Any(g => g.Count() > 1))
                throw new InvalidDataException("每个账号需要不同的档案名称和控制端口。");
            return state;
        }

        public void Save()
        {
            Directory.CreateDirectory(AppPaths.DataRoot());
            var path = FilePath;
            var temporary = path + ".tmp";
            File.WriteAllText(temporary, Json.Serialize(this), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        public static void Validate(AccountEntry account)
        {
            if (account == null || !Regex.IsMatch(account.Profile ?? "", "^[A-Za-z0-9_-]{1,32}$"))
                throw new ArgumentException("档案名称只能包含 1–32 个英文字母、数字、_ 或 -。");
            if (account.Port < 1024 || account.Port > 65535)
                throw new ArgumentException("控制端口必须在 1024–65535 之间。");
            if (string.IsNullOrWhiteSpace(account.Server))
                throw new ArgumentException("请填写服务器名称或区服编号。");
            if (!UrlText.IsWeb(account.EntryUrl))
                throw new ArgumentException("游戏入口必须是 http(s) 网址。");
        }

        public static string SuccessKey(string profile, string level, string track) =>
            profile.ToLowerInvariant() + "\n" + (level ?? "").Trim() + "\n" +
            Path.GetFullPath(track).ToLowerInvariant();
    }
}
