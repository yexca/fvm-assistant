using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;

namespace FvmAssistant.Automation
{
    /// <summary>
    /// Reads normal-stage room numbers with digit masks from the user's local track-editor data.
    /// Unknown or ambiguous pixels never become a room number. No mask pixels enter the source tree.
    /// </summary>
    internal sealed class RoomNumberReader
    {
        private readonly List<bool[][]> _indoor = new List<bool[][]>();
        private readonly List<bool[][]> _list = new List<bool[][]>();

        private RoomNumberReader() { }

        public static RoomNumberReader Load(string trackDirectory)
        {
            var reader = new RoomNumberReader();
            foreach (var name in new[] { "房间内房间号", "房间内房间号_win7" })
            {
                var set = LoadSet(trackDirectory, name, 6);
                if (set != null) reader._indoor.Add(set);
            }
            foreach (var name in new[] { "列表房间号", "列表房间号_win7" })
            {
                var set = LoadSet(trackDirectory, name, 5);
                if (set != null) reader._list.Add(set);
            }
            return reader._indoor.Count > 0 && reader._list.Count > 0 ? reader : null;
        }

        private static bool[][] LoadSet(string root, string folderName, int width)
        {
            if (string.IsNullOrWhiteSpace(root)) return null;
            var folder = Path.Combine(root, "附加程序", "数字", folderName);
            var masks = new bool[10][];
            for (int digit = 0; digit < 10; digit++)
            {
                var path = Path.Combine(folder, digit + ".png");
                if (!File.Exists(path)) return null;
                try
                {
                    using (var image = new Bitmap(path))
                    {
                        if (image.Width != width || image.Height != 8) return null;
                        var mask = new bool[width * 8];
                        for (int y = 0; y < 8; y++)
                            for (int x = 0; x < width; x++)
                            {
                                var pixel = image.GetPixel(x, y);
                                mask[y * width + x] = pixel.R >= 240 && pixel.G >= 240 && pixel.B >= 240;
                            }
                        masks[digit] = mask;
                    }
                }
                catch (Exception error) when (error is IOException || error is ArgumentException ||
                    error is UnauthorizedAccessException || error is ExternalException)
                {
                    return null;
                }
            }
            return masks;
        }

        public int? ReadRoom(StageFrame frame) => ReadNumber(frame, _indoor, 6, 7, 164, 28, 0, indoor: true);

        /// <summary>Finds only the requested number in the currently visible room list.</summary>
        public Point? FindListRoom(StageFrame frame, int number, int offsetX)
        {
            if (number <= 0 || number > 9999) return null;
            for (int y = 126; y <= 350; y++)
                if (ReadNumber(frame, _list, 5, 6, 150, y, offsetX, indoor: false) == number)
                    return new Point(138 + offsetX, y + 4);
            return null;
        }

        private static int? ReadNumber(StageFrame frame, IReadOnlyList<bool[][]> sets, int width,
            int pitch, int x, int y, int offsetX, bool indoor)
        {
            if (frame == null || sets.Count == 0) return null;
            for (int length = 4; length >= 1; length--)
            {
                int number = 0;
                bool valid = true;
                for (int position = 0; position < length; position++)
                {
                    var digit = ReadDigit(frame, sets, width,
                        x + offsetX + position * pitch - length * 3, y, indoor);
                    if (digit < 0 || (position == 0 && digit == 0)) { valid = false; break; }
                    number = number * 10 + digit;
                }
                if (valid) return number;
            }
            return null;
        }

        private static int ReadDigit(StageFrame frame, IReadOnlyList<bool[][]> sets,
            int width, int x0, int y0, bool indoor)
        {
            if (x0 < 0 || y0 < 0 || x0 + width > frame.Width || y0 + 8 > frame.Height) return -1;
            int bestDigit = -1, bestErrors = int.MaxValue, nextErrors = int.MaxValue;
            for (int digit = 0; digit < 10; digit++)
            {
                int digitErrors = int.MaxValue;
                foreach (var set in sets)
                {
                    int errors = 0;
                    var mask = set[digit];
                    for (int y = 0; y < 8; y++)
                        for (int x = 0; x < width; x++)
                        {
                            int i = ((y0 + y) * frame.Width + x0 + x) * 4;
                            if (mask[y * width + x] != IsDigitPixel(frame.Bgra, i, indoor)) errors++;
                        }
                    if (errors < digitErrors) digitErrors = errors;
                }
                if (digitErrors < bestErrors)
                {
                    nextErrors = bestErrors;
                    bestErrors = digitErrors;
                    bestDigit = digit;
                }
                else if (digitErrors < nextErrors) nextErrors = digitErrors;
            }
            return bestErrors <= 2 && nextErrors - bestErrors >= 2 ? bestDigit : -1;
        }

        private static bool IsDigitPixel(byte[] bgra, int i, bool indoor)
        {
            int b = bgra[i], g = bgra[i + 1], r = bgra[i + 2];
            if (indoor)
                return (r >= 238 && g >= 238 && b >= 238) ||
                    (r >= 128 && Math.Abs(r - g) <= 8 && Math.Abs(g - b) <= 8);
            return r >= 155 && g >= 225 && b >= 210 && g - r >= 18;
        }
    }
}
