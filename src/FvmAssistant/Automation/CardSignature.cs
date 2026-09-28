using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace FvmAssistant.Automation
{
    /// <summary>Foreground colors learned from a held card, without storing account images on disk.</summary>
    internal sealed class CardSignature
    {
        private readonly HashSet<int> _colors;

        private CardSignature(HashSet<int> colors) { _colors = colors; }

        // Quantization absorbs minor rendering differences. Matching counts distinct colors so a
        // large flat patch in the background cannot dominate the evidence for a card.
        private static int Color(byte[] pixels, int offset) =>
            (pixels[offset] >> 3) | ((pixels[offset + 1] >> 3) << 5) | ((pixels[offset + 2] >> 3) << 10);

        private static Dictionary<int, int> Colors(StageFrame frame)
        {
            var counts = new Dictionary<int, int>();
            for (int i = 0; i < frame.Bgra.Length; i += 4)
            {
                int color = Color(frame.Bgra, i);
                counts[color] = counts.TryGetValue(color, out var count) ? count + 1 : 1;
            }
            return counts;
        }

        public static CardSignature Learn(StageFrame background1, StageFrame background2, StageFrame held1, StageFrame held2)
        {
            var background = new HashSet<int>(Colors(background1).Keys.Concat(Colors(background2).Keys));
            var first = Colors(held1);
            var second = Colors(held2);
            var colors = new HashSet<int>(first.Where(p => p.Value >= 2 && !background.Contains(p.Key) &&
                second.TryGetValue(p.Key, out var count) && count >= 2).Select(p => p.Key));
            // Weak or unchanged captures are left unclassified and use the normal retry policy.
            return colors.Count >= 12 ? new CardSignature(colors) : null;
        }

        public double Score(StageFrame frame, Rectangle region)
        {
            region = Rectangle.Intersect(region, new Rectangle(0, 0, frame.Width, frame.Height));
            var found = new HashSet<int>();
            for (int y = region.Top; y < region.Bottom; y++)
                for (int x = region.Left; x < region.Right; x++)
                {
                    int color = Color(frame.Bgra, (y * frame.Width + x) * 4);
                    if (_colors.Contains(color)) found.Add(color);
                }
            return found.Count / (double)_colors.Count;
        }
    }
}
