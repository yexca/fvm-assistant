using System;
using System.Threading;
using System.Threading.Tasks;
using FvmAssistant.Automation;

namespace FvmAssistant
{
    /// <summary>
    /// Outside a grind run, reloads the page when the game loader sits at "加载中 100%" — a login
    /// server that did not answer (packet loss on the path to mainland China). The grind loop has
    /// its own recovery, so this stays quiet while a run is active.
    /// </summary>
    internal sealed class LoaderWatchdog : IDisposable
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(3);
        private static readonly TimeSpan StuckAfter = TimeSpan.FromSeconds(30);
        private const int MaxReloads = 3;

        private readonly MainForm _form;
        private readonly GrindController _grind;
        private readonly Timer _timer;
        private int _busy;
        private DateTime? _stuckSince;
        private int _reloads;
        private bool _disabled;
        private string _lastError;

        public LoaderWatchdog(MainForm form, GrindController grind)
        {
            _form = form;
            _grind = grind;
            _timer = new Timer(_ => _ = TickAsync(), null, Interval, Interval);
        }

        private async Task TickAsync()
        {
            if (_disabled || Interlocked.Exchange(ref _busy, 1) == 1) return;
            try
            {
                if (_grind.Running)
                {
                    _stuckSince = null;
                    return;
                }

                ScreenDetector detector;
                try
                {
                    detector = _grind.Detector();
                }
                catch (Exception e)
                {
                    _disabled = true; // no 轨道编辑器 data or templates: nothing to detect with
                    Log.Warn("loader watchdog disabled: " + e.Message);
                    return;
                }

                OsrGameIo io;
                try
                {
                    io = await _grind.CreateGameIoAsync().ConfigureAwait(false);
                }
                catch (InvalidOperationException)
                {
                    _stuckSince = null; // no game on the page (server list, black page, …)
                    return;
                }

                var screen = detector.Detect(io.Grab());
                if (screen != GameScreen.Loading100)
                {
                    if (screen != GameScreen.Unknown) _reloads = 0; // logged in
                    _stuckSince = null;
                    return;
                }

                _stuckSince = _stuckSince ?? DateTime.UtcNow;
                if (DateTime.UtcNow - _stuckSince < StuckAfter) return;
                _stuckSince = null;
                if (++_reloads > MaxReloads)
                {
                    if (_reloads == MaxReloads + 1)
                    {
                        Log.Warn("loader stuck at 100%; giving up on automatic reloads");
                        _form.SetStatus("游戏一直停在“加载中 100%”，已自动刷新 3 次仍未进入，请稍后手动刷新");
                    }
                    return;
                }
                Log.Warn($"loader stuck at 100% for {StuckAfter.TotalSeconds:0} s; reloading the page (attempt {_reloads}/{MaxReloads})");
                _form.SetStatus($"停在“加载中 100%”超过 {StuckAfter.TotalSeconds:0} 秒，自动刷新（第 {_reloads} 次）");
                io.Reload();
            }
            catch (Exception e)
            {
                if (e.Message != _lastError) Log.Warn("loader watchdog: " + e.Message);
                _lastError = e.Message;
            }
            finally
            {
                Volatile.Write(ref _busy, 0);
            }
        }

        public void Dispose() => _timer.Dispose();
    }
}
