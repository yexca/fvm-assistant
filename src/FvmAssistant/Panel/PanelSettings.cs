using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace FvmAssistant.Panel
{
    /// <summary>What the operation panel remembers between runs in its account's panel.json.</summary>
    internal sealed class PanelSettings
    {
        public string Level { get; set; } = "美味/曲奇";
        public string Track { get; set; } = @"通用轨道\6美味\1曲奇.txt";

        /// <summary>Track card name → my learned card name.</summary>
        public Dictionary<string, string> CardMap { get; set; } = new Dictionary<string, string>();

        public int Rounds { get; set; } = 5;
        public bool Shovel { get; set; }
        public bool Boss { get; set; }

        /// <summary>The account picks up flames by itself (a paid feature): sweep only where items drop.</summary>
        public bool AutoFlame { get; set; }

        public static PanelSettings Load(string path)
        {
            try
            {
                if (File.Exists(path)) return Json.Deserialize<PanelSettings>(File.ReadAllText(path, Encoding.UTF8)) ?? new PanelSettings();
            }
            catch (Exception e)
            {
                Log.Warn("panel settings unreadable, using defaults: " + e.Message);
            }
            return new PanelSettings();
        }

        public void Save(string path)
        {
            try
            {
                File.WriteAllText(path, Json.Serialize(this), new UTF8Encoding(false));
            }
            catch (IOException e)
            {
                Log.Warn("could not save panel settings: " + e.Message);
            }
        }
    }
}
