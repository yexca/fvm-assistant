using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using CefSharp;

namespace FvmAssistant
{
    internal sealed class PluginInfo
    {
        public long FrameId { get; set; }
        public string FrameUrl { get; set; }
        public string Tag { get; set; }
        public string Type { get; set; }
        public string Src { get; set; }

        /// <summary>Position in main-frame (viewport) pixels.</summary>
        public RectangleF PageRect { get; set; }

        /// <summary>
        /// <see cref="PageRect"/> in the whole pixels it is drawn at. The page centres the 950 px game, so
        /// an odd page width puts it at x.5, which Chromium draws at the next pixel (122.5 → 123).
        /// Rectangle.Round rounds halves to even (122) and shifted every frame and click by a pixel,
        /// enough to fail every template.
        /// </summary>
        public Rectangle StageRect => new Rectangle(Snap(PageRect.X), Snap(PageRect.Y), Snap(PageRect.Width), Snap(PageRect.Height));

        private static int Snap(float v) => (int)Math.Floor(v + 0.5f);

        /// <summary>True when an iframe offset on the path had to be guessed.</summary>
        public bool OffsetGuessed { get; set; }

        public bool LooksLikeFlash =>
            (Type ?? "").IndexOf("shockwave-flash", StringComparison.OrdinalIgnoreCase) >= 0 ||
            (Src ?? "").IndexOf(".swf", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal sealed class LocateResult
    {
        public List<PluginInfo> Plugins { get; } = new List<PluginInfo>();
        public int ScannedFrames { get; set; }
        public PluginInfo Best { get; set; }
    }

    /// <summary>
    /// Finds &lt;embed&gt;/&lt;object&gt; elements in every frame (the game usually sits in a
    /// cross-origin iframe) and maps their rectangles into main-frame coordinates.
    /// </summary>
    internal static class FlashLocator
    {
        private const string ScanScript = @"(function(){
  function box(e){var b=e.getBoundingClientRect();return {x:b.left,y:b.top,w:b.width,h:b.height};}
  var out={url:location.href,plugins:[],frames:[]};
  var ps=document.querySelectorAll('embed,object');
  for(var i=0;i<ps.length;i++){var e=ps[i],o=box(e);o.tag=e.tagName;o.type=e.type||'';o.src=e.src||e.data||'';o.id=e.id||'';o.name=e.name||'';out.plugins.push(o);}
  var fs=document.querySelectorAll('iframe,frame');
  for(var j=0;j<fs.length;j++){var f=fs[j],b=f.getBoundingClientRect(),cs=getComputedStyle(f);
    out.frames.push({x:b.left+f.clientLeft+(parseFloat(cs.paddingLeft)||0),y:b.top+f.clientTop+(parseFloat(cs.paddingTop)||0),
      w:f.clientWidth,h:f.clientHeight,src:f.src||'',id:f.id||'',name:f.name||'',tag:f.tagName,type:''});}
  return JSON.stringify(out);
})()";

        // Property names match the JSON produced by ScanScript.
        private sealed class FrameScan
        {
            public string url { get; set; }
            public List<ElementScan> plugins { get; set; }
            public List<ElementScan> frames { get; set; }
        }

        private sealed class ElementScan
        {
            public double x { get; set; }
            public double y { get; set; }
            public double w { get; set; }
            public double h { get; set; }
            public string tag { get; set; }
            public string type { get; set; }
            public string src { get; set; }
            public string id { get; set; }
            public string name { get; set; }
        }

        public static async Task<LocateResult> LocateAsync(IBrowser browser)
        {
            var frames = new Dictionary<long, IFrame>();
            foreach (var id in browser.GetFrameIdentifiers())
            {
                var frame = browser.GetFrame(id);
                if (frame != null && frame.IsValid) frames[id] = frame;
            }

            var scans = new Dictionary<long, FrameScan>();
            foreach (var pair in frames)
            {
                try
                {
                    var response = await pair.Value.EvaluateScriptAsync(ScanScript, timeout: TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                    if (response.Success && response.Result is string json)
                        scans[pair.Key] = Json.Deserialize<FrameScan>(json);
                    else
                        Log.Warn($"scan frame {pair.Key} failed: {response.Message}");
                }
                catch (Exception e)
                {
                    Log.Warn($"scan frame {pair.Key} threw: {e.Message}");
                }
            }

            var offsets = new Dictionary<long, (PointF origin, bool guessed)>();
            (PointF origin, bool guessed) OffsetOf(long frameId, int depth)
            {
                if (offsets.TryGetValue(frameId, out var known)) return known;
                var result = (PointF.Empty, false);
                if (depth < 16 && frames.TryGetValue(frameId, out var frame) && !frame.IsMain)
                {
                    var parent = frame.Parent;
                    if (parent == null)
                    {
                        result = (PointF.Empty, true);
                    }
                    else
                    {
                        var (parentOrigin, parentGuessed) = OffsetOf(parent.Identifier, depth + 1);
                        if (scans.TryGetValue(parent.Identifier, out var parentScan))
                        {
                            var element = MatchIframe(parentScan.frames ?? new List<ElementScan>(), frame, out var guessed);
                            result = element == null
                                ? (parentOrigin, true)
                                : (new PointF(parentOrigin.X + (float)element.x, parentOrigin.Y + (float)element.y), parentGuessed || guessed);
                        }
                        else
                        {
                            result = (parentOrigin, true);
                        }
                    }
                }
                offsets[frameId] = result;
                return result;
            }

            var located = new LocateResult { ScannedFrames = scans.Count };
            foreach (var pair in scans)
            {
                var (origin, guessed) = OffsetOf(pair.Key, 0);
                foreach (var p in pair.Value.plugins ?? new List<ElementScan>())
                {
                    located.Plugins.Add(new PluginInfo
                    {
                        FrameId = pair.Key,
                        FrameUrl = pair.Value.url,
                        Tag = p.tag,
                        Type = p.type,
                        Src = p.src,
                        PageRect = new RectangleF(origin.X + (float)p.x, origin.Y + (float)p.y, (float)p.w, (float)p.h),
                        OffsetGuessed = guessed,
                    });
                }
            }

            located.Best = located.Plugins
                .Where(p => p.PageRect.Width >= 100 && p.PageRect.Height >= 100)
                .OrderByDescending(p => p.LooksLikeFlash)
                .ThenByDescending(p => p.PageRect.Width * p.PageRect.Height)
                .FirstOrDefault();
            return located;
        }

        private static ElementScan MatchIframe(List<ElementScan> candidates, IFrame child, out bool guessed)
        {
            guessed = false;
            var name = child.Name;
            if (!string.IsNullOrEmpty(name))
            {
                var byName = candidates.FirstOrDefault(c => c.name == name || c.id == name);
                if (byName != null) return byName;
            }

            var byUrl = candidates.Where(c => SameDocument(c.src, child.Url)).ToList();
            if (byUrl.Count == 1) return byUrl[0];

            // Redirected or script-written iframes: fall back to the largest visible one.
            guessed = true;
            return candidates.Where(c => c.w > 0 && c.h > 0).OrderByDescending(c => c.w * c.h).FirstOrDefault();
        }

        private static bool SameDocument(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return false;
            if (!Uri.TryCreate(a, UriKind.Absolute, out var ua) || !Uri.TryCreate(b, UriKind.Absolute, out var ub)) return a == b;
            return ua.GetLeftPart(UriPartial.Query) == ub.GetLeftPart(UriPartial.Query);
        }
    }
}
