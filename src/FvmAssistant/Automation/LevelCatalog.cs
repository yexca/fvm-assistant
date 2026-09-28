using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;

namespace FvmAssistant.Automation
{
    internal sealed class LevelEntry
    {
        public string Island { get; set; }

        /// <summary>Where the island is on the world map popup.</summary>
        public Point IslandAt { get; set; }

        public string Name { get; set; }

        /// <summary>Where the level's bubble is on the island map.</summary>
        public Point At { get; set; }

        /// <summary>What the boss stage consumes (e.g. 神殿钥匙), as listed by 轨道编辑器.</summary>
        public string Key { get; set; }

        public bool HasBoss => !string.IsNullOrEmpty(Key) && Key != "无";

        public override string ToString() => $"{Island}/{Name}";
    }

    /// <summary>
    /// Reads 轨道编辑器's 附加程序\文本\普通关卡.txt: "##岛 x y" starts an island (its world-map
    /// position), then "关卡 x y 钥匙" lines give each level's position on that island's map.
    /// Checked against the recording: 美味 and 曲奇 land on their labels.
    /// </summary>
    internal static class LevelCatalog
    {
        public static List<LevelEntry> Load(string path)
        {
            var levels = new List<LevelEntry>();
            string island = null;
            var islandAt = Point.Empty;
            foreach (var raw in EditorText.ReadAllLines(path))
            {
                var f = raw.Split('\t').Select(s => s.Trim()).ToArray();
                if (f.Length < 3 || !int.TryParse(f[1], out var x) || !int.TryParse(f[2], out var y)) continue;
                if (f[0].StartsWith("##"))
                {
                    island = f[0].Substring(2);
                    islandAt = new Point(x, y);
                }
                else if (island != null)
                {
                    levels.Add(new LevelEntry { Island = island, IslandAt = islandAt, Name = f[0], At = new Point(x, y), Key = f.Length > 3 ? f[3] : "" });
                }
            }
            return levels;
        }

        /// <summary>"美味/曲奇" or just "曲奇" (first island listing it).</summary>
        public static LevelEntry Find(IEnumerable<LevelEntry> levels, string query)
        {
            var parts = query.Split('/', '\\');
            return parts.Length == 2
                ? levels.FirstOrDefault(l => l.Island == parts[0].Trim() && l.Name == parts[1].Trim())
                : levels.FirstOrDefault(l => l.Name == query.Trim());
        }
    }
}
