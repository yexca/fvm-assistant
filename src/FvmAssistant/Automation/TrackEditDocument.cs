using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace FvmAssistant.Automation
{
    /// <summary>Edits step fields in a copy while preserving every untouched source line.</summary>
    internal sealed class TrackEditDocument
    {
        private readonly Stack<string> _undo = new Stack<string>();
        private readonly Stack<string> _redo = new Stack<string>();
        private string _text;

        public TrackEditDocument(string sourcePath, string text)
        {
            SourcePath = sourcePath;
            _text = text ?? throw new ArgumentNullException(nameof(text));
        }

        public string SourcePath { get; }
        public string Text => _text;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public TrackFile Track => TrackFile.Parse(_text);

        public void SetHeader(string key, string value)
        {
            if (string.IsNullOrWhiteSpace(key) || key.IndexOfAny(new[] { '\r', '\n', '\t', '=' }) >= 0 ||
                value == null || value.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                throw new FormatException("A header must be a single key=value line.");
            var lines = _text.Replace("\r\n", "\n").Split('\n').ToList();
            var track = Track;
            if (track.HeaderLines.TryGetValue(key, out var sourceLine))
                lines[sourceLine - 1] = key + "=" + value;
            else lines.Insert(0, key + "=" + value);
            Commit(string.Join("\n", lines));
        }

        public string GetSpec(int sourceLine) => FieldsAt(sourceLine)[1];

        public void SetSpec(int sourceLine, string spec)
        {
            if (spec == null || spec.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0 ||
                TrackFile.Parse("Card\t" + spec).Diagnostics.Count > 0)
                throw new FormatException("Expected cooldown[layer]flags[,conditions], for example 7[3]补极.");
            var lines = _text.Replace("\r\n", "\n").Split('\n');
            var fields = FieldsAt(sourceLine);
            fields[1] = spec;
            lines[sourceLine - 1] = string.Join("\t", fields);
            Commit(string.Join("\n", lines));
        }

        public void MoveStep(int sourceLine, int index, int direction)
        {
            var lines = _text.Replace("\r\n", "\n").Split('\n');
            var fields = FieldsAt(sourceLine);
            int target = index + direction;
            if (direction != -1 && direction != 1) throw new ArgumentOutOfRangeException(nameof(direction));
            if (index < 0 || target < 0 || index >= fields.Count - 2 || target >= fields.Count - 2)
                throw new ArgumentOutOfRangeException(nameof(index));
            var value = fields[index + 2];
            fields[index + 2] = fields[target + 2];
            fields[target + 2] = value;
            lines[sourceLine - 1] = string.Join("\t", fields);
            Commit(string.Join("\n", lines));
        }

        private List<string> FieldsAt(int sourceLine)
        {
            var lines = _text.Replace("\r\n", "\n").Split('\n');
            if (sourceLine < 1 || sourceLine > lines.Length || !Track.CardsAt(sourceLine))
                throw new ArgumentOutOfRangeException(nameof(sourceLine));
            var fields = lines[sourceLine - 1].Split('\t').ToList();
            if (fields.Count < 2) throw new InvalidOperationException("The selected card row is invalid.");
            return fields;
        }

        public void SetStep(int sourceLine, int index, string raw)
        {
            var step = ParseStep(raw);
            var lines = _text.Replace("\r\n", "\n").Split('\n');
            var fields = FieldsAt(sourceLine);
            int count = fields.Count - 2;
            if (index < 0 || index > count) throw new ArgumentOutOfRangeException(nameof(index));
            if (index == count) fields.Add(step);
            else fields[index + 2] = step;
            lines[sourceLine - 1] = string.Join("\t", fields);
            Commit(string.Join("\n", lines));
        }

        public void RemoveStep(int sourceLine, int index)
        {
            var lines = _text.Replace("\r\n", "\n").Split('\n');
            var fields = FieldsAt(sourceLine);
            if (index < 0 || index >= fields.Count - 2)
                throw new ArgumentOutOfRangeException(nameof(index));
            fields.RemoveAt(index + 2);
            lines[sourceLine - 1] = string.Join("\t", fields);
            Commit(string.Join("\n", lines));
        }

        private static string ParseStep(string raw)
        {
            if (raw == null || raw.IndexOfAny(new[] { '\r', '\n', '\t' }) >= 0)
                throw new FormatException("A step must be a single time|cell field.");
            var parsed = TrackFile.Parse("Card\t7[3]\t" + raw);
            if (parsed.Diagnostics.Count > 0 || parsed.Opening.Cards.Count != 1 ||
                parsed.Opening.Cards[0].Steps.Count != 1)
                throw new FormatException("Expected time|cell, for example 035.5|49.");
            var step = parsed.Opening.Cards[0].Steps[0];
            if (step.TimeDeciseconds > 9600 || (step.TimeDeciseconds > 0 && step.TimeDeciseconds < 10) ||
                !(step.HasCell || step.IsMarker))
                throw new FormatException("Time must be zero or 1–960 seconds; cell must be 11–79 or 00.");
            return step.EditorTimeText + "|" + step.Cell;
        }

        private void Commit(string text)
        {
            if (text == _text) return;
            _undo.Push(_text);
            _redo.Clear();
            _text = text;
        }

        public void Undo()
        {
            if (!CanUndo) return;
            _redo.Push(_text);
            _text = _undo.Pop();
        }

        public void Redo()
        {
            if (!CanRedo) return;
            _undo.Push(_text);
            _text = _redo.Pop();
        }

        public void SaveCopy(string path, string allowedRoot)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetFullPath(allowedRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Edited tracks must be saved in the private edited-tracks folder.");
            if (SourcePath != null && string.Equals(full, Path.GetFullPath(SourcePath), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Save an edited copy instead of replacing its source.");
            Directory.CreateDirectory(root);
            using (var stream = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                writer.Write(_text);
        }
    }

    internal static class TrackEditLookup
    {
        public static bool CardsAt(this TrackFile track, int sourceLine) =>
            track.Waves.SelectMany(w => w.Cards).Any(c => c.SourceLine == sourceLine);
    }
}
