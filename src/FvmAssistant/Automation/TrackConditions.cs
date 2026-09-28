using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace FvmAssistant.Automation
{
    /// <summary>Parsed conditions attached to an 极 row. Other row modes keep the fields as inert metadata.</summary>
    internal sealed class TrackConditions
    {
        internal static readonly string[] Names = { "delay", "trigger mode", "flames", "wave",
            "card", "card count", "image", "image count", "target row", "target column", "follow card",
            "termination count", "termination time" };

        private readonly string[] _fields;
        private TrackConditions(string raw)
        {
            Raw = raw ?? "";
            _fields = Raw.Split(',');
        }

        public string Raw { get; }
        public int DelaySeconds { get; private set; }
        public bool TriggerMode { get; private set; }
        public int MinimumFlames { get; private set; }
        public string CardName { get; private set; }
        public int MinimumCardCount { get; private set; }
        public int TerminationCount { get; private set; }
        public int TerminationSeconds { get; private set; }
        public List<string> Errors { get; } = new List<string>();
        public IReadOnlyList<string> Unsupported => _fields.Select((value, index) => new { value, index })
            .Where(p => p.index >= 3 && p.index <= 10 && p.index != 4 && p.index != 5 && !Default(p.value))
            .Select(p => Names[p.index]).ToList();
        public IReadOnlyList<string> NonDefault => _fields.Select((value, index) => new { value, index })
            .Where(p => !Default(p.value))
            .Select(p => p.index < Names.Length ? Names[p.index] : "unknown field").ToList();

        public static TrackConditions Parse(string raw)
        {
            var result = new TrackConditions(raw);
            if (result._fields.Length > Names.Length)
                result.Errors.Add("Too many condition fields.");
            result.DelaySeconds = result.Number(0, 960);
            int mode = result.Number(1, 1);
            result.TriggerMode = mode == 1;
            result.MinimumFlames = result.Number(2, 15000);
            result.CardName = result.Text(4);
            result.MinimumCardCount = result.Number(5, 63);
            if ((result.CardName.Length == 0) != (result.MinimumCardCount == 0))
                result.Errors.Add("Card condition needs both a card name and a positive count.");
            result.TerminationCount = result.Number(11, 63);
            result.TerminationSeconds = result.Number(12, 960);
            return result;
        }

        private int Number(int index, int maximum)
        {
            if (index >= _fields.Length || Default(_fields[index])) return 0;
            if (!int.TryParse(_fields[index].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < 0 || value > maximum)
            {
                Errors.Add(Names[index] + " must be an integer from 0 to " + maximum + ".");
                return 0;
            }
            return value;
        }

        private string Text(int index) => index < _fields.Length && !Default(_fields[index]) ? _fields[index].Trim() : "";

        public static bool Default(string value) => string.IsNullOrWhiteSpace(value) ||
            string.Equals(value.Trim(), "NULL", StringComparison.OrdinalIgnoreCase) ||
            (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && number == 0);
    }

    /// <summary>Per-card condition clock and confirmed placement count.</summary>
    internal sealed class ConditionGate
    {
        private readonly TrackConditions _conditions;
        private DateTime? _readyAt;
        private bool _triggered;

        public ConditionGate(TrackConditions conditions) { _conditions = conditions; }
        public int SuccessfulPlacements { get; private set; }

        public bool Stopped(DateTime now, DateTime start) =>
            (_conditions.TerminationCount > 0 && SuccessfulPlacements >= _conditions.TerminationCount) ||
            (_conditions.TerminationSeconds > 0 && now - start > TimeSpan.FromSeconds(_conditions.TerminationSeconds));

        public bool Allows(DateTime now, DateTime start, int? flames, int? cardCount = null)
        {
            if (Stopped(now, start)) return false;

            bool met = (_conditions.MinimumFlames == 0 || (flames.HasValue && flames.Value >= _conditions.MinimumFlames)) &&
                (_conditions.MinimumCardCount == 0 || (cardCount.HasValue && cardCount.Value >= _conditions.MinimumCardCount));
            if (_conditions.TriggerMode && _triggered) return now >= _readyAt.Value;
            if (!met)
            {
                _readyAt = null;
                return false;
            }
            if (!_readyAt.HasValue) _readyAt = now.AddSeconds(_conditions.DelaySeconds);
            if (_conditions.TriggerMode) _triggered = true;
            return now >= _readyAt.Value;
        }

        public void Placed()
        {
            SuccessfulPlacements++;
            if (!_conditions.TriggerMode) _readyAt = null;
        }

        public void RollBack()
        {
            if (SuccessfulPlacements > 0) SuccessfulPlacements--;
        }
    }
}
