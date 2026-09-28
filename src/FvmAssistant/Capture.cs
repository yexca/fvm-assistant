using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Windows.Forms;

namespace FvmAssistant
{
    /// <summary>
    /// The candidate ways of getting the game's pixels. All return the view's client area
    /// (page viewport) at 1:1, so crops use page coordinates. Window-based methods must run on the UI thread.
    /// </summary>
    internal static class Capture
    {
        /// <summary>Plain screen copy. Only correct while the window is visible and on top; the baseline.</summary>
        public static Bitmap FromScreen(Control view)
        {
            var size = view.ClientSize;
            var bmp = new Bitmap(size.Width, size.Height, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
                g.CopyFromScreen(view.PointToScreen(Point.Empty), Point.Empty, size);
            return bmp;
        }

        /// <summary>PrintWindow on the top-level form, then cut out the view.</summary>
        public static Bitmap PrintTopLevel(Control view)
        {
            var form = view.FindForm() ?? throw new InvalidOperationException("view has no form");
            using (var full = PrintWindowBitmap(form.Handle, out var windowRect))
            {
                var origin = view.PointToScreen(Point.Empty);
                var region = new Rectangle(origin.X - windowRect.Left, origin.Y - windowRect.Top, view.ClientSize.Width, view.ClientSize.Height);
                return Crop(full, region);
            }
        }

        /// <summary>The whole window including toolbar and panel (for looking at the UI itself).</summary>
        public static Bitmap PrintForm(Form form) => PrintWindowBitmap(form.Handle, out _);

        /// <summary>PrintWindow on CEF's own child window inside the view.</summary>
        public static Bitmap PrintChild(Control view)
        {
            var child = Native.FindDescendant(view.Handle, "Chrome_WidgetWin_0");
            if (child == IntPtr.Zero) throw new InvalidOperationException("CEF child window (Chrome_WidgetWin_0) not found");
            return PrintWindowBitmap(child, out _);
        }

        private static Bitmap PrintWindowBitmap(IntPtr hwnd, out Native.RECT rect)
        {
            if (!Native.GetWindowRect(hwnd, out rect)) throw new InvalidOperationException("GetWindowRect failed");
            int w = rect.Right - rect.Left, h = rect.Bottom - rect.Top;
            if (w <= 0 || h <= 0) throw new InvalidOperationException($"window rect is empty ({w}x{h})");
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppRgb);
            using (var g = Graphics.FromImage(bmp))
            {
                var hdc = g.GetHdc();
                try
                {
                    if (!Native.PrintWindow(hwnd, hdc, Native.PW_RENDERFULLCONTENT))
                        throw new InvalidOperationException("PrintWindow returned false");
                }
                finally
                {
                    g.ReleaseHdc(hdc);
                }
            }
            return bmp;
        }

        public static Bitmap FromPng(byte[] png)
        {
            using (var ms = new MemoryStream(png))
            using (var img = Image.FromStream(ms))
                return new Bitmap(img);
        }

        public static Bitmap Crop(Bitmap source, Rectangle region)
        {
            var clipped = Rectangle.Intersect(region, new Rectangle(Point.Empty, source.Size));
            if (clipped.Width <= 0 || clipped.Height <= 0)
                throw new InvalidOperationException($"crop {region} lies outside the {source.Width}x{source.Height} capture");
            return source.Clone(clipped, PixelFormat.Format32bppRgb);
        }
    }

    /// <summary>Cheap numbers that tell a blank/black/frozen capture apart from a live game frame.</summary>
    internal sealed class ImageStats
    {
        public int Width { get; private set; }
        public int Height { get; private set; }
        public double MeanLuma { get; private set; }
        public double StdLuma { get; private set; }

        /// <summary>Fraction of pixels with luma below 16.</summary>
        public double DarkRatio { get; private set; }

        /// <summary>Distinct 15-bit colours on an 8px sample grid; a flat image scores 1-2.</summary>
        public int SampledColors { get; private set; }

        /// <summary>Hash of the pixels, to see whether consecutive frames changed.</summary>
        public string Hash { get; private set; }

        public static ImageStats Of(Bitmap bmp)
        {
            var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppRgb);
            byte[] buf;
            int stride = data.Stride;
            try
            {
                buf = new byte[stride * bmp.Height];
                Marshal.Copy(data.Scan0, buf, 0, buf.Length);
            }
            finally
            {
                bmp.UnlockBits(data);
            }

            double sum = 0, sumSq = 0;
            long dark = 0;
            var colors = new HashSet<int>();
            for (int y = 0; y < bmp.Height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < bmp.Width; x++)
                {
                    int i = row + x * 4;
                    int b = buf[i], g = buf[i + 1], r = buf[i + 2];
                    buf[i + 3] = 0; // X channel of 32bppRgb is undefined; keep it out of the hash
                    double luma = 0.299 * r + 0.587 * g + 0.114 * b;
                    sum += luma;
                    sumSq += luma * luma;
                    if (luma < 16) dark++;
                    if ((x & 7) == 0 && (y & 7) == 0) colors.Add(((r >> 3) << 10) | ((g >> 3) << 5) | (b >> 3));
                }
            }

            double n = (double)bmp.Width * bmp.Height;
            double mean = sum / n;
            string hash;
            using (var sha = SHA1.Create())
                hash = BitConverter.ToString(sha.ComputeHash(buf), 0, 8).Replace("-", "").ToLowerInvariant();

            return new ImageStats
            {
                Width = bmp.Width,
                Height = bmp.Height,
                MeanLuma = Math.Round(mean, 1),
                StdLuma = Math.Round(Math.Sqrt(Math.Max(0, sumSq / n - mean * mean)), 1),
                DarkRatio = Math.Round(dark / n, 3),
                SampledColors = colors.Count,
                Hash = hash,
            };
        }
    }
}
