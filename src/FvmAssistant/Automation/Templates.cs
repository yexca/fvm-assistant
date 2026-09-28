using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace FvmAssistant.Automation
{
    /// <summary>
    /// A small image expected at a fixed stage position. Files are named "(x,y)name.png" — the
    /// convention 轨道编辑器 uses, so its UI templates load unchanged. Transparent pixels are ignored.
    /// </summary>
    internal sealed class Template
    {
        private static readonly Regex FileName = new Regex(@"^\((\d+),(\d+)\)(.+)\.png$", RegexOptions.IgnoreCase);

        private readonly byte[] _bgra;
        private readonly int _opaque;

        private Template(string name, Point at, Size size, byte[] bgra)
        {
            Name = name;
            At = at;
            Size = size;
            _bgra = bgra;
            for (int i = 3; i < bgra.Length; i += 4)
                if (bgra[i] > 0) _opaque++;
        }

        public string Name { get; }
        public Point At { get; }
        public Size Size { get; }
        public Rectangle Bounds => new Rectangle(At, Size);

        public static bool TryParseFileName(string path, out Point at, out string name)
        {
            var m = FileName.Match(Path.GetFileName(path));
            at = m.Success ? new Point(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value)) : Point.Empty;
            name = m.Success ? m.Groups[3].Value : null;
            return m.Success;
        }

        public static Template Load(string path)
        {
            if (!TryParseFileName(path, out var at, out var name))
                throw new ArgumentException("template file must be named (x,y)name.png: " + path);
            using (var bmp = new Bitmap(path))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
                try
                {
                    var bgra = new byte[bmp.Width * bmp.Height * 4];
                    int rowBytes = bmp.Width * 4;
                    for (int y = 0; y < bmp.Height; y++)
                        Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * rowBytes, rowBytes);
                    return new Template(name, at, bmp.Size, bgra);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
            }
        }

        /// <summary>Mean absolute RGB difference (0-255) over opaque pixels; +∞ when out of bounds.</summary>
        public double Diff(StageFrame frame, int dx = 0, int dy = 0)
        {
            int x0 = At.X + dx, y0 = At.Y + dy;
            if (_opaque == 0 || x0 < 0 || y0 < 0 || x0 + Size.Width > frame.Width || y0 + Size.Height > frame.Height)
                return double.PositiveInfinity;

            long sum = 0;
            var f = frame.Bgra;
            for (int y = 0; y < Size.Height; y++)
            {
                int ti = y * Size.Width * 4;
                int fi = ((y0 + y) * frame.Width + x0) * 4;
                for (int x = 0; x < Size.Width; x++, ti += 4, fi += 4)
                {
                    if (_bgra[ti + 3] == 0) continue;
                    sum += Math.Abs(f[fi] - _bgra[ti]) + Math.Abs(f[fi + 1] - _bgra[ti + 1]) + Math.Abs(f[fi + 2] - _bgra[ti + 2]);
                }
            }
            return sum / (3.0 * _opaque);
        }

        /// <summary>
        /// Columns where at least <paramref name="minPixels"/> opaque pixels differ from the template by more
        /// than <paramref name="threshold"/> (summed RGB). Tells text in an empty-field template apart from a
        /// blinking caret, which a mean over the whole field cannot.
        /// </summary>
        public int ChangedColumns(StageFrame frame, int dx = 0, int dy = 0, int threshold = 90, int minPixels = 2)
        {
            int x0 = At.X + dx, y0 = At.Y + dy;
            if (x0 < 0 || y0 < 0 || x0 + Size.Width > frame.Width || y0 + Size.Height > frame.Height) return 0;
            var f = frame.Bgra;
            int columns = 0;
            for (int x = 0; x < Size.Width; x++)
            {
                int changed = 0;
                for (int y = 0; y < Size.Height; y++)
                {
                    int ti = (y * Size.Width + x) * 4, fi = ((y0 + y) * frame.Width + x0 + x) * 4;
                    if (_bgra[ti + 3] == 0) continue;
                    if (Math.Abs(f[fi] - _bgra[ti]) + Math.Abs(f[fi + 1] - _bgra[ti + 1]) + Math.Abs(f[fi + 2] - _bgra[ti + 2]) > threshold) changed++;
                }
                if (changed >= minPixels) columns++;
            }
            return columns;
        }
    }

    internal sealed class TemplateLibrary
    {
        private readonly Dictionary<string, Template> _byName = new Dictionary<string, Template>();

        public int Count => _byName.Count;

        /// <summary>Loads every "(x,y)name.png" in the directory; later loads override earlier names.</summary>
        public int LoadDirectory(string directory)
        {
            if (!Directory.Exists(directory)) return 0;
            int loaded = 0;
            foreach (var path in Directory.GetFiles(directory, "*.png"))
            {
                if (!Template.TryParseFileName(path, out _, out _)) continue;
                var t = Template.Load(path);
                _byName[t.Name] = t;
                loaded++;
            }
            return loaded;
        }

        public Template Get(string name) =>
            _byName.TryGetValue(name, out var t) ? t : throw new KeyNotFoundException("template not loaded: " + name);

        public bool Contains(string name) => _byName.ContainsKey(name);
    }
}
