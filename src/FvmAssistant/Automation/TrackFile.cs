using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FvmAssistant.Automation
{
    /// <summary>One grid cell, 1-based: row 1-7 top to bottom, column 1-9 left to right.</summary>
    internal struct Cell
    {
        public Cell(int row, int col)
        {
            Row = row;
            Col = col;
        }

        public int Row { get; }
        public int Col { get; }

        public override string ToString() => $"{Row}{Col}";
    }

    internal sealed class TrackStep
    {
        public TrackStep(int timeSec, Cell cell) : this(timeSec * 10, cell, true)
        {
        }

        private TrackStep(int timeDeciseconds, Cell cell, bool _)
        {
            TimeDeciseconds = timeDeciseconds;
            Cell = cell;
        }

        public static TrackStep AtDeciseconds(int timeDeciseconds, Cell cell) =>
            new TrackStep(timeDeciseconds, cell, true);

        public int TimeDeciseconds { get; }
        public double TimeSec => TimeDeciseconds / 10.0;
        public string TimeText => (TimeDeciseconds / 10).ToString(CultureInfo.InvariantCulture) +
            (TimeDeciseconds % 10 == 0 ? "" : "." + (TimeDeciseconds % 10).ToString(CultureInfo.InvariantCulture));
        public string EditorTimeText => (TimeDeciseconds / 10).ToString("000", CultureInfo.InvariantCulture) +
            (TimeDeciseconds % 10 == 0 ? "" : "." + (TimeDeciseconds % 10).ToString(CultureInfo.InvariantCulture));
        public Cell Cell { get; }
        public bool IsMarker => Cell.Row == 0 && Cell.Col == 0;
        public bool HasCell => Cell.Row >= 1 && Cell.Row <= 7 && Cell.Col >= 1 && Cell.Col <= 9;
    }

    internal sealed class TrackCard
    {
        public int SourceLine { get; set; }
        public string Name { get; set; }
        public int CooldownSec { get; set; }

        /// <summary>Stacking layer from the editor: 0 special, 1 承载, 2 护罩, 3 普通, 4 咖啡粉, 5 冷却.</summary>
        public int Layer { get; set; }

        /// <summary>Raw flag letters, e.g. "补极独".</summary>
        public string Flags { get; set; } = "";

        /// <summary>Typed condition fields after the flags, with source text retained for diagnostics.</summary>
        public TrackConditions Conditions { get; set; } = TrackConditions.Parse("");

        public List<TrackStep> Steps { get; } = new List<TrackStep>();
        public IEnumerable<Cell> FormationCells => Steps.Where(s => s.HasCell).Select(s => s.Cell);

        /// <summary>补: each placement goes to the first free cell of the formation.</summary>
        public bool Fill => Flags.Contains('补');

        /// <summary>极 (∞): place as soon as the slot is ready instead of at the step times.</summary>
        public bool Extreme => Flags.Contains('极');

        /// <summary>独: keeps real time instead of track time.</summary>
        public bool Independent => Flags.Contains('独');

        /// <summary>继: default support schedule in the opening; inherited steps in later waves.</summary>
        public bool Inherit => Flags.Contains('继');

        /// <summary>绝 (★): absolute priority, placed as usual while the 优先队列 holds the other cards back.</summary>
        public bool Absolute => Flags.Contains('绝');

        public bool IsShovel => Name == "铲子";
        public bool IsGem => Name == "宝石";
    }

    internal sealed class TrackWave
    {
        public TrackWave(string label)
        {
            Label = label;
        }

        /// <summary>"0" for the opening section, otherwise the text of "--第N波--".</summary>
        public string Label { get; }

        public List<TrackCard> Cards { get; } = new List<TrackCard>();

        /// <summary>Deck cards in slot order (the editor lists them in carry order; 宝石/铲子 are not deck slots).</summary>
        public IEnumerable<TrackCard> DeckCards => Cards.Where(c => !c.IsShovel && !c.IsGem);
    }

    /// <summary>
    /// Parser for 轨道编辑器 track files (GBK or UTF-8 text). Header "key=value" lines, then one line per card:
    /// name TAB cooldown[layer]flags[,conditions] TAB time|rowcol ...; "--第N波--" starts a wave section.
    /// </summary>
    internal sealed class TrackFile
    {
        private static readonly Regex Spec = new Regex(@"^(\d+)\[(\d+)\]([^,]*)(?:,(.*))?$");
        private static readonly Regex Step = new Regex(@"^(\d+)(?:\.(\d))?\|(\d)(\d)$");
        private static readonly Regex WaveHeader = new Regex(@"^--第(.+)波--$");

        public string Path { get; private set; }
        public Dictionary<string, string> Header { get; } = new Dictionary<string, string>();
        public List<TrackWave> Waves { get; } = new List<TrackWave>();
        public List<string> Warnings { get; } = new List<string>();
        public List<TrackDiagnostic> Diagnostics { get; } = new List<TrackDiagnostic>();
        public Dictionary<string, int> HeaderLines { get; } = new Dictionary<string, int>();

        public TrackWave Opening => Waves[0];

        /// <summary>
        /// "优先队列" (e.g. 海星,海星,狮子座): these placements come first, in this order; meanwhile only 绝
        /// cards are placed, so the flames are saved up for the queue.
        /// </summary>
        public IReadOnlyList<string> PriorityQueue =>
            (Header.TryGetValue("优先队列", out var v) ? v : "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0).ToList();

        /// <summary>Optional track-wide card attempt limit; 0 uses the normal runner cadence.</summary>
        public int PlantSpeed => Header.TryGetValue("放卡限速", out var value) &&
            int.TryParse(value, out var speed) && speed >= 1 && speed <= 5 ? speed : 0;

        /// <summary>Where the editor puts the 1P character ("人物1位置", e.g. "11" or "62,52").</summary>
        public IReadOnlyList<Cell> Player1Cells => ParseCells(Header.TryGetValue("人物1位置", out var v) ? v : "");

        public static TrackFile Load(string path)
        {
            var text = EditorText.ReadAllText(path);
            var track = Parse(text);
            track.Path = path;
            return track;
        }

        public static TrackFile Parse(string text)
        {
            var track = new TrackFile();
            var wave = new TrackWave("0");
            track.Waves.Add(wave);

            var lines = text.Replace("\r\n", "\n").Split('\n');
            for (int n = 0; n < lines.Length; n++)
            {
                var line = lines[n].TrimEnd('\r', ' ').TrimStart('\uFEFF');
                if (line.Length == 0) continue;

                var waveMatch = WaveHeader.Match(line);
                if (waveMatch.Success)
                {
                    wave = new TrackWave(waveMatch.Groups[1].Value);
                    track.Waves.Add(wave);
                    continue;
                }

                if (!line.Contains('\t'))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0)
                    {
                        var key = line.Substring(0, eq);
                        if (track.Header.ContainsKey(key)) track.Problem("duplicate-header", n + 1, null, "Duplicate header: " + key);
                        track.Header[key] = line.Substring(eq + 1);
                        track.HeaderLines[key] = n + 1;
                    }
                    else track.Problem("unrecognised-line", n + 1, null, "Expected a header or a tab-separated card row.");
                    continue;
                }

                var fields = line.Split('\t');
                var spec = Spec.Match(fields[1]);
                if (!spec.Success || !int.TryParse(spec.Groups[1].Value, out var cooldown) ||
                    !int.TryParse(spec.Groups[2].Value, out var layer))
                {
                    track.Problem("bad-card-spec", n + 1, fields[0], "Invalid cooldown/layer specification.");
                    continue;
                }

                var card = new TrackCard
                {
                    SourceLine = n + 1,
                    Name = fields[0],
                    CooldownSec = cooldown,
                    Layer = layer,
                    Flags = spec.Groups[3].Value,
                    Conditions = TrackConditions.Parse(spec.Groups[4].Value),
                };
                foreach (var raw in fields.Skip(2))
                {
                    var step = Step.Match(raw.Trim());
                    if (step.Success && int.TryParse(step.Groups[1].Value, out var seconds) &&
                        seconds <= int.MaxValue / 10)
                        card.Steps.Add(TrackStep.AtDeciseconds(seconds * 10 +
                            (step.Groups[2].Success ? step.Groups[2].Value[0] - '0' : 0),
                            new Cell(step.Groups[3].Value[0] - '0', step.Groups[4].Value[0] - '0')));
                    else if (raw.Length > 0)
                        track.Problem("bad-step", n + 1, card.Name,
                            "Step omitted: expected ttt(.t)|rowcol with at most one decimal digit.");
                }
                wave.Cards.Add(card);
            }
            return track;
        }

        private void Problem(string code, int line, string card, string message)
        {
            var diagnostic = new TrackDiagnostic("error", code, line, card, message);
            Diagnostics.Add(diagnostic);
            Warnings.Add(diagnostic.ToString());
        }

        private static IReadOnlyList<Cell> ParseCells(string value) =>
            value.Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length == 2 && char.IsDigit(s[0]) && char.IsDigit(s[1]) && s[0] != '0')
                .Select(s => new Cell(s[0] - '0', s[1] - '0'))
                .ToList();
    }
}
