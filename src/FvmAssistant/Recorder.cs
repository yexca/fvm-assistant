using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace FvmAssistant
{
    /// <summary>
    /// Records a manual play session in the off-screen view: every mouse press/release (and
    /// throttled moves) with page and stage coordinates, the stage frame at each press, and a
    /// frame every few seconds. Keyboard input is deliberately not recorded (room passwords etc.).
    /// </summary>
    internal sealed class Recorder : IDisposable
    {
        private const int MoveLogIntervalMs = 100;

        private readonly OsrView _view;
        private readonly Rectangle _stage;
        private readonly StreamWriter _log;
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private readonly System.Threading.Timer _timer;
        private readonly object _gate = new object();
        private int _seq;
        private int _presses;
        private long _lastMoveMs = -MoveLogIntervalMs;
        private bool _stopped;

        public Recorder(OsrView view, Rectangle stage, string directory, int frameIntervalMs)
        {
            _view = view;
            _stage = stage;
            Directory = directory;
            System.IO.Directory.CreateDirectory(directory);
            _log = new StreamWriter(Path.Combine(directory, "events.jsonl"), false, new UTF8Encoding(false)) { AutoFlush = true };
            Write(new { t = 0, kind = "start", stage = new { x = _stage.X, y = _stage.Y, w = _stage.Width, h = _stage.Height } });

            _view.UserMouse += OnUserMouse;
            _timer = new System.Threading.Timer(_ => SaveFrame("tick"), null, 0, frameIntervalMs);
        }

        public string Directory { get; }
        public int Presses => _presses;
        public int Frames => _seq;
        public long ElapsedMs => _clock.ElapsedMilliseconds;

        private void OnUserMouse(string kind, MouseEventArgs e)
        {
            long t = _clock.ElapsedMilliseconds;
            if (kind == "move")
            {
                if (t - _lastMoveMs < MoveLogIntervalMs) return;
                _lastMoveMs = t;
            }

            string frame = null;
            if (kind == "down")
            {
                Interlocked.Increment(ref _presses);
                frame = SaveFrame("down");
            }

            Write(new
            {
                t,
                kind,
                button = kind == "move" ? null : e.Button.ToString(),
                page = new { x = e.X, y = e.Y },
                stage = new { x = e.X - _stage.X, y = e.Y - _stage.Y },
                inStage = _stage.Contains(e.X, e.Y),
                frame,
            });
        }

        /// <summary>Grabs the current frame synchronously (cheap copy) and encodes it in the background.</summary>
        private string SaveFrame(string reason)
        {
            lock (_gate)
            {
                if (_stopped) return null;
            }

            Bitmap full;
            try
            {
                full = _view.CaptureFrame();
            }
            catch (Exception e)
            {
                Log.Warn("recorder frame grab failed: " + e.Message);
                return null;
            }
            if (full == null) return null;

            int seq = Interlocked.Increment(ref _seq);
            long t = _clock.ElapsedMilliseconds;
            var name = $"{seq:0000}_{t:0000000}ms_{reason}.png";
            Task.Run(() =>
            {
                try
                {
                    using (full)
                    using (var stage = Capture.Crop(full, _stage))
                        stage.Save(Path.Combine(Directory, name), ImageFormat.Png);
                }
                catch (Exception e)
                {
                    Log.Warn($"recorder could not save {name}: {e.Message}");
                }
            });
            if (reason == "tick") Write(new { t, kind = "tick", frame = name });
            return name;
        }

        private void Write(object entry)
        {
            lock (_gate)
            {
                if (_stopped) return;
                _log.WriteLine(Json.Serialize(entry));
            }
        }

        public void Dispose()
        {
            _view.UserMouse -= OnUserMouse;
            _timer.Dispose();
            Write(new { t = _clock.ElapsedMilliseconds, kind = "stop" });
            lock (_gate)
            {
                _stopped = true;
                _log.Dispose();
            }
        }
    }
}
