using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CefSharp;
using FvmAssistant.Automation;

namespace FvmAssistant
{
    /// <summary>The control endpoints. Every endpoint is a GET returning JSON; see /help.</summary>
    internal sealed class AssistantApi
    {
        private static readonly string[] CaptureMethods = { "osr", "devtools", "printwindow", "printwindow-child", "screen", "form" };

        private readonly MainForm _form;
        private readonly AppPaths _paths;
        private readonly AssistantOptions _options;
        private readonly DevToolsBridge _devtools = new DevToolsBridge();
        private readonly SemaphoreSlim _captureLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _recordLock = new SemaphoreSlim(1, 1);
        private readonly GrindController _grind;
        private readonly AgentApi _agent;
        private PluginInfo _flash;
        private Recorder _recorder;

        public AssistantApi(MainForm form, AppPaths paths, AssistantOptions options, GrindController grind)
        {
            _form = form;
            _paths = paths;
            _options = options;
            _grind = grind;
            _agent = new AgentApi(form, options, grind);
            _form.ToggleRecording = ToggleRecordingAsync;
        }

        public Task<object> HandleAsync(HttpRequest r)
        {
            if (r.Path.StartsWith("/agent/v1/", StringComparison.Ordinal)) return _agent.HandleAsync(r);
            switch (r.Path.TrimEnd('/'))
            {
                case "":
                case "/help": return Task.FromResult(Help());
                case "/status": return Task.FromResult(Status(r));
                case "/frames": return Task.FromResult(Frames(r));
                case "/locate": return LocateAsync(r);
                case "/capture": return CaptureAsync(r);
                case "/sample": return SampleAsync(r);
                case "/click": return PointerAsync(r, click: true);
                case "/move": return PointerAsync(r, click: false);
                case "/drag": return DragAsync(r);
                case "/window": return WindowAsync(r);
                case "/navigate": return Task.FromResult(Navigate(r));
                case "/reload": return Task.FromResult(Reload());
                case "/type": return TypeAsync(r);
                case "/hosts": return Task.FromResult(Hosts());
                case "/record": return RecordAsync(r);
                case "/detect": return DetectAsync(r);
                case "/grind": return GrindAsync(r);
                case "/duo": return DuoAsync(r);
                case "/track": return Track(r);
                case "/record-track": return RecordTrackAsync(r);
                case "/cards": return CardsAsync(r);
                case "/inventory": return InventoryAsync(r);
                case "/portal": return PortalAsync(r);
                case "/deck": return DeckAsync(r);
                case "/missions": return MissionsAsync(r);
                case "/guild-tasks": return MissionsAsync(r);
                default: throw new HttpError(404, $"unknown endpoint {r.Path}; see /help");
            }
        }

        private static object Help() => new
        {
            header = ControlServer.RequiredHeader + ": 1",
            endpoints = new[]
            {
                "/agent/v1/state|levels|tracks|track|cards|validate|start|run|stop|capture   structured local Agent API; see docs/agent-api.md",
                "/status[?full=1]",
                "/frames[?full=1]",
                "/locate                       find the Flash <embed>/<object> and remember its rect",
                "/capture?method=M&crop=C&label=L   M: osr|devtools|printwindow|printwindow-child|screen; C: flash|full|x,y,w,h",
                "/sample?method=M&crop=C&count=4&interval=500   repeated captures; shows whether frames keep changing",
                "/move?x=&y=&space=flash|page&method=cef|post",
                "/click?x=&y=&space=flash|page&method=cef|post[&button=left|right]   right requires cef",
                "/drag?fromX=&fromY=&toX=&toY=&space=flash|page   attended CEF left-button drag",
                "/window?state=normal|minimize|offscreen|onscreen|bottom|top",
                "/navigate?url=https://…",
                "/reload",
                "/hosts                        hosts requested so far",
                "/record[?state=start|stop&interval=3000]   record manual mouse input + frames (osr only)",
                "/detect[?slots=5]             which game screen is showing, template scores, card slot brightness",
                "/grind?state=start&rounds=1&track=通用轨道/6美味/1曲奇.txt&map=小火炉:1,海星:5&slots=5[&level=美味/曲奇][&shovel=1]",
                "/grind?state=finish-round   finish the current solo round, collect rewards, then return to town",
                "/grind?state=stop | /grind    immediate stop / progress",
                "/duo?state=start&role=host|guest&rounds=N&track=...&cards=...   start in an already paired room",
                "/duo?state=start&role=host|guest&level=...&password=1234&room=N   pair automatically (room is guest-only)",
                "/duo?state=plan&track=...&cards=...&level=...   inspect one account's conservative duo formation",
                "/duo?state=release&phase=room|battle|boss&run=ID&round=N   coordinator gate; /duo shows progress",
                "/duo?state=stop   stop the paired run on this account",
                "/track?path=...&level=岛/关卡[&shovel=1][&cards=轨道卡:我的卡 | &map=轨道卡:1]   inspect compatibility without game input",
                "   ...&cards=小火炉:日光炉,海星:红柳烤串机   build this deck in the room first (replaces map/slots)",
                "   ...&boss=1  enter the boss stage (uses a key)   ...&autoflame=1  account picks up flames itself",
                "/cards[?learn=日光炉,,,,红柳烤串机]   list learned card icons / learn them from the current deck bar",
                "/inventory?state=capture   capture the manually opened 背包 page for reviewed card learning",
                "/inventory?state=learn&id=...&items=JSON[&replace=1]   save named 42x50 icons; items: [{Name,X,Y}]; items64 accepts UTF-8 base64 JSON",
                "/deck?cards=日光炉,红柳烤串机   empty the room's deck and add these cards in order",
                "/record-track[?rec=<recording-id>][&save=曲奇-录制[&overwrite=1]]   read a recording back as a track (default: newest)",
                "/portal?state=select&server=<server-number>   choose a server through the visible 4399 portal controls",
                "/guild-tasks?state=publish   publish guild-leader tasks once, using visible controls",
                "/guild-tasks?state=open   open the guild task board and return fresh evidence",
                "/missions?state=open   open the account task board and return fresh evidence",
                "/missions?state=inspect   current board state and guarded, private capture",
                "/missions?state=select&row=Y&listHash=...   select an observed task row and return fresh evidence (may auto-accept)",
                "/missions?state=claim&selectedHash=...   claim a verified completed reward",
                "/missions?state=accept&row=Y&listHash=...   click a visible task and verify 任务进行中",
            },
        };

