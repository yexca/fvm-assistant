using System;

namespace FvmAssistant
{
    internal sealed class RendererFrameHealth
    {
        private readonly object _gate = new object();
        private bool _available = true;
        private bool _navigationStarted;
        private bool _navigationCompleted;
        private string _reason;

        internal bool Available { get { lock (_gate) return _available; } }
        internal string FailureReason { get { lock (_gate) return _reason; } }

        internal void Terminated(string reason)
        {
            lock (_gate)
            {
                _available = false;
                _navigationStarted = _navigationCompleted = false;
                _reason = reason;
            }
        }

        internal void MainFrameStarted()
        {
            lock (_gate)
            {
                if (_available) return;
                _navigationStarted = true;
                _navigationCompleted = false;
            }
        }

        internal void MainFrameCompleted(bool succeeded)
        {
            lock (_gate)
            {
                if (!_available && _navigationStarted) _navigationCompleted = succeeded;
            }
        }

        internal void Painted(bool popup)
        {
            lock (_gate)
            {
                if (popup || _available || !_navigationCompleted) return;
                _available = true;
                _reason = null;
            }
        }

        internal void RequireAvailable()
        {
            lock (_gate)
                if (!_available) throw new InvalidOperationException(
                    "The browser renderer is unavailable (" + _reason + "). Reload or reopen the game before sending input.");
        }
    }
}
