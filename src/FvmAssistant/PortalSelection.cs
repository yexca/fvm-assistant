using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CefSharp;

namespace FvmAssistant
{
    internal sealed class PortalElement
    {
        public string tag { get; set; }
        public string type { get; set; }
        public string label { get; set; }
        public int x { get; set; }
        public int y { get; set; }
        public int w { get; set; }
        public int h { get; set; }
        public int CenterX => x + w / 2;
        public int CenterY => y + h / 2;
    }

    internal sealed class PortalScan
    {
        public List<PortalElement> inputs { get; set; } = new List<PortalElement>();
        public List<PortalElement> entries { get; set; } = new List<PortalElement>();
    }

    /// <summary>Finds the visible server field and entry control; clicks them through CEF input.</summary>
    internal static class PortalSelection
    {
        private const string ScanScript = @"(function () {
            function visible(e) {
                var r = e.getBoundingClientRect();
                return r.width > 12 && r.height > 10 && r.left >= 0 && r.top >= 0 &&
                    r.right <= window.innerWidth && r.bottom <= window.innerHeight;
            }
            function item(e) {
                var r = e.getBoundingClientRect();
                return { tag: e.tagName.toLowerCase(), type: e.type || '',
                    label: ((e.innerText || e.value || e.alt || '') + '').trim().slice(0, 20),
                    x: Math.round(r.left), y: Math.round(r.top),
                    w: Math.round(r.width), h: Math.round(r.height) };
            }
            var inputs = Array.from(document.querySelectorAll('input'))
                .filter(e => visible(e) && (e.type === 'text' || e.type === 'number'))
                .map(e => { var data = item(e); data.label = ''; return data; });
            var entries = Array.from(document.querySelectorAll('a,button,input,[role=button]'))
                .filter(e => visible(e) && ((e.innerText || e.value || e.alt || '').trim() === '进入'))
                .map(item);
            return JSON.stringify({ inputs: inputs, entries: entries });
        })()";

        public static async Task<PortalScan> ScanAsync(IBrowser browser)
        {
            var frame = browser.GetFrameIdentifiers().Select(browser.GetFrame).FirstOrDefault(f => f != null && f.IsValid && f.IsMain);
            if (frame == null) throw new InvalidOperationException("The server page is not ready.");
            var response = await frame.EvaluateScriptAsync(ScanScript, timeout: TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            if (!response.Success || !(response.Result is string json))
                throw new InvalidOperationException("The server controls could not be inspected.");
            return Json.Deserialize<PortalScan>(json) ?? new PortalScan();
        }

        public static (PortalElement input, PortalElement entry) FindPair(PortalScan scan)
        {
            var pair = (from input in scan.inputs
                        from entry in scan.entries
                        where input.CenterX < entry.CenterX &&
                              Math.Abs(input.CenterY - entry.CenterY) <= 35 &&
                              entry.CenterX - input.CenterX < 300
                        orderby Math.Abs(input.CenterY - entry.CenterY), entry.CenterX - input.CenterX
                        select (input, entry)).FirstOrDefault();
            if (pair.input == null || pair.entry == null)
                throw new InvalidOperationException("The server number field and Enter button were not found together on the portal page.");
            return pair;
        }
    }
}