        // ---------- state ----------
        private Task<object> MissionsAsync(HttpRequest r) => Guard<object>(async () =>
        {
            var action = r.Q("state", "inspect");
            if (r.Path == "/guild-tasks" && action == "open") action = "open-guild";
            return await _grind.MissionActionAsync(action, r.QInt("row", 0),
                r.Q("listHash"), r.Q("selectedHash")).ConfigureAwait(false);
        });

        private object Status(HttpRequest r)
        {
            var view = _form.View;
            var offscreen = view as OsrView;
            bool rendererReady = offscreen == null || offscreen.RendererAvailable;
            return new
            {
                mode = _options.Mode.ToString(),
                profile = _options.Profile,
                gpu = _options.Gpu,
                keepRendering = _options.KeepRendering,
                flashPlugin = CefBootstrap.FlashVersion,
                browserReady = rendererReady && view.BrowserOrNull != null,
                rendererReady,
                rendererUnavailableReason = offscreen?.RendererUnavailableReason,
                loading = view.WebBrowser.IsLoading,
                url = UrlText.Redact(view.WebBrowser.Address, r.Flag("full")),
                title = _form.Hooks.Title,
                pluginCrashes = _form.Hooks.PluginCrashes,
                renderTerminations = _form.Hooks.RenderTerminations,
                window = WindowInfo(),
                flash = rendererReady ? PluginJson(_flash, r.Flag("full")) : null,
                captureDir = _paths.Captures,
                logFile = _paths.LogFile,
            };
        }

        private object Frames(HttpRequest r)
        {
            var browser = RequireBrowser();
            bool full = r.Flag("full");
            var frames = browser.GetFrameIdentifiers()
                .Select(id => browser.GetFrame(id))
                .Where(f => f != null && f.IsValid)
                .Select(f => (object)new { id = f.Identifier, parent = f.Parent?.Identifier, main = f.IsMain, name = f.Name, url = UrlText.Redact(f.Url, full) })
                .ToList();
            return new { count = frames.Count, frames };
        }

        private async Task<object> LocateAsync(HttpRequest r)
        {
            var result = await FlashLocator.LocateAsync(RequireBrowser()).ConfigureAwait(false);
            Volatile.Write(ref _flash, result.Best);
            bool full = r.Flag("full");
            _form.SetStatus(result.Best == null ? "未找到 Flash" : $"Flash @ {Fmt(result.Best.PageRect)}");
            return new
            {
                scannedFrames = result.ScannedFrames,
                best = PluginJson(result.Best, full),
                plugins = result.Plugins.Select(p => PluginJson(p, full)).ToList(),
            };
        }

        private async Task<PluginInfo> RequireFlashAsync()
        {
            var flash = Volatile.Read(ref _flash);
            if (flash != null) return flash;
            flash = (await FlashLocator.LocateAsync(RequireBrowser()).ConfigureAwait(false)).Best;
            Volatile.Write(ref _flash, flash);
            return flash ?? throw new HttpError(409, "no Flash object on the page yet; wait for the game to load, then /locate");
        }

        // ---------- capture ----------

