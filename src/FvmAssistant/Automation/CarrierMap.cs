using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FvmAssistant.Automation
{
    /// <summary>
    /// The cells of a map where a card needs a carrier (承载) under it: water for 木盘子, gaps in the
    /// clouds for 棉花糖. 轨道编辑器 keeps one carrier formation per map for its 自动承载, in
    /// 预制轨道\承载轨道\&lt;n岛&gt;\&lt;n关&gt;.txt: the carrier card with steps at 0 s lists every such cell.
    /// Timed steps remain separate from fixed cells and can be inherited by an opening card.
    /// </summary>
    internal sealed class CarrierMap
    {
        private CarrierMap(string source, IReadOnlyList<TrackCard> cards)
        {
            Source = source;
            Cards = cards;
            Carrier = cards[0].Name;
            Cells = cards.SelectMany(c => c.Steps.Where(s => s.TimeDeciseconds == 0 && s.HasCell)
                .Select(s => s.Cell)).Distinct().ToList();
        }

        /// <summary>The file it came from, relative to 承载轨道 (e.g. 7火山\5城堡.txt).</summary>
        public string Source { get; }

        /// <summary>The carrier the editor lays there (木盘子, 棉花糖…).</summary>
        public string Carrier { get; }

        public IReadOnlyList<TrackCard> Cards { get; }

        public IReadOnlyCollection<Cell> Cells { get; }

        /// <summary>Clouds rather than water: a card put on an open cloud falls through instead of being refused.</summary>
        public bool Clouds => Cards.Any(c => IsCloudCarrier(c.Name));

        public static bool IsCloudCarrier(string name) => name.Contains("棉花糖") || name.Contains("麦芽糖");

        public bool Matches(TrackCard card) => card.Layer == 1 && Cards.Any(c => SameCarrier(c, card));

        private static bool SameCarrier(TrackCard a, TrackCard b) => a.Name == b.Name ||
            (IsCloudCarrier(a.Name) && IsCloudCarrier(b.Name));

        /// <summary>Opening 继 imports default timing while retaining any explicit formation.</summary>
        public TrackCard Resolve(TrackCard card)
        {
            if (card.Layer != 1 || !card.Inherit) return card;
            var defaults = Cards.FirstOrDefault(c => SameCarrier(c, card));
            if (defaults == null) return card;
            bool explicitFormation = card.Steps.Any(s => s.HasCell);
            string flags = explicitFormation
                ? new string(card.Flags.Where(f => f != '独' && f != '极').ToArray()) +
                  (defaults.Independent ? "独" : "") + (defaults.Extreme ? "极" : "")
                : defaults.Flags + (card.Absolute ? "绝" : "");
            var resolved = new TrackCard
            {
                Name = card.Name, Layer = card.Layer, CooldownSec = card.CooldownSec,
                SourceLine = card.SourceLine,
                Flags = flags, Conditions = defaults.Conditions
            };
            if (!explicitFormation)
            {
                // A formation with no timing must keep replenishing the fixed supports.
                if (defaults.Steps.All(s => s.TimeDeciseconds == 0)) resolved.Flags += "补极";
                resolved.Steps.AddRange(defaults.Steps);
            }
            else
            {
                int lastTimed = defaults.Steps.FindLastIndex(s => s.TimeDeciseconds > 0) + 1;
                int length = Math.Max(card.Steps.Count, lastTimed);
                for (int i = 0; i < length; i++)
                {
                    int time = i < defaults.Steps.Count ? defaults.Steps[i].TimeDeciseconds : 0;
                    var cell = i < card.Steps.Count ? card.Steps[i].Cell : new Cell(0, 0);
                    if (time == 0 && cell.Row == 0 && cell.Col == 0) continue;
                    resolved.Steps.Add(TrackStep.AtDeciseconds(time, cell));
                }
            }
            return resolved;
        }

        /// <summary>
        /// The map of <paramref name="level"/>, or else of the preset folder the track is in
        /// (通用轨道\7火山\5城堡.txt → 承载轨道\7火山\5城堡.txt). Null when there is none, or the map has
        /// no carrier cells (神殿).
        /// </summary>
        public static CarrierMap Find(string presetDir, LevelEntry level, string trackPath)
        {
            var root = Path.Combine(presetDir, "承载轨道");
            if (!Directory.Exists(root)) return null;

            string path = null;
            if (level != null)
            {
                var island = Directory.GetDirectories(root).FirstOrDefault(d => Bare(Path.GetFileName(d)) == level.Island);
                if (island != null)
                    path = Directory.GetFiles(island, "*.txt").FirstOrDefault(f => Bare(Path.GetFileNameWithoutExtension(f)) == level.Name);
            }
            else if (trackPath != null)
            {
                var folder = Path.GetFileName(Path.GetDirectoryName(trackPath));
                var name = Bare(Path.GetFileNameWithoutExtension(trackPath)).TrimEnd('+');
                var island = Path.Combine(root, folder);
                if (Directory.Exists(island))
                    path = Directory.GetFiles(island, "*.txt").FirstOrDefault(f => Bare(Path.GetFileNameWithoutExtension(f)) == name);
            }
            if (path == null) return null;

            var defaults = TrackFile.Load(path);
            if (defaults.Diagnostics.Count > 0)
                throw new FormatException("Invalid support layout " + Path.GetFileName(path) + ": " +
                    string.Join("; ", defaults.Diagnostics.Select(d => d.ToString())));
            var cards = defaults.Opening.Cards.Where(c => c.Layer == 1 && c.Steps.Count > 0).ToList();
            if (cards.Any(c => c.Steps.Any(s => !s.HasCell &&
                !(s.IsMarker && c.Fill && !c.Extreme && s.TimeDeciseconds > 0))) ||
                cards.SelectMany(c => c.Steps).Any(s => s.TimeDeciseconds < 0 ||
                (s.TimeDeciseconds > 0 && s.TimeDeciseconds < 10) || s.TimeDeciseconds > 9600))
                throw new FormatException("Invalid support cells or times in " + Path.GetFileName(path));
            if (defaults.Waves.Count > 1 || cards.GroupBy(c => c.Name).Any(g => g.Count() > 1))
                throw new FormatException("Support layout requires a single wave with unique card names: " + Path.GetFileName(path));
            if (cards.Count == 0) return null;
            return new CarrierMap(path.Substring(root.Length + 1), cards);
        }

        /// <summary>A folder or file name without the editor's ordering number ("15深渊" → "深渊").</summary>
        private static string Bare(string name) => name.TrimStart("0123456789".ToCharArray());
    }
}
