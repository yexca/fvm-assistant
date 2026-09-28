using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CefSharp;

namespace FvmAssistant
{
    /// <summary>
    /// Handlers shared by both view modes: keep popups in the same browser, record which hosts
    /// the game talks to (for a future navigation allowlist) and surface plugin/renderer crashes.
    /// </summary>
    internal sealed class BrowserHooks
    {
        private const string GameFrameHost = "td-4399.123u.com";
        private const int MaxAutoReloads = 3;
        private static readonly TimeSpan GameFrameTimeout = TimeSpan.FromSeconds(30);

        private readonly bool _autoDialogs;
        private IWebBrowser _browser;
        private int _pluginCrashes;
        private int _renderTerminations;
        private int _autoReloads;
        private int _gameFrameRequests;
        private int _gameFrameSettled;
        private long _lastAutoReloadTicks;

        public BrowserHooks(bool autoDialogs)
        {
            _autoDialogs = autoDialogs;
        }

        public ConcurrentDictionary<string, int> Hosts { get; } = new ConcurrentDictionary<string, int>();
        public string Title { get; private set; } = "";
        public int PluginCrashes => _pluginCrashes;
        public int RenderTerminations => _renderTerminations;

        public void Attach(IGameView view)
        {
            var browser = view.WebBrowser;
            _browser = browser;
            browser.LifeSpanHandler = new PopupsInPlace();
            browser.RequestHandler = new Recorder(this);
            browser.JsDialogHandler = new Dialogs(_autoDialogs);

            view.TitleChanged += (s, e) => Title = e.Title ?? "";
            browser.LoadError += (s, e) =>
            {
                bool gameFrame = DnsProbe.GameHosts.Any(h => h.host == UrlText.HostOf(e.FailedUrl));
                if (gameFrame) GameFrameSettled();
                if (e.ErrorCode == CefErrorCode.Aborted) return;
                Log.Warn($"load error {e.ErrorCode} {UrlText.Redact(e.FailedUrl)} main={e.Frame?.IsMain}");

                // The game iframe timing out leaves a black page; a reload normally recovers it.
                if (gameFrame && (e.ErrorCode == CefErrorCode.TimedOut || e.ErrorCode == CefErrorCode.ConnectionTimedOut))
                    AutoReload("game frame timed out");
            };
            browser.FrameLoadStart += (s, e) =>
            {
                if (e.Frame.IsMain) GameFrameSettled();
            };
            browser.FrameLoadEnd += (s, e) =>
            {
                Log.Info($"frame loaded {e.HttpStatusCode} main={e.Frame.IsMain} {UrlText.Redact(e.Url)}");
                if (!e.Frame.IsMain && UrlText.HostOf(e.Url) == GameFrameHost)
                {
                    GameFrameSettled();
                    if (e.HttpStatusCode == 200) Interlocked.Exchange(ref _autoReloads, 0);
                }
            };
            browser.ConsoleMessage += (s, e) =>
            {
                if (e.Level >= LogSeverity.Warning || e.Message.IndexOf("plugin", System.StringComparison.OrdinalIgnoreCase) >= 0)
                    Log.Info($"console[{e.Level}] {e.Message} ({UrlText.Redact(e.Source)}:{e.Line})");
            };
        }

        /// <summary>
        /// A request to a stalled CDN node can hang for minutes without Chromium reporting an error,
        /// so give the game iframe a deadline of its own.
        /// </summary>
        private void GameFrameRequested()
        {
            int request = Interlocked.Increment(ref _gameFrameRequests);
            Task.Delay(GameFrameTimeout).ContinueWith(_ =>
            {
                if (Volatile.Read(ref _gameFrameRequests) == request && Volatile.Read(ref _gameFrameSettled) < request)
                    AutoReload($"game frame still loading after {GameFrameTimeout.TotalSeconds:0} s");
            });
        }

        private void GameFrameSettled() => Volatile.Write(ref _gameFrameSettled, Volatile.Read(ref _gameFrameRequests));

        /// <summary>Reloads the page, at most <see cref="MaxAutoReloads"/> times until the game frame loads again.</summary>
        private void AutoReload(string reason)
        {
            // The deadline and Chromium's own ~30 s timeout can fire together for the same request.
            long now = DateTime.UtcNow.Ticks, last = Interlocked.Read(ref _lastAutoReloadTicks);
            if (now - last < TimeSpan.FromSeconds(10).Ticks || Interlocked.CompareExchange(ref _lastAutoReloadTicks, now, last) != last)
            {
                Log.Info($"{reason}; a reload is already under way");
                return;
            }

            int attempt = Interlocked.Increment(ref _autoReloads);
            if (attempt > MaxAutoReloads)
            {
                if (attempt == MaxAutoReloads + 1) Log.Warn($"{reason}; giving up on automatic reloads");
                return;
            }
            Log.Warn($"{reason}; reloading the page (attempt {attempt}/{MaxAutoReloads})");
            Task.Delay(2000).ContinueWith(_ => _browser.Reload());
        }

