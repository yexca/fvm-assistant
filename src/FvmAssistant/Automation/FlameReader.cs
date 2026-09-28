using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace FvmAssistant.Automation
{
    /// <summary>Reads the battle flame counter from locally installed track-editor digit masks.</summary>
    internal sealed class FlameReader
    {
        private const int Width = 6, Height = 10, Pitch = 7, DigitY = 75;
        private static readonly int[] LengthOffset = { 0, 0, 4, 7, 11, 14 };
        private readonly List<bool[][]> _sets = new List<bool[][]>();

        private FlameReader() { }

        /// <summary>No third-party pixels are embedded in or copied into the application.</summary>
        public static FlameReader Load(string trackDirectory)
        {
            if (string.IsNullOrEmpty(trackDirectory)) return null;
            var reader = new FlameReader();
            foreach (var name in new[] { "火苗", "火苗_win7" })
            {
                var folder = Path.Combine(trackDirectory, "附加程序", "数字", name);
                var masks = new bool[10][];
                bool complete = true;
                for (int digit = 0; digit < 10; digit++)
                {
                    var path = Path.Combine(folder, digit + ".png");
                    if (!File.Exists(path)) { complete = false; break; }
                    try
                    {
                        using (var bitmap = new Bitmap(path))
                        {
                            if (bitmap.Width != Width || bitmap.Height != Height) { complete = false; break; }
                            var mask = new bool[Width * Height];
                            for (int y = 0; y < Height; y++)
                                for (int x = 0; x < Width; x++)
                                {
                                    var pixel = bitmap.GetPixel(x, y);
                                    mask[y * Width + x] = pixel.R >= 240 && pixel.G >= 240 && pixel.B >= 240;
                                }
                            masks[digit] = mask;
                        }
                    }
                    catch (Exception error) when (error is IOException || error is ArgumentException ||
                        error is UnauthorizedAccessException || error is ExternalException)
                    {
                        complete = false;
                        break;
                    }
                }
                if (complete) reader._sets.Add(masks);
            }
            return reader._sets.Count == 0 ? null : reader;
        }

        /// <summary>Null means unreadable; a recognized zero is returned as 0.</summary>
        public int? Read(StageFrame frame, int slotCount = 1)
        {
            if (frame == null || frame.Width < 200 || frame.Height < DigitY + Height) return null;
            // The counter follows the left edge of the card bar when the game switches to 13+ slots.
            int firstDigitX = slotCount > 12 ? 151 : 172;
            for (int length = 5; length >= 1; length--)
            {
                int value = 0;
                bool valid = true;
                for (int position = 0; position < length; position++)
                {
                    int digit = ReadDigit(frame, firstDigitX + position * Pitch - LengthOffset[length], DigitY);
                    if (digit < 0) { valid = false; break; }
                    value = value * 10 + digit;
                }
                if (valid) return value <= 15000 ? value : (int?)null;
            }
            // Some live Flash renderers use a wider, anti-aliased counter font whose lower edge is
            // clipped by the counter border. Its top/middle/side strokes can still be classified
            // without treating a partial template match as a number.
            return ReadStrokes(frame, slotCount);
        }

        private static int? ReadStrokes(StageFrame frame, int slotCount)
        {
            int left = slotCount > 12 ? 134 : 155;
            int right = slotCount > 12 ? 179 : 200;
            const int top = 77, bottom = 87;
            if (right > frame.Width || bottom > frame.Height) return null;
            var groups = new List<(int left, int right)>();
            int first = -1, last = -1;
            for (int x = left; x < right; x++)
            {
                bool active = false;
                for (int y = top; y < bottom && !active; y++) active = Dark(frame, x, y);
                if (!active) continue;
                if (first >= 0 && (x - last > 2 || x - first >= 7))
                {
                    groups.Add((first, last));
                    first = -1;
                }
                if (first < 0) first = x;
                last = x;
            }
            if (first >= 0) groups.Add((first, last));
            if (groups.Count < 1 || groups.Count > 5) return null;

            int value = 0;
            foreach (var group in groups)
            {
                int digit = StrokeDigit(frame, group.left, group.right);
                if (digit < 0 || (value == 0 && groups.Count > 1 && digit == 0)) return null;
                value = value * 10 + digit;
            }
            return value <= 15000 ? value : (int?)null;
        }

        private static int StrokeDigit(StageFrame frame, int left, int right)
        {
            int width = right - left + 1;
            if (width < 2 || width > 7) return -1;
            int Count(int x1, int x2, int y1, int y2)
            {
                int count = 0;
                for (int y = y1; y < y2; y++)
                    for (int x = x1; x <= x2; x++)
                        if (Dark(frame, x, y)) count++;
                return count;
            }
            if (width <= 3)
            {
                int rows = 0;
                for (int y = 77; y < 87; y++)
                    if (Count(left, right, y, y + 1) > 0) rows++;
                return rows >= 7 ? 1 : -1;
            }

            bool top = Count(left, right, 77, 78) >= 2;
            bool upperLeft = Count(left, left + 1, 78, 82) >= 2;
            bool upperRight = Count(right - 1, right, 78, 82) >= 2;
            bool middle = false;
            for (int y = 81; y <= 83; y++)
                for (int x = left; x < right; x++)
                    if (Dark(frame, x, y) && Dark(frame, x + 1, y)) middle = true;
            bool lowerLeft = Count(left, left + 1, 83, 87) >= 2;
            bool lowerRight = Count(right - 1, right, 83, 87) >= 2;
            int strokes = (top ? 1 : 0) | (upperLeft ? 2 : 0) | (upperRight ? 4 : 0) |
                (middle ? 8 : 0) | (lowerLeft ? 16 : 0) | (lowerRight ? 32 : 0);
            switch (strokes)
            {
                case 55: return 0; // top and four sides
                case 29: return 2;
                case 45: return 3;
                case 46: return 4;
                case 43: return 5;
                case 59: return 6;
                case 37: return 7;
                case 63: return 8;
                case 47: return 9;
                default: return -1;
            }
        }

        private static bool Dark(StageFrame frame, int x, int y)
        {
            int offset = (y * frame.Width + x) * 4;
            return frame.Bgra[offset] <= 128 && frame.Bgra[offset + 1] <= 128 && frame.Bgra[offset + 2] <= 128;
        }

        private int ReadDigit(StageFrame frame, int x0, int y0)
        {
            if (x0 < 0 || x0 + Width > frame.Width) return -1;
            int bestDigit = -1, bestErrors = int.MaxValue, nextErrors = int.MaxValue;
            foreach (var set in _sets)
                for (int digit = 0; digit < 10; digit++)
                {
                    int errors = 0;
                    var mask = set[digit];
                    for (int y = 0; y < Height; y++)
                        for (int x = 0; x < Width; x++)
                        {
                            int pixel = ((y0 + y) * frame.Width + x0 + x) * 4;
                            bool black = frame.Bgra[pixel] <= 24 && frame.Bgra[pixel + 1] <= 24 && frame.Bgra[pixel + 2] <= 24;
                            if (black != mask[y * Width + x]) errors++;
                        }
                    if (errors < bestErrors)
                    {
                        nextErrors = bestErrors;
                        bestErrors = errors;
                        bestDigit = digit;
                    }
                    else if (digit != bestDigit && errors < nextErrors) nextErrors = errors;
                }
            return bestErrors <= 2 && nextErrors - bestErrors >= 2 ? bestDigit : -1;
        }
    }
}