        private async Task<object> CaptureAsync(HttpRequest r)
        {
            var method = CaptureMethod(r);
            var crop = r.Q("crop", "flash");
            var label = Regex.Replace(r.Q("label", ""), "[^A-Za-z0-9_-]", "");

            await _captureLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var sw = Stopwatch.StartNew();
                using (var full = await GrabAsync(method).ConfigureAwait(false))
                {
                    long grabMs = sw.ElapsedMilliseconds;
                    var region = await ResolveCropAsync(crop).ConfigureAwait(false);
                    using (var image = region.HasValue ? Capture.Crop(full, region.Value) : (Bitmap)full.Clone())
                    {
                        var path = Save(image, method, label);
                        _form.SetStatus($"截图 {method} → {Path.GetFileName(path)}");
                        return new
                        {
                            method,
                            crop,
                            grabMs,
                            path,
                            source = new { w = full.Width, h = full.Height },
                            region = region.HasValue ? RectJson(region.Value) : null,
                            stats = ImageStats.Of(image),
                        };
                    }
                }
            }
            finally
            {
                _captureLock.Release();
            }
        }

        private async Task<object> SampleAsync(HttpRequest r)
        {
            var method = CaptureMethod(r);
            var crop = r.Q("crop", "flash");
            int count = Math.Max(2, Math.Min(20, r.QInt("count", 4)));
            int interval = Math.Max(50, Math.Min(5000, r.QInt("interval", 500)));
            var label = Regex.Replace(r.Q("label", ""), "[^A-Za-z0-9_-]", "");

            var frames = new List<object>();
            var hashes = new HashSet<string>();
            string first = null, last = null;
            await _captureLock.WaitAsync().ConfigureAwait(false);
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (i > 0) await Task.Delay(interval).ConfigureAwait(false);
                    var sw = Stopwatch.StartNew();
                    try
                    {
                        using (var full = await GrabAsync(method).ConfigureAwait(false))
                        {
                            long grabMs = sw.ElapsedMilliseconds;
                            var region = await ResolveCropAsync(crop).ConfigureAwait(false);
                            using (var image = region.HasValue ? Capture.Crop(full, region.Value) : (Bitmap)full.Clone())
                            {
                                var stats = ImageStats.Of(image);
                                hashes.Add(stats.Hash);
                                if (i == 0) first = Save(image, method, label + "first");
                                if (i == count - 1) last = Save(image, method, label + "last");
                                frames.Add(new { i, grabMs, stats.Hash, stats.MeanLuma, stats.StdLuma, stats.DarkRatio, stats.SampledColors });
                            }
                        }
                    }
                    catch (Exception e) when (!(e is HttpError))
                    {
                        frames.Add(new { i, grabMs = sw.ElapsedMilliseconds, error = e.Message });
                    }
                }
            }
            finally
            {
                _captureLock.Release();
            }

            _form.SetStatus($"采样 {method} ×{count}：{hashes.Count} 种画面");
            return new { method, crop, count, interval, distinctFrames = hashes.Count, frames, first, last, window = WindowInfo() };
        }

        private static string CaptureMethod(HttpRequest r)
        {
            var method = r.Q("method") ?? throw new HttpError(400, "missing 'method' (" + string.Join("|", CaptureMethods) + ")");
            if (!CaptureMethods.Contains(method)) throw new HttpError(400, "method must be one of " + string.Join("|", CaptureMethods));
            return method;
        }

        private async Task<Bitmap> GrabAsync(string method)
        {
            var view = _form.View;
            switch (method)
            {
                case "osr":
                    if (!view.IsOffscreen) throw new HttpError(400, "osr capture needs --mode osr");
                    return view.CaptureFrame() ?? throw new HttpError(503, "no frame rendered yet");
                case "devtools":
                    var json = await _devtools.CallAsync(RequireBrowser(), "Page.captureScreenshot",
                        new Dictionary<string, object> { ["format"] = "png" }, TimeSpan.FromSeconds(8)).ConfigureAwait(false);
                    return Capture.FromPng(Convert.FromBase64String((string)Json.Parse(json)["data"]));
                case "printwindow":
                    return Ui(() => Capture.PrintTopLevel(view.Control));
                case "printwindow-child":
                    if (view.IsOffscreen) throw new HttpError(400, "osr mode has no CEF child window");
                    return Ui(() => Capture.PrintChild(view.Control));
                case "screen":
                    return Ui(() => Capture.FromScreen(view.Control));
                case "form":
                    return Ui(() => Capture.PrintForm(_form));
                default:
                    throw new HttpError(400, "unknown capture method " + method);
            }
        }

        private async Task<Rectangle?> ResolveCropAsync(string crop)
        {
            if (crop == "full") return null;
            if (crop == "flash") return (await RequireFlashAsync().ConfigureAwait(false)).StageRect;
            var parts = crop.Split(',');
            if (parts.Length == 4 && parts.All(p => int.TryParse(p, out _)))
            {
                var v = parts.Select(int.Parse).ToArray();
                return new Rectangle(v[0], v[1], v[2], v[3]);
            }
            throw new HttpError(400, "crop must be flash|full|x,y,w,h");
        }

        private string Save(Bitmap image, string method, string label)
        {
            var name = $"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{_options.Mode.ToString().ToLowerInvariant()}_{method}{(label.Length > 0 ? "_" + label : "")}.png";
            var path = Path.Combine(_paths.Captures, name);
            image.Save(path, ImageFormat.Png);
            return path;
        }

        // ---------- input ----------

        private async Task<object> PointerAsync(HttpRequest r, bool click)
        {
            if (_grind.Running || _grind.OperationLock.CurrentCount == 0)
                throw new HttpError(409, "Manual pointer input is disabled while automation owns this account.");
            double x = r.QDouble("x"), y = r.QDouble("y");
            var space = r.Q("space", "flash");
            var method = r.Q("method", "cef");
            var buttonName = r.Q("button", "left");
            if (buttonName != "left" && buttonName != "right") throw new HttpError(400, "button must be left|right");
            if (buttonName == "right" && (!click || method != "cef")) throw new HttpError(400, "right button requires a cef click");
            var button = buttonName == "right" ? MouseButtonType.Right : MouseButtonType.Left;

            if (space == "flash")
            {
                var origin = (await RequireFlashAsync().ConfigureAwait(false)).StageRect.Location;
                x += origin.X;
                y += origin.Y;
            }
            else if (space != "page")
            {
                throw new HttpError(400, "space must be flash|page");
            }

            int px = (int)Math.Round(x), py = (int)Math.Round(y);
            switch (method)
            {
                case "cef":
                    var host = RequireBrowser().GetHost();
                    if (click) await Input.ClickCefAsync(host, px, py, button).ConfigureAwait(false);
                    else Input.MoveCef(host, px, py);
                    break;
                case "post":
                    if (_form.View.IsOffscreen) throw new HttpError(400, "post needs a real render widget window (windowed mode)");
                    var hwnd = Ui(() => Native.FindDescendant(_form.View.Control.Handle, "Chrome_RenderWidgetHostHWND"));
                    if (hwnd == IntPtr.Zero) throw new HttpError(409, "Chrome_RenderWidgetHostHWND not found");
                    if (click) await Input.ClickPostAsync(hwnd, px, py).ConfigureAwait(false);
                    else Input.MovePost(hwnd, px, py);
                    break;
                default:
                    throw new HttpError(400, "method must be cef|post");
            }

            var action = click ? "click" : "move";
            Log.Info($"{action} {method} page=({px},{py})");
            _form.SetStatus($"{action} {method} ({px},{py})");
            return new { action, method, space, page = new { x = px, y = py }, window = WindowInfo() };
        }

        private async Task<object> DragAsync(HttpRequest r)
        {
            if (_grind.Running || !await _grind.OperationLock.WaitAsync(0).ConfigureAwait(false))
                throw new HttpError(409, "Manual pointer input is disabled while automation owns this account.");
            try
            {
                var space = r.Q("space", "flash");
                var offset = Point.Empty;
                if (space == "flash") offset = (await RequireFlashAsync().ConfigureAwait(false)).StageRect.Location;
                else if (space != "page") throw new HttpError(400, "space must be flash|page");
                int Coordinate(string key, int origin)
                {
                    double value = r.QDouble(key);
                    if (double.IsNaN(value) || double.IsInfinity(value) || Math.Abs(value) > 10000)
                        throw new HttpError(400, "Drag coordinates must be finite and within 10000 pixels.");
                    return (int)Math.Round(value) + origin;
                }
                var from = new Point(Coordinate("fromX", offset.X), Coordinate("fromY", offset.Y));
                var to = new Point(Coordinate("toX", offset.X), Coordinate("toY", offset.Y));
                await Input.DragCefAsync(RequireBrowser().GetHost(), from.X, from.Y, to.X, to.Y).ConfigureAwait(false);
                Log.Info($"drag cef page=({from.X},{from.Y})->({to.X},{to.Y})");
                return new { action = "drag", method = "cef", space, from = new { x = from.X, y = from.Y }, to = new { x = to.X, y = to.Y } };
            }
            finally { _grind.OperationLock.Release(); }
        }

        // ---------- window / navigation ----------

        private async Task<object> WindowAsync(HttpRequest r)
        {
            var state = r.Q("state") ?? throw new HttpError(400, "missing 'state'");
            Action action;
            switch (state)
            {
                case "minimize": action = () => _form.WindowState = FormWindowState.Minimized; break;
                case "normal": action = () => _form.WindowState = FormWindowState.Normal; break;
                case "offscreen":
                    action = () =>
                    {
                        _form.WindowState = FormWindowState.Normal;
                        _form.MoveOffscreen();
                    };
                    break;
                case "onscreen": action = _form.MoveOnscreen; break;
                case "bottom":
                    action = () => Native.SetWindowPos(_form.Handle, Native.HWND_BOTTOM, 0, 0, 0, 0,
                        Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
                    break;
                case "top":
                    action = () =>
                    {
                        if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
                        _form.Show();
                        _form.Activate();
                    };
                    break;
                default: throw new HttpError(400, "state must be normal|minimize|offscreen|onscreen|bottom|top");
            }

            Ui(() => { action(); return 0; });
            await Task.Delay(400).ConfigureAwait(false); // let the compositor react before the caller samples
            _form.SetStatus("窗口 → " + state);
            return new { state, window = WindowInfo() };
        }

        private object Navigate(HttpRequest r)
        {
            var url = r.Q("url") ?? throw new HttpError(400, "missing 'url'");
            if (!UrlText.IsWeb(url)) throw new HttpError(400, "url must be http(s)");
            Volatile.Write(ref _flash, null);
            _form.View.WebBrowser.Load(url);
            return new { loading = UrlText.Redact(url) };
        }

        private Task<object> TypeAsync(HttpRequest r) => Guard<object>(async () =>
        {
            var text = r.Q("text", "");
            if (text.Length == 0 || text.Length > 32 || !text.All(char.IsLetterOrDigit)) throw new ArgumentException("text: 1-32 letters or digits");
            var io = await _grind.CreateGameIoAsync().ConfigureAwait(false);
            await io.TypeAsync(text).ConfigureAwait(false);
            return new { typed = text.Length };
        });

        private object Reload()
        {
            Volatile.Write(ref _flash, null);
            _form.View.WebBrowser.Reload();
            return new { reloading = true };
        }

        private object Hosts() => new
        {
            hosts = _form.Hooks.Hosts.OrderByDescending(p => p.Value).Select(p => new { host = p.Key, requests = p.Value }).ToList(),
        };

        // ---------- recording ----------

        private async Task<object> RecordAsync(HttpRequest r)
        {
            switch (r.Q("state"))
            {
                case null: return RecordingJson(Volatile.Read(ref _recorder), active: true);
                case "start": return RecordingJson(await StartRecordingAsync(Math.Max(500, r.QInt("interval", 3000))).ConfigureAwait(false), active: true);
                case "stop": return RecordingJson(await StopRecordingAsync().ConfigureAwait(false), active: false);
                default: throw new HttpError(400, "state must be start|stop");
            }
        }

        private async Task<string> ToggleRecordingAsync()
        {
            if (Volatile.Read(ref _recorder) != null)
            {
                var stopped = await StopRecordingAsync().ConfigureAwait(false);
                return $"录制已停止：{stopped.Presses} 次点击，{stopped.Frames} 帧 → {stopped.Directory}";
            }
            var started = await StartRecordingAsync(3000).ConfigureAwait(false);
            return "录制中 → " + started.Directory;
        }

        private async Task<Recorder> StartRecordingAsync(int intervalMs)
        {
            if (!(_form.View is OsrView osr)) throw new HttpError(400, "recording needs --mode osr");
            await _recordLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_recorder != null) throw new HttpError(409, "already recording to " + _recorder.Directory);
                // Always re-locate: the game iframe may have been reloaded since the last /locate.
                var flash = (await FlashLocator.LocateAsync(RequireBrowser()).ConfigureAwait(false)).Best
                            ?? throw new HttpError(409, "no Flash object on the page yet");
                Volatile.Write(ref _flash, flash);
                var dir = Path.Combine(_paths.Captures, $"rec-{DateTime.Now:yyyyMMdd-HHmmss}");
                var recorder = new Recorder(osr, flash.StageRect, dir, intervalMs);
                Volatile.Write(ref _recorder, recorder);
                Log.Info($"recording started → {dir} stage={Fmt(flash.PageRect)}");
                _form.SetRecording(true);
                return recorder;
            }
            finally
            {
                _recordLock.Release();
            }
        }

        private async Task<Recorder> StopRecordingAsync()
        {
            await _recordLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var recorder = _recorder ?? throw new HttpError(409, "not recording");
                recorder.Dispose();
                Volatile.Write(ref _recorder, null);
                Log.Info($"recording stopped: {recorder.Presses} presses, {recorder.Frames} frames, {recorder.ElapsedMs} ms");
                _form.SetRecording(false);
                return recorder;
            }
            finally
            {
                _recordLock.Release();
            }
        }

        private static object RecordingJson(Recorder r, bool active) => r == null
            ? (object)new { recording = false }
            : new { recording = active, directory = r.Directory, presses = r.Presses, frames = r.Frames, elapsedMs = r.ElapsedMs };

        // ---------- automation (shared with the operation panel through GrindController) ----------

        private static async Task<T> Guard<T>(Func<Task<T>> action)
        {
            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (ArgumentException e) { throw new HttpError(400, e.Message); }
            catch (NotSupportedException e) { throw new HttpError(400, e.Message); }
            catch (FormatException e) { throw new HttpError(400, e.Message); }
            catch (FileNotFoundException e) { throw new HttpError(404, e.Message); }
            catch (KeyNotFoundException e) { throw new HttpError(404, e.Message); }
            catch (InvalidOperationException e) { throw new HttpError(409, e.Message); }
        }

        private Task<object> DetectAsync(HttpRequest r) => Guard<object>(async () =>
        {
            var detector = _grind.Detector();
            Automation.StageFrame frame;
            var file = r.Q("file");
            if (file != null)
            {
                // Offline check against a saved stage-sized screenshot (e.g. a recording frame).
                using (var bmp = new Bitmap(file))
                    frame = Automation.StageFrame.FromBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            }
            else
            {
                frame = (await _grind.CreateGameIoAsync().ConfigureAwait(false)).Grab();
            }
            var screen = detector.Detect(frame);
            int slots = Math.Max(1, Math.Min(18, r.QInt("slots", 5)));
            var layout = new Automation.BattleLayout(slots);
            return new
            {
                screen = screen.ToString(),
                secondSeat = detector.SecondSeat(frame).ToString(),
                scores = detector.Scores(frame),
                slotValues = screen == Automation.GameScreen.Battle
                    ? Enumerable.Range(1, slots).Select(i => Math.Round(layout.SlotValue(frame, i), 2)).ToList()
                    : null,
            };
        });

        private Task<object> GrindAsync(HttpRequest r) => Guard<object>(async () =>
        {
            switch (r.Q("state"))
            {
                case null:
                    return GrindJson();
                case "stop":
                    await _grind.StopAndWaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    return GrindJson();
                case "finish-round":
                    _grind.FinishAfterRound();
                    return GrindJson();
                case "start":
                    var request = ParseGrindRequest(r);
                    await _grind.StartAsync(request).ConfigureAwait(false);
                    await Task.Delay(300).ConfigureAwait(false);
                    return GrindJson();
                default:
                    throw new HttpError(400, "state must be start|finish-round|stop");
            }
        });

        private static GrindRequest ParseGrindRequest(HttpRequest r, DuoRole? duoRole = null)
        {
            var request = new GrindRequest
            {
                Level = r.Q("level"),
                Track = r.Q("track") ?? throw new HttpError(400, "missing 'track'"),
                Rounds = r.QInt("rounds", 1),
                Shovel = r.Flag("shovel"),
                Boss = r.Flag("boss"),
                AutoFlame = r.Flag("autoflame"),
                DuoRole = duoRole,
                DuoRoom = duoRole.HasValue ? r.QInt("room", 0) : 0,
                DuoPassword = duoRole.HasValue ? r.Q("password") : null,
            };
            if (r.Q("cards") is string cards) request.Cards = ParseCardPairs(cards);
            else
            {
                request.SlotCount = r.QInt("slots", 0);
                request.SlotMap = ParseSlotMap(r.Q("map") ?? throw new HttpError(400, "missing 'cards' or 'map'"));
            }
            return request;
        }

        private Task<object> DuoAsync(HttpRequest r) => Guard<object>(async () =>
        {
            switch (r.Q("state"))
            {
                case null:
                    _grind.Duo?.Touch();
                    return DuoJson();
                case "plan":
                    var planRequest = ParseGrindRequest(r);
                    var selected = planRequest.Cards != null
                        ? planRequest.Cards.Select(c => c.track)
                        : planRequest.SlotMap.Keys;
                    var planLevel = string.IsNullOrWhiteSpace(planRequest.Level) ? null : _grind.FindLevel(planRequest.Level);
                    var plan = DuoTrackPlan.Build(_grind.PrepareTrack(planRequest.Track, planLevel), selected, planRequest.Shovel);
                    return new { player = plan.Player.ToString(), cells = plan.Cells.Select(c => c.ToString()).ToList() };
                case "start":
                    DuoRole role;
                    if (r.Q("role") == "host") role = DuoRole.Host;
                    else if (r.Q("role") == "guest") role = DuoRole.Guest;
                    else throw new HttpError(400, "role must be host or guest");
                    await _grind.StartAsync(ParseGrindRequest(r, role)).ConfigureAwait(false);
                    return DuoJson();
                case "release":
                    _grind.ReleaseDuo(r.Q("phase"), r.Q("run"), r.QInt("round", 0));
                    return DuoJson();
                case "stop":
                    if (_grind.Duo != null) await _grind.StopAndWaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                    return DuoJson();
                default:
                    throw new HttpError(400, "state must be start|release|stop");
            }
        });

        private object DuoJson()
        {
            var g = _grind.Current;
            var d = _grind.Duo;
            if (g == null || d == null) return new { running = false };
            return new
            {
                running = _grind.Running,
                profile = _options.Profile,
                run = d.RunId,
                role = d.Role.ToString().ToLowerInvariant(),
                room = d.RoomNumber,
                state = g.State,
                round = g.RoundsDone + 1,
                roundsDone = g.RoundsDone,
                bossRoundsDone = g.BossRoundsDone,
                screen = g.LastScreen.ToString(),
                runDirectory = g.RunDirectory,
                messages = g.Messages.Skip(Math.Max(0, g.Messages.Count - 20)).ToList(),
            };
        }

        private object GrindJson()
        {
            var g = _grind.Current;
            if (g == null) return new { running = false };
            var messages = g.Messages;
            return new
            {
                running = _grind.Running,
                state = g.State,
                roundsDone = g.RoundsDone,
                bossRoundsDone = g.BossRoundsDone,
                finishAfterRoundRequested = g.FinishAfterRoundRequested,
                screen = g.LastScreen.ToString(),
                runDirectory = g.RunDirectory,
                messages = messages.Skip(Math.Max(0, messages.Count - 40)).ToList(),
            };
        }

        private Task<object> Track(HttpRequest r) => Guard<object>(() =>
        {
            var level = r.Q("level");
            var preflight = _grind.PrepareTrack(r.Q("path") ?? throw new HttpError(400, "missing 'path'"),
                level == null ? null : _grind.FindLevel(level));
            var track = preflight.Track;
            IEnumerable<string> selected = null;
            if (r.Q("cards") is string cards) selected = ParseCardPairs(cards).Select(c => c.track);
            else if (r.Q("map") is string map) selected = ParseSlotMap(map).Keys;
            var report = preflight.Inspect(selected, r.Flag("shovel"), r.Q("cards") != null ? Automation.DeckBuilder.DeckSlots : 18);
            return Task.FromResult<object>(new
            {
                track.Path,
                header = track.Header,
                player1 = track.Player1Cells.Select(c => c.ToString()).ToList(),
                waves = track.Waves.Select(w => new
                {
                    w.Label,
                    cards = w.Cards.Select(c => new
                    {
                        c.Name,
                        c.CooldownSec,
                        c.Layer,
                        c.Flags,
                        Conditions = c.Conditions.Raw,
                        steps = c.Steps.Count,
                        first = c.Steps.Take(4).Select(s => $"{s.TimeText}s@{s.Cell}").ToList(),
                    }).ToList(),
                }).ToList(),
                track.Warnings,
                compatibility = new { report.CanStart, report.Diagnostics },
                effectiveDeck = preflight.DeckCards.Select(c => new { c.Name, c.Layer, c.Flags, steps = c.Steps.Count }).ToList(),
            });
        });

        private Task<object> RecordTrackAsync(HttpRequest r) => Guard<object>(async () =>
        {
            var dir = _grind.RecordingPath(r.Q("rec"));
            var battles = await Task.Run(() => _grind.ReadRecording(dir)).ConfigureAwait(false);
            var best = battles.OrderByDescending(b => b.Placed).First();
            var text = best.ToTrackText($"由录制 {Path.GetFileName(dir)} 生成");
            var saved = r.Q("save") is string name ? _grind.SaveUserTrack(name, text, r.Flag("overwrite")) : null;
            return new
            {
                recording = dir,
                battles = battles.Select(b => new
                {
                    b.StartSec,
                    character = b.Character?.ToString(),
                    cards = b.Cards.Select(c => new { c.Slot, c.Name, c.Learned, c.CooldownSec, c.Layer, cells = string.Join(" ", c.Cells) }).ToList(),
                    shovel = b.Shovel.Select(s => $"{s.TimeText}s@{s.Cell}").ToList(),
                    b.NotPlaced,
                    b.Unverified,
                    b.Log,
                }).ToList(),
                text,
                saved,
            };
        });

        private Task<object> PortalAsync(HttpRequest r) => Guard<object>(async () =>
        {
            var browser = RequireBrowser();
            var address = _form.View.WebBrowser.Address;
            if (!Uri.TryCreate(address, UriKind.Absolute, out var uri) ||
                uri.Host != "my.4399.com" || !uri.AbsolutePath.EndsWith("/wd-air", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Open the 4399 server selection page first.");
            var scan = await PortalSelection.ScanAsync(browser).ConfigureAwait(false);
            if (r.Q("state") == "inspect") return new { scan.inputs, scan.entries };
            if (r.Q("state") != "select") throw new HttpError(400, "state must be inspect|select");
            var server = r.Q("server") ?? "";
            if (!Regex.IsMatch(server, "^[1-9][0-9]{0,3}$")) throw new HttpError(400, "server must be a 1-4 digit number");
            var pair = PortalSelection.FindPair(scan);
            var host = browser.GetHost();
            await Input.ClickCefAsync(host, pair.input.CenterX, pair.input.CenterY).ConfigureAwait(false);
            await Input.ReplaceCefTextAsync(host, server).ConfigureAwait(false);
            await Task.Delay(250).ConfigureAwait(false);
            await Input.ClickCefAsync(host, pair.entry.CenterX, pair.entry.CenterY).ConfigureAwait(false);
            var deadline = DateTime.UtcNow.AddSeconds(15);
            while (DateTime.UtcNow < deadline)
            {
                var next = _form.View.WebBrowser.Address ?? "";
                if (Uri.TryCreate(next, UriKind.Absolute, out var entered) &&
                    entered.AbsolutePath.IndexOf("tpl-sid-" + server + "-", StringComparison.OrdinalIgnoreCase) >= 0)
                    return new { server, entered = true };
                await Task.Delay(250).ConfigureAwait(false);
            }
            throw new InvalidOperationException("The server page did not confirm entry into server " + server + ". Check the open game window.");
        });

        private Task<object> InventoryAsync(HttpRequest r) => Guard<object>(async () =>
        {
            var state = r.Q("state", "capture");
            if (state == "capture")
            {
                var snapshot = await _grind.CaptureInventoryAsync().ConfigureAwait(false);
                using (var bitmap = snapshot.Frame.ToBitmap())
                using (var stream = new MemoryStream())
                {
                    bitmap.Save(stream, ImageFormat.Png);
                    return new { id = snapshot.Id, png = Convert.ToBase64String(stream.ToArray()) };
                }
            }
            if (state != "learn") throw new HttpError(400, "Inventory state must be capture or learn.");
            // UTF-8 base64 avoids expanding each Chinese character to nine URL characters in a full-page batch.
            var json = r.Q("items64") is string packed ? Encoding.UTF8.GetString(Convert.FromBase64String(packed)) : r.Q("items");
            var items = Json.Deserialize<List<InventorySelection>>(json ?? throw new HttpError(400, "Missing items or items64."));
            var learned = await _grind.LearnInventoryAsync(r.Q("id"), items, r.Flag("replace")).ConfigureAwait(false);
            return new { learned, cards = _grind.Cards.Names };
        });

        private Task<object> CardsAsync(HttpRequest r) => Guard<object>(async () =>
        {
            if (r.Q("learn") is string learn)
            {
                // Names for deck bar slots 1..n; an empty name skips that slot.
                var (frame, filled) = await _grind.ReadDeckAsync().ConfigureAwait(false);
                var names = learn.Split(new[] { ',', '，' });
                var learned = new List<string>();
                for (int i = 0; i < names.Length; i++)
                {
                    var name = names[i].Trim();
                    if (name.Length == 0) continue;
                    if (!filled.Contains(i + 1)) throw new HttpError(409, $"卡组第 {i + 1} 槽是空的");
                    _grind.LearnCard(name, frame, i + 1);
                    learned.Add($"{name} ← 第 {i + 1} 槽");
                }
                return new { learned, directory = _grind.CardsDirectory, cards = _grind.Cards.Names };
            }
            return new { directory = _grind.CardsDirectory, cards = _grind.Cards.Names };
        });

        private Task<object> DeckAsync(HttpRequest r) => Guard<object>(async () =>
        {
            var names = (r.Q("cards") ?? throw new HttpError(400, "missing 'cards'"))
                .Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();
            var log = new List<string>();
            await _grind.BuildDeckAsync(names, log.Add).ConfigureAwait(false);
            return new { deck = names, log };
        });

        private static List<(string track, string mine)> ParseCardPairs(string value)
        {
            var result = new List<(string, string)>();
            foreach (var pair in value.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(new[] { ':', '：', '=' }, 2);
                var track = parts[0].Trim();
                var mine = parts.Length == 2 ? parts[1].Trim() : track;
                if (track.Length == 0 || mine.Length == 0) throw new HttpError(400, $"bad cards entry '{pair}' (want 轨道卡名:我的卡名)");
                result.Add((track, mine));
            }
            if (result.Count == 0 || result.Count > 18) throw new HttpError(400, "cards must list 1-18 entries");
            return result;
        }

        private static Dictionary<string, int> ParseSlotMap(string map)
        {
            var result = new Dictionary<string, int>();
            foreach (var pair in map.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = pair.Split(new[] { ':', '：', '=' }, 2);
                if (parts.Length != 2 || !int.TryParse(parts[1].Trim(), out var slot) || slot < 1)
                    throw new HttpError(400, $"bad map entry \"{pair}\" (want 卡名:槽位)");
                result[parts[0].Trim()] = slot;
            }
            return result;
        }

        // ---------- helpers ----------

        private IBrowser RequireBrowser()
        {
            if (_form.View is OsrView offscreen && !offscreen.RendererAvailable)
                throw new HttpError(409, "Browser renderer is unavailable. Reload or reopen the game before sending input.");
            return _form.View.BrowserOrNull ?? throw new HttpError(409, "browser not initialized yet");
        }

        private T Ui<T>(Func<T> func) => _form.InvokeRequired ? (T)_form.Invoke(func) : func();

        private object WindowInfo() => Ui(() =>
        {
            var handle = _form.Handle;
            var view = _form.View.Control;
            var origin = view.PointToScreen(Point.Empty);
            return (object)new
            {
                state = _form.WindowState.ToString(),
                visible = _form.Visible,
                iconic = Native.IsIconic(handle),
                foreground = Native.GetForegroundWindow() == handle,
                bounds = RectJson(_form.Bounds),
                viewport = new { x = origin.X, y = origin.Y, w = view.ClientSize.Width, h = view.ClientSize.Height },
            };
        });

        private static object PluginJson(PluginInfo p, bool full) => p == null ? null : new
        {
            rect = RectJson(p.StageRect),
            p.Tag,
            p.Type,
            src = UrlText.Redact(p.Src, full),
            frame = p.FrameId,
            frameUrl = UrlText.Redact(p.FrameUrl, full),
            p.OffsetGuessed,
            p.LooksLikeFlash,
        };

        private static object RectJson(Rectangle r) => new { x = r.X, y = r.Y, w = r.Width, h = r.Height };

        private static string Fmt(RectangleF r) => $"{r.X:0},{r.Y:0} {r.Width:0}x{r.Height:0}";
    }
}