        private void RecordHost(string url)
        {
            var host = UrlText.HostOf(url);
            if (host != null) Hosts.AddOrUpdate(host, 1, (_, n) => n + 1);
        }

        private sealed class PopupsInPlace : ILifeSpanHandler
        {
            public bool OnBeforePopup(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, string targetUrl, string targetFrameName,
                WindowOpenDisposition targetDisposition, bool userGesture, IPopupFeatures popupFeatures, IWindowInfo windowInfo,
                IBrowserSettings browserSettings, ref bool noJavascriptAccess, out IWebBrowser newBrowser)
            {
                newBrowser = null;
                Log.Info($"popup -> same browser: {UrlText.Redact(targetUrl)}");
                if (UrlText.IsWeb(targetUrl)) chromiumWebBrowser.Load(targetUrl);
                return true;
            }

            public void OnAfterCreated(IWebBrowser chromiumWebBrowser, IBrowser browser) { }

            public bool DoClose(IWebBrowser chromiumWebBrowser, IBrowser browser) => false;

            public void OnBeforeClose(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
        }

        private sealed class Recorder : CefSharp.Handler.RequestHandler
        {
            private readonly BrowserHooks _owner;

            public Recorder(BrowserHooks owner)
            {
                _owner = owner;
            }

            protected override bool OnBeforeBrowse(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame, IRequest request, bool userGesture, bool isRedirect)
            {
                var url = request.Url ?? "";
                if (UrlText.IsWeb(url) || url.StartsWith("about:") || url.StartsWith("data:") || url.StartsWith("blob:") || url.StartsWith("devtools:"))
                    return false;
                Log.Warn($"blocked navigation to {UrlText.Redact(url)}");
                return true;
            }

            protected override IResourceRequestHandler GetResourceRequestHandler(IWebBrowser chromiumWebBrowser, IBrowser browser, IFrame frame,
                IRequest request, bool isNavigation, bool isDownload, string requestInitiator, ref bool disableDefaultHandling)
            {
                _owner.RecordHost(request.Url);
                if (isNavigation && frame != null && !frame.IsMain && UrlText.HostOf(request.Url) == GameFrameHost)
                    _owner.GameFrameRequested();
                return null;
            }

            protected override void OnPluginCrashed(IWebBrowser chromiumWebBrowser, IBrowser browser, string pluginPath)
            {
                Interlocked.Increment(ref _owner._pluginCrashes);
                Log.Error("plugin crashed: " + pluginPath);
            }

            protected override void OnRenderProcessTerminated(IWebBrowser chromiumWebBrowser, IBrowser browser, CefTerminationStatus status)
            {
                Interlocked.Increment(ref _owner._renderTerminations);
                Log.Error("render process terminated: " + status);
            }
        }

        /// <summary>
        /// Windowed mode shows CEF's native dialogs. Off-screen mode has nowhere to show them,
        /// so alerts are accepted and confirm/prompt are cancelled (and logged).
        /// </summary>
        private sealed class Dialogs : IJsDialogHandler
        {
            private readonly bool _auto;

            public Dialogs(bool auto)
            {
                _auto = auto;
            }

            public bool OnJSDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, string originUrl, CefJsDialogType dialogType,
                string messageText, string defaultPromptText, IJsDialogCallback callback, ref bool suppressMessage)
            {
                Log.Info($"js {dialogType} from {UrlText.Redact(originUrl)}: {messageText}");
                if (!_auto) return false;
                callback.Continue(dialogType == CefJsDialogType.Alert);
                return true;
            }

            public bool OnBeforeUnloadDialog(IWebBrowser chromiumWebBrowser, IBrowser browser, string messageText, bool isReload, IJsDialogCallback callback)
            {
                if (!_auto) return false;
                callback.Continue(true);
                return true;
            }

            public void OnResetDialogState(IWebBrowser chromiumWebBrowser, IBrowser browser) { }

            public void OnDialogClosed(IWebBrowser chromiumWebBrowser, IBrowser browser) { }
        }
    }
}
