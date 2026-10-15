using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace FvmAssistant.Automation
{
    /// <summary>Immutable copy of the Flash stage pixels (BGRA, stride = width * 4) in stage coordinates.</summary>
    internal sealed class StageFrame
    {
        private StageFrame(int width, int height, byte[] bgra)
        {
            Width = width;
            Height = height;
            Bgra = bgra;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] Bgra { get; }

        public static StageFrame FromBitmap(Bitmap full, Rectangle stage)
        {
            if (!new Rectangle(Point.Empty, full.Size).Contains(stage))
                throw new InvalidOperationException($"stage {stage} is outside the {full.Width}x{full.Height} frame");

            var data = full.LockBits(stage, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var bgra = new byte[stage.Width * stage.Height * 4];
                int rowBytes = stage.Width * 4;
                for (int y = 0; y < stage.Height; y++)
                    Marshal.Copy(data.Scan0 + y * data.Stride, bgra, y * rowBytes, rowBytes);
                return new StageFrame(stage.Width, stage.Height, bgra);
            }
            finally
            {
                full.UnlockBits(data);
            }
        }

        public StageFrame Crop(Rectangle region)
        {
            if (!new Rectangle(0, 0, Width, Height).Contains(region))
                throw new ArgumentOutOfRangeException(nameof(region), region.ToString());
            var bgra = new byte[region.Width * region.Height * 4];
            int rowBytes = region.Width * 4;
            for (int y = 0; y < region.Height; y++)
                Buffer.BlockCopy(Bgra, ((region.Y + y) * Width + region.X) * 4, bgra, y * rowBytes, rowBytes);
            return new StageFrame(region.Width, region.Height, bgra);
        }

        /// <summary>
        /// A copy moved by whole pixels: pixel (x, y) of the result is (x + dx, y + dy) of this frame,
        /// edges repeated. Re-aligns frames that were cropped a pixel off the stage.
        /// </summary>
        public StageFrame Shifted(int dx, int dy)
        {
            if (dx == 0 && dy == 0) return this;
            if (Math.Abs(dx) >= Width || Math.Abs(dy) >= Height) throw new ArgumentOutOfRangeException(nameof(dx));
            var bgra = new byte[Bgra.Length];
            int row = Width * 4, keep = (Width - Math.Abs(dx)) * 4;
            for (int y = 0; y < Height; y++)
            {
                int src = Math.Min(Height - 1, Math.Max(0, y + dy)) * row, dst = y * row;
                Buffer.BlockCopy(Bgra, src + Math.Max(0, dx) * 4, bgra, dst + Math.Max(0, -dx) * 4, keep);
                for (int x = 0; x < Width; x++)
                {
                    if (x + dx >= 0 && x + dx < Width) continue;
                    Buffer.BlockCopy(Bgra, src + (x + dx < 0 ? 0 : Width - 1) * 4, bgra, dst + x * 4, 4);
                }
            }
            return new StageFrame(Width, Height, bgra);
        }

        /// <summary>Pixels whose summed RGB difference exceeds <paramref name="threshold"/>; for small changes a mean would dilute.</summary>
        public int CountChanged(StageFrame other, int threshold = 60)
        {
            if (other.Width != Width || other.Height != Height) throw new ArgumentException("frame sizes differ");
            int n = 0;
            for (int i = 0; i + 3 < Bgra.Length; i += 4)
                if (Math.Abs(Bgra[i] - other.Bgra[i]) + Math.Abs(Bgra[i + 1] - other.Bgra[i + 1]) + Math.Abs(Bgra[i + 2] - other.Bgra[i + 2]) > threshold)
                    n++;
            return n;
        }

        /// <summary>Mean absolute RGB difference (0-255) against a frame of the same size.</summary>
        public double MeanAbsDiff(StageFrame other)
        {
            if (other.Width != Width || other.Height != Height) throw new ArgumentException("frame sizes differ");
            long sum = 0;
            for (int i = 0; i < Bgra.Length; i += 4)
                sum += Math.Abs(Bgra[i] - other.Bgra[i]) + Math.Abs(Bgra[i + 1] - other.Bgra[i + 1]) + Math.Abs(Bgra[i + 2] - other.Bgra[i + 2]);
            return sum / (3.0 * Width * Height);
        }

        /// <summary>Mean of max(R,G,B)/255 over the region (HSV "value").</summary>
        public double MeanValue(Rectangle region)
        {
            var r = Rectangle.Intersect(region, new Rectangle(0, 0, Width, Height));
            if (r.Width <= 0 || r.Height <= 0) return 0;
            long sum = 0;
            for (int y = r.Top; y < r.Bottom; y++)
            {
                int i = (y * Width + r.Left) * 4;
                for (int x = r.Left; x < r.Right; x++, i += 4)
                    sum += Math.Max(Bgra[i], Math.Max(Bgra[i + 1], Bgra[i + 2]));
            }
            return sum / (255.0 * r.Width * r.Height);
        }

        public Bitmap ToBitmap()
        {
            using (var bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = Width * 4;
                    for (int y = 0; y < Height; y++)
                        Marshal.Copy(Bgra, y * rowBytes, data.Scan0 + y * data.Stride, rowBytes);
                }
                finally
                {
                    bmp.UnlockBits(data);
                }
                // Frames come from an opaque page; force alpha so saved PNGs are not transparent.
                return bmp.Clone(new Rectangle(0, 0, Width, Height), PixelFormat.Format24bppRgb);
            }
        }
    }
}
