using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace FvmAssistant.Automation
{
    internal sealed class TrackDiagnostic
    {
        public TrackDiagnostic(string severity, string code, int line, string card, string message)
        {
            Severity = severity; Code = code; Line = line; Card = card; Message = message;
        }
        public string Severity { get; }
        public string Code { get; }
        public int Line { get; }
        public string Card { get; }
        public string Message { get; }
        public override string ToString() => $"{Severity} [{Code}]" + (Line > 0 ? $" line {Line}" : "") +
            (string.IsNullOrEmpty(Card) ? "" : $" ({Card})") + ": " + Message;
    }

    internal sealed class TrackCompatibilityReport
    {
        public List<TrackDiagnostic> Diagnostics { get; } = new List<TrackDiagnostic>();
        public bool CanStart => !Diagnostics.Any(d => d.Severity == "error");
        public void RequireCompatible()
        {
            if (!CanStart) throw new InvalidOperationException("Track cannot run with the current executor:\n" +
                string.Join("\n", Diagnostics.Where(d => d.Severity == "error").Select(d => d.ToString())));
        }
    }

    /// <summary>A single loaded snapshot shared by preview, validation and the running loop.</summary>
    internal sealed class TrackPreflight
    {
        public TrackPreflight(TrackFile track, CarrierMap carriers, FlameReader flameReader = null)
        {
            Track = track;
            Carriers = carriers;
            FlameReader = flameReader;
            DeckCards = track.Opening.DeckCards.Select(c => carriers?.Resolve(c) ?? c)
                .Where(c => c.Steps.Count > 0 || (carriers?.Matches(c) == true && carriers.Cells.Count > 0) ||
                    c.Inherit || c.Extreme).ToList();
        }

        public TrackFile Track { get; }
        public CarrierMap Carriers { get; }
        public FlameReader FlameReader { get; }
        public IReadOnlyList<TrackCard> DeckCards { get; }
        private static bool IsDefault(string value) => TrackConditions.Default(value);

        public TrackCompatibilityReport Inspect(IEnumerable<string> selectedCards = null, bool useShovel = false, int deckLimit = 18, bool duo = false)
        {
            var report = new TrackCompatibilityReport();
            report.Diagnostics.AddRange(Track.Diagnostics);
            var selected = selectedCards == null ? null : new HashSet<string>(selectedCards);
            void Add(string code, TrackCard card, string message, string severity = "error") =>
                report.Diagnostics.Add(new TrackDiagnostic(severity, code, card?.SourceLine ?? 0, card?.Name, message));
            void Header(string key, string message, string severity = "error") => report.Diagnostics.Add(
                new TrackDiagnostic(severity, "header-unsupported", Track.HeaderLines.TryGetValue(key, out var line) ? line : 0,
                    null, key + ": " + message));

            if (Track.Waves.Count > 1)
                Add("later-waves", null, "Later wave sections are not executed. Use an opening-only track.");
            foreach (var wave in Track.Waves)
            {
                foreach (var duplicate in wave.Cards.GroupBy(c => c.Name).Where(g => g.Count() > 1))
                    Add("duplicate-card", duplicate.Last(), "Card names must be unique within each wave.");
                foreach (var card in wave.Cards)
                {
                    if (string.IsNullOrWhiteSpace(card.Name)) Add("empty-card", card, "A card name is required.");
                    if (card.Layer < 0 || card.Layer > 6) Add("invalid-layer", card, "Layer must be between 0 and 6.");
                    if (card.Steps.Any(s => s.TimeDeciseconds < 0 ||
                        (s.TimeDeciseconds > 0 && s.TimeDeciseconds < 10) || s.TimeDeciseconds > 9600))
                        Add("invalid-time", card, "Track times must be zero or between 1 and 960 seconds, in tenths.");
                    if (card.Steps.Any(s => !s.HasCell &&
                        !(s.IsMarker && card.Fill && !card.Extreme && !card.IsShovel && s.TimeDeciseconds > 0)))
                        Add("invalid-cell", card, "Only playable cells can be clicked; a timed 00 marker requires scheduled 补.");
                }
            }

            foreach (var key in new[] { "退出时间", "魔塔层数", "移动板块", "刷技能模式" })
                if (Track.Header.TryGetValue(key, out var value) && !IsDefault(value))
                    Header(key, "This non-default setting is not implemented.");
            if (Track.Header.TryGetValue("放卡限速", out var speed) && !IsDefault(speed) &&
                (!int.TryParse(speed, out var cardsPerSecond) || cardsPerSecond < 0 || cardsPerSecond > 5))
                Header("放卡限速", "Expected an integer from 0 (unlimited) to 5 cards per second.");
            if (!duo && Track.Header.TryGetValue("人物2位置", out var secondPlayer) && !IsDefault(secondPlayer))
                report.Diagnostics.Add(new TrackDiagnostic("warning", "second-player-ignored",
                    Track.HeaderLines.TryGetValue("人物2位置", out var line) ? line : 0, null,
                    "人物2位置 is ignored in a solo room; the character uses 人物1位置."));
            var knownHeaders = new HashSet<string> { "轨道版本号", "注释", "卡槽数", "人物1位置", "优先队列",
                "退出时间", "魔塔层数", "放卡限速", "移动板块", "刷技能模式", "人物2位置" };
            foreach (var key in Track.Header.Keys.Where(k => !knownHeaders.Contains(k)))
                Header(key, "Unknown header; execution semantics cannot be verified.");
            if (Track.Header.TryGetValue("人物1位置", out var positions) && !string.IsNullOrWhiteSpace(positions))
                if (positions.Split(',').Any(p => !Regex.IsMatch(p.Trim(), @"^[1-7][1-9]$")))
                    Header("人物1位置", "Expected comma-separated playable cells (rows 1-7, columns 1-9).");
            if (Track.Header.TryGetValue("卡槽数", out var count) &&
                (!int.TryParse(count, out var slots) || slots != Track.Opening.DeckCards.Count()))
                Header("卡槽数", "Declared slot count does not match the opening deck.");

            if (selected != null)
                foreach (var name in selected.Where(n => !DeckCards.Any(c => c.Name == n)))
                    Add("unknown-mapping", null, "Mapped card has no executable opening plan: " + name);

            var active = DeckCards.Where(c => selected == null || selected.Contains(c.Name)).ToList();
            if (active.Count == 0) Add("empty-deck", null, "Select at least one card with an opening plan.");
            if (active.Count > deckLimit) Add("deck-capacity", null, "This deck mode supports at most " + deckLimit + " selected cards.");
            foreach (var card in DeckCards.Where(c => selected != null && !selected.Contains(c.Name)))
                Add("card-skipped", card, "This card is not mapped and will be skipped.", "warning");
            foreach (var card in Track.Opening.Cards.Where(c => c.IsShovel || c.IsGem))
            {
                bool hasAction = card.Steps.Count > 0 || card.Extreme;
                if (!hasAction) continue;
                if (card.IsGem) Add("gem-action", card, "Gem actions are not implemented.");
                else if (useShovel) active.Add(card);
                else Add("shovel-skipped", card, "Shovel actions are disabled.", "warning");
            }

            foreach (var card in active)
            {
                var original = Track.Opening.Cards.First(c => c.Name == card.Name && c.SourceLine == card.SourceLine);
                if (card != original) Add("support-inherited", original,
                    (original.Steps.Count == 0 ? "Resolved default support steps from " :
                        "Resolved default timing over explicit support cells from ") + Carriers.Source + ".", "info");
                if (original.Inherit && card == original)
                    Add("missing-inheritance", original, "The requested default support schedule could not be resolved.");
                if (card != original && card.Steps.Any(s => !s.HasCell &&
                    !(s.IsMarker && card.Fill && !card.Extreme && s.TimeDeciseconds > 0)))
                    Add("invalid-inherited-cell", original,
                        "Inherited timing has no matching playable cell; only scheduled 补 may use a timed 00 marker.");
                if (card.Steps.Count == 0 && !(Carriers?.Matches(card) == true && Carriers.Cells.Count > 0))
                    Add("missing-plan", original, "No placement steps or matching fixed support are available.");
                if (original.Inherit && original.Layer != 1)
                    Add("inheritance-unsupported", original, "Opening inheritance is supported only for support cards.");
                foreach (var flag in card.Flags.Distinct().Where(f => !"补极独继绝锁x".Contains(f)))
                    Add("flag-unsupported", original, "Unsupported flag: " + flag);
                if ((card.Layer == 0 && !card.IsShovel && !card.IsGem) || card.Layer == 4 || card.Layer == 6)
                    Add("special-layer", original, "This special-card layer behavior is not implemented.");
                if (card.Layer == 5 && card.Name == "冰沙")
                    Add("sorbet-layout", original, "冰沙 requires its editor-defined fallback cells, which are not implemented.");

                var conditions = card.Conditions;
                foreach (var error in conditions.Errors)
                    Add("condition-format", original, error, card.Extreme ? "error" : "warning");
                if (card.Extreme)
                {
                    if (conditions.Unsupported.Count > 0)
                        Add("conditions-unsupported", original, "Conditions are not executed: " + string.Join(", ", conditions.Unsupported) + ".");
                    if (conditions.MinimumCardCount > 0 && conditions.CardName.Length > 0)
                    {
                        var required = DeckCards.FirstOrDefault(c => c.Name == conditions.CardName);
                        if (required == null || (selected != null && !selected.Contains(required.Name)))
                            Add("condition-card-missing", original, "Card-count condition needs a selected opening card: " + conditions.CardName);
                        else if (required.Layer != 3)
                            Add("condition-card-layer", original, "Card-count observation currently requires a layer-3 card: " + conditions.CardName);
                    }
                    if (conditions.MinimumFlames > 0 && FlameReader == null)
                        Add("flame-assets-missing", original, "Flame digit masks are unavailable in the configured track-editor directory.");
                }
                else if (conditions.NonDefault.Count > 0)
                    Add("conditions-inactive", original, "Condition fields without 极 are inactive: " + string.Join(", ", conditions.NonDefault) + ".", "warning");
                if (card.IsShovel && card.Flags.Any(f => f != '独' && f != '锁' && f != 'x'))
                    Add("shovel-mode", original, "Only timed shovel steps are implemented.");
                if (card.Fill && !card.Extreme && !card.IsShovel)
                {
                    if (!card.Steps.Any(s => s.HasCell))
                        Add("scheduled-fill-cells", original, "Scheduled 补 needs at least one playable formation cell.");
                    if (!card.Steps.Any(s => s.TimeDeciseconds > 0))
                        Add("scheduled-fill-events", original, "Scheduled 补 needs at least one positive-time event.");
                }
                if (!card.Fill && !card.Extreme && !card.IsShovel && card.Steps.Any(s => s.TimeSec == 0))
                    Add("zero-time-schedule", original, "Time-zero formation entries need an explicit supported fill mode.");
                if (card.Extreme && card.Steps.Skip(1).Any(s => s.TimeSec > 0))
                    Add("timed-prefix", original, "A timed prefix or multiple timing markers before filling is not implemented.");
            }

            foreach (var name in Track.PriorityQueue.Distinct())
            {
                if (!DeckCards.Any(c => c.Name == name)) Add("queue-card-missing", null, "Priority queue references an unavailable card: " + name);
                else if (selected != null && !selected.Contains(name))
                    Add("queue-card-skipped", null, "Unmapped priority queue entry will be skipped: " + name, "warning");
            }
            return report;
        }
    }
}
